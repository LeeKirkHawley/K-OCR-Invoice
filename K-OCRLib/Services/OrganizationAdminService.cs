using System.Text;
using K_OCRLib.Data;
using K_OCRLib.Identity;
using K_OCRLib.Models.Api.OrganizationAdmin;
using K_OCRLib.Security;
using K_OCRLib.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K_OCRLib.Services;

public class OrganizationAdminService : IOrganizationAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
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
        IEmailService emailService,
        ILogger<OrganizationAdminService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<OrganizationUserOverview[]> ListUsersAsync(string organizationId)
        => await GetOrgUsersAsync(organizationId);

    public async Task<OrganizationUserOverview?> GetOrganizationAdmin(string organizationId)
    {
        OrganizationUserOverview[] users = await GetOrgUsersAsync(organizationId);

        OrganizationUserOverview? admin = users?.Where(u => u.IsOrganizationAdmin == true).FirstOrDefault();

        return admin;
    }

    public async Task<OrganizationUserOverview[]> GetOrgUsersAsync(string organizationId)
    {
        var memberships = await _dbContext.UserOrganizationMemberships
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.OrganizationId == organizationId)
            .ToListAsync();
        var adminCount = memberships.Count(m => m.Role == RoleNames.OrganizationAdmin);

        return memberships
            .Select(m => new OrganizationUserOverview
            {
                UserId = m.UserId,
                FullName = m.User.FullName,
                Email = m.User.Email,
                IsActive = m.User.LockoutEnd is null || m.User.LockoutEnd <= DateTimeOffset.UtcNow,
                IsOrganizationAdmin = m.Role == RoleNames.OrganizationAdmin,
                CanChangeRole = m.Role != RoleNames.OrganizationAdmin || adminCount > 1,
                CanRemove = m.Role != RoleNames.OrganizationAdmin || adminCount > 1,
                Roles = [m.Role]
            })
            .OrderBy(u => u.FullName ?? u.Email ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<InviteUserResult> InviteUserAsync(
        string organizationId, InviteUserRequest request, string? baseUrl = null)
    {
        var organization = await _dbContext.Organizations.FindAsync(organizationId);
        if (organization is null || !organization.IsActive)
            throw new InvalidOperationException("Organization not found or inactive.");

        var roleName = request.Role;
        if (!_allowedRoles.Contains(roleName))
            throw new ArgumentException($"Role '{roleName}' is not a valid organization role.");

        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        var tempPassword = string.Empty;
        var invitationToken = string.Empty;
        var setupLink = string.Empty;
        var emailSent = false;
        ApplicationUser user;

        if (existingUser is not null)
        {
            if (existingUser.IsGlobalAdmin)
                throw new InvalidOperationException("A global admin account cannot be assigned as an organization member.");

            user = existingUser;
            user.FullName = request.Name.Trim();

            var membership = await _dbContext.UserOrganizationMemberships
                .FirstOrDefaultAsync(m => m.UserId == user.Id && m.OrganizationId == organizationId);

            if (membership is null)
            {
                _dbContext.UserOrganizationMemberships.Add(new UserOrganizationMembership
                {
                    UserId = user.Id,
                    OrganizationId = organizationId,
                    Role = roleName
                });
            }
            else
            {
                membership.Role = roleName;
            }

            if (string.IsNullOrWhiteSpace(user.OrganizationId))
                user.OrganizationId = organizationId;

            await _userManager.UpdateAsync(user);
            await _dbContext.SaveChangesAsync();
            await _userManager.UpdateSecurityStampAsync(user);
        }
        else
        {
            tempPassword = GenerateTemporaryPassword();
            user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FullName = request.Name.Trim(),
                EmailConfirmed = false,
                OrganizationId = organizationId,
                LockoutEnabled = true
            };

            var result = await _userManager.CreateAsync(user, tempPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Unable to create user: {string.Join("; ", result.Errors.Select(e => e.Description))}");

            _dbContext.UserOrganizationMemberships.Add(new UserOrganizationMembership
            {
                UserId = user.Id,
                OrganizationId = organizationId,
                Role = roleName
            });
            await _dbContext.SaveChangesAsync();

            invitationToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(invitationToken));
            setupLink = BuildSetupLink(user.Id, encodedToken, baseUrl);

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
        }

        _logger.LogInformation(
            "User created: UserId={UserId}, Email={Email}, FullName={FullName}, OrganizationId={OrgId}, Role={Role}.",
            user.Id, user.Email, user.FullName, organizationId, roleName);

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
        var membership = await _dbContext.UserOrganizationMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId)
            ?? throw new InvalidOperationException("User does not belong to the specified organization.");

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        await EnsureUserCanBeRemovedAsync(membership);

        _dbContext.UserOrganizationMemberships.Remove(membership);
        await _dbContext.SaveChangesAsync();

        if (string.Equals(user.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            user.OrganizationId = await _dbContext.UserOrganizationMemberships
                .Where(m => m.UserId == userId)
                .OrderBy(m => m.Organization.Name)
                .Select(m => m.OrganizationId)
                .FirstOrDefaultAsync();

            await _userManager.UpdateAsync(user);
            await _userManager.UpdateSecurityStampAsync(user);
        }

        var remainingMemberships = await _dbContext.UserOrganizationMemberships
            .AnyAsync(m => m.UserId == userId);

        if (!remainingMemberships && !user.IsGlobalAdmin)
        {
            var deleteResult = await _userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
                throw new InvalidOperationException(
                    $"Unable to delete user: {string.Join("; ", deleteResult.Errors.Select(e => e.Description))}");
        }

        _logger.LogInformation(
            "User deleted: UserId={UserId}, Email={Email}, FullName={FullName}, OrganizationId={OrgId}, Reason={Reason}.",
            user.Id, user.Email, user.FullName, organizationId, "organization admin removal");
    }

    public async Task ChangeUserRoleAsync(string userId, string organizationId, string newRole)
    {
        if (!_allowedRoles.Contains(newRole))
            throw new ArgumentException($"Role '{newRole}' is not a valid organization role.");

        var membership = await _dbContext.UserOrganizationMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId)
            ?? throw new InvalidOperationException("User does not belong to the specified organization.");

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        await EnsureRoleChangeIsAllowedAsync(membership, newRole);

        membership.Role = newRole;
        await _dbContext.SaveChangesAsync();
        await _userManager.UpdateSecurityStampAsync(user);
    }

    private async Task EnsureUserCanBeRemovedAsync(UserOrganizationMembership membership)
    {
        if (membership.Role != RoleNames.OrganizationAdmin)
            return;

        var adminCount = await _dbContext.UserOrganizationMemberships
            .CountAsync(m => m.OrganizationId == membership.OrganizationId && m.Role == RoleNames.OrganizationAdmin);
        if (adminCount <= 1)
            throw new InvalidOperationException("Cannot remove the last organization admin. Assign another admin first.");
    }

    private async Task EnsureRoleChangeIsAllowedAsync(UserOrganizationMembership membership, string newRole)
    {
        if (membership.Role != RoleNames.OrganizationAdmin || string.Equals(newRole, RoleNames.OrganizationAdmin, StringComparison.OrdinalIgnoreCase))
            return;

        var adminCount = await _dbContext.UserOrganizationMemberships
            .CountAsync(m => m.OrganizationId == membership.OrganizationId && m.Role == RoleNames.OrganizationAdmin);
        if (adminCount <= 1)
            throw new InvalidOperationException("Cannot change the last organization admin's role. Assign another admin first.");
    }

    private static string BuildSetupLink(string userId, string encodedToken, string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl)) return string.Empty;
        return $"{baseUrl}/auth/setpassword?userId={Uri.EscapeDataString(userId)}&token={encodedToken}";
    }

    private static string GenerateTemporaryPassword()
    {
        var segment = Guid.NewGuid().ToString("N")[..6];
        return $"Kocr!{segment}";
    }
}
