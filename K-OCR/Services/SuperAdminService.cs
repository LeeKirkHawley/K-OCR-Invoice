using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Models.Api.SuperAdmin;
using K_OCR.Security;

namespace K_OCR.Services;

public class SuperAdminService : ISuperAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IEmailService _emailService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SuperAdminService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IEmailService emailService,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<OrganizationOverview[]> ListOrganizationsAsync()
    {
        return await _dbContext.Organizations
            .Select(org => new OrganizationOverview
            {
                OrganizationId = org.Id,
                Name = org.Name,
                Description = org.Description,
                IsActive = org.IsActive,
                CreatedAtUtc = org.CreatedAtUtc,
                UserCount = org.Users.Count
            })
            .ToArrayAsync();
    }

    public async Task<CreateOrganizationResult> CreateOrganizationAsync(CreateOrganizationRequest request)
    {
        await EnsureRolesAsync();

        var organization = new Organization
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            BaseDirectory = string.IsNullOrWhiteSpace(request.BaseDirectory) ? null : request.BaseDirectory.Trim(),
            IsActive = true
        };

        await _dbContext.Organizations.AddAsync(organization);
        await _dbContext.SaveChangesAsync();

        var tempPassword = GenerateTemporaryPassword();
        var user = new ApplicationUser
        {
            UserName = request.AdminEmail,
            Email = request.AdminEmail,
            FullName = request.AdminName.Trim(),
            EmailConfirmed = false,
            OrganizationId = organization.Id,
            IsOrganizationAdmin = true,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
        {
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException(
                $"Unable to create admin user: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await _userManager.AddToRoleAsync(user, RoleNames.OrganizationAdmin);
        var invitationToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(invitationToken));

        var baseUrl = BuildBaseUrl();
        var setupLink = $"{baseUrl}/account/set-password?userId={Uri.EscapeDataString(user.Id)}&token={encodedToken}";

        bool emailSent = false;
        try
        {
            await _emailService.SendOrgAdminInviteAsync(
                user.Email!, request.AdminName.Trim(), organization.Name, setupLink);
            emailSent = true;
        }
        catch
        {
            // Email failure is non-fatal — caller can share the setup link manually.
        }

        return new CreateOrganizationResult
        {
            OrganizationId = organization.Id,
            AdminUserId = user.Id,
            AdminEmail = user.Email!,
            AdminName = user.FullName ?? string.Empty,
            TempPassword = tempPassword,
            InvitationToken = invitationToken,
            SetupLink = setupLink,
            EmailSent = emailSent
        };
    }

    public async Task RevokeOrganizationAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .Include(o => o.Users)
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        organization.IsActive = false;

        foreach (var user in organization.Users)
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            await _userManager.UpdateSecurityStampAsync(user);
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task ReEnableOrganizationAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .Include(o => o.Users)
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        organization.IsActive = true;

        foreach (var user in organization.Users)
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.UpdateSecurityStampAsync(user);
        }

        await _dbContext.SaveChangesAsync();
    }

    private string BuildBaseUrl()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return "https://localhost";
        var req = ctx.Request;
        return $"{req.Scheme}://{req.Host}";
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.OrganizationAdmin, RoleNames.OrganizationUser })
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));
    }

    private static string GenerateTemporaryPassword()
    {
        var segment = Guid.NewGuid().ToString("N")[..6];
        return $"Kocr!{segment}";
    }
}
