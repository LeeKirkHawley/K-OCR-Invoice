using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

    public OrganizationAdminService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<OrganizationUserOverview[]> ListUsersAsync(string organizationId)
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
                UserId = user.Id,
                Email = user.Email,
                IsActive = user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow,
                Roles = roles.ToArray()
            });
        }

        return overviews.ToArray();
    }

    public async Task<InviteUserResult> InviteUserAsync(string organizationId, InviteUserRequest request)
    {
        var organization = await _dbContext.Organizations.FindAsync(organizationId);
        if (organization is null || !organization.IsActive)
            throw new InvalidOperationException("Organization not found or inactive.");

        if (!await _roleManager.RoleExistsAsync(RoleNames.OrganizationUser))
            await _roleManager.CreateAsync(new IdentityRole(RoleNames.OrganizationUser));

        var tempPassword = GenerateTemporaryPassword();
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = false,
            OrganizationId = organizationId,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Unable to create user: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await _userManager.AddToRoleAsync(user, RoleNames.OrganizationUser);
        var invitationToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        return new InviteUserResult
        {
            UserId = user.Id,
            TempPassword = tempPassword,
            InvitationToken = invitationToken
        };
    }

    private static string GenerateTemporaryPassword()
    {
        var segment = Guid.NewGuid().ToString("N")[..6];
        return $"Kocr!{segment}";
    }
}
