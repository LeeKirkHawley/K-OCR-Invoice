using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Models.Api.OrganizationAdmin;
using K_OCR.Security;

namespace K_OCR.Services;

public class OrganizationAdminService : IOrganizationAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IEmailService _emailService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<OrganizationAdminService> _logger;

    private static readonly HashSet<string> _allowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.OrganizationAdmin,
        RoleNames.OrganizationValidator,
        RoleNames.OrganizationUser
    };

    public OrganizationAdminService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IEmailService emailService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<OrganizationAdminService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<OrganizationUserOverview[]> ListUsersAsync(string organizationId)
        => await GetOrgUsersAsync(organizationId);

    public async Task<OrganizationUserOverview[]> GetOrgUsersAsync(string organizationId)
    {
        var users = await _userManager.Users
            .Where(u => u.OrganizationId == organizationId)
            .ToListAsync();

        var overviews = new List<OrganizationUserOverview>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            overviews.Add(new OrganizationUserOverview
            {
                UserId   = user.Id,
                FullName = user.FullName,
                Email    = user.Email,
                IsActive = user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow,
                Roles    = roles.ToArray()
            });
        }

        return overviews.ToArray();
    }

    public async Task<InviteUserResult> InviteUserAsync(string organizationId, InviteUserRequest request)
    {
        var organization = await _dbContext.Organizations.FindAsync(organizationId);
        if (organization is null || !organization.IsActive)
            throw new InvalidOperationException("Organization not found or inactive.");

        var roleName = request.Role;
        if (!_allowedRoles.Contains(roleName))
            throw new ArgumentException($"Role '{roleName}' is not a valid organization role.");

        if (!await _roleManager.RoleExistsAsync(roleName))
            await _roleManager.CreateAsync(new IdentityRole(roleName));

        var tempPassword = GenerateTemporaryPassword();
        var user = new ApplicationUser
        {
            UserName       = request.Email,
            Email          = request.Email,
            FullName       = request.Name.Trim(),
            EmailConfirmed = false,
            OrganizationId = organizationId,
            IsOrganizationAdmin = string.Equals(roleName, RoleNames.OrganizationAdmin, StringComparison.OrdinalIgnoreCase),
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Unable to create user: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await _userManager.AddToRoleAsync(user, roleName);
        var invitationToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(invitationToken));
        var setupLink = BuildSetupLink(user.Id, encodedToken);

        bool emailSent = false;
        try
        {
            await _emailService.SendOrgUserInviteAsync(
                user.Email!, request.Name.Trim(), organization.Name, roleName, setupLink);
            emailSent = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send invite email to {Email}; returning setup link instead.", user.Email);
        }

        return new InviteUserResult
        {
            UserId          = user.Id,
            TempPassword    = tempPassword,
            InvitationToken = invitationToken,
            SetupLink       = setupLink,
            EmailSent       = emailSent
        };
    }

    public async Task RemoveUserAsync(string userId, string organizationId)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        if (!string.Equals(user.OrganizationId, organizationId, StringComparison.Ordinal))
            throw new InvalidOperationException("User does not belong to the specified organization.");

        var deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
            throw new InvalidOperationException(
                $"Unable to delete user: {string.Join("; ", deleteResult.Errors.Select(e => e.Description))}");
    }

    private string BuildSetupLink(string userId, string encodedToken)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null) return string.Empty;
        var baseUrl = $"{request.Scheme}://{request.Host}";
        return $"{baseUrl}/account/set-password?userId={Uri.EscapeDataString(userId)}&token={encodedToken}";
    }

    private static string GenerateTemporaryPassword()
    {
        var segment = Guid.NewGuid().ToString("N")[..6];
        return $"Kocr!{segment}";
    }
}
