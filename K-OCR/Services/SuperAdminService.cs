using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _configuration;
    private readonly IPathService _pathService;

    public SuperAdminService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IEmailService emailService,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        IPathService pathService)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _pathService = pathService;
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

        var trimmedName = request.Name.Trim();

        // Check for duplicate display name or conflicting sanitized folder name
        var existingNames = await _dbContext.Organizations.Select(o => o.Name).ToArrayAsync();
        if (existingNames.Any(n => string.Equals(n, trimmedName, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateOrganizationNameException(
                $"An organization named \"{trimmedName}\" already exists.");

        var sanitizedNew = _pathService.SanitizeName(trimmedName);
        if (existingNames.Any(n => _pathService.SanitizeName(n) == sanitizedNew))
            throw new DuplicateOrganizationNameException(
                $"The organization name \"{trimmedName}\" would produce a folder name that conflicts with an existing organization. Choose a different name.");

        var organization = new Organization
        {
            Name = trimmedName,
            Description = request.Description?.Trim(),
            IsActive = true
        };

        await _dbContext.Organizations.AddAsync(organization);
        await _dbContext.SaveChangesAsync();

        bool allowDuplicateEmails = _configuration.GetValue<bool>("AllowDuplicateEmails", false);
        var existingUser = await _userManager.FindByEmailAsync(request.AdminEmail);

        ApplicationUser user;
        string invitationToken;
        string tempPassword;
        if (allowDuplicateEmails && existingUser is { IsGlobalAdmin: true })
            throw new InvalidOperationException(
                "A global admin account cannot be reassigned to a new organization.");

        bool reusingExistingUser = allowDuplicateEmails && existingUser is not null;

        if (reusingExistingUser)
        {
            // Re-use the existing account — point it at the new organisation.
            // Password is intentionally left unchanged; no reset token is generated.
            user = existingUser!;
            user.OrganizationId = organization.Id;
            user.IsOrganizationAdmin = true;
            user.FullName = request.AdminName.Trim();
            await _userManager.UpdateAsync(user);
            await _userManager.UpdateSecurityStampAsync(user);
            if (!await _userManager.IsInRoleAsync(user, RoleNames.OrganizationAdmin))
                await _userManager.AddToRoleAsync(user, RoleNames.OrganizationAdmin);
            tempPassword = string.Empty;
            invitationToken = string.Empty;
        }
        else
        {
            tempPassword = GenerateTemporaryPassword();
            user = new ApplicationUser
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
            invitationToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        }

        string setupLink = string.Empty;
        bool emailSent = false;

        if (!reusingExistingUser)
        {
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(invitationToken));
            var baseUrl = BuildBaseUrl();
            setupLink = $"{baseUrl}/account/set-password?userId={Uri.EscapeDataString(user.Id)}&token={encodedToken}";

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
        }

        // Create org folder on disk
        var orgPath = _pathService.GetOrgFolderPath(organization.Name);
        try
        {
            Directory.CreateDirectory(orgPath);
        }
        catch (Exception ex)
        {
            // Roll back: delete user and org
            if (!reusingExistingUser)
                await _userManager.DeleteAsync(user);
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException(
                $"Organization was created but the folder could not be created at \"{orgPath}\": {ex.Message}");
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

    public async Task DeleteOrganizationAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .Include(o => o.Users)
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        if (organization.IsActive)
            throw new InvalidOperationException("Organization must be revoked before it can be deleted.");

        foreach (var user in organization.Users)
            await _userManager.DeleteAsync(user);

        _dbContext.Organizations.Remove(organization);
        await _dbContext.SaveChangesAsync();

        // Delete org folder from disk
        var orgPath = _pathService.GetOrgFolderPath(organization.Name);
        if (Directory.Exists(orgPath))
            Directory.Delete(orgPath, recursive: true);
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
