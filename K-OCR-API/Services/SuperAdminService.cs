using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using K_OCR_API.Data;
using K_OCR_API.Identity;
using K_OCR_API.Models.SuperAdmin.Requests;
using K_OCR_API.Models.SuperAdmin.Responses;
using K_OCR.Security;

namespace K_OCR_API.Services;

public class SuperAdminService : ISuperAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public SuperAdminService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
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
            IsActive = true
        };

        await _dbContext.Organizations.AddAsync(organization);
        await _dbContext.SaveChangesAsync();

        var tempPassword = GenerateTemporaryPassword();
        var user = new ApplicationUser
        {
            UserName = request.AdminEmail,
            Email = request.AdminEmail,
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
            throw new InvalidOperationException($"Unable to create admin user: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await _userManager.AddToRoleAsync(user, RoleNames.OrganizationAdmin);
        var invitationToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        return new CreateOrganizationResult
        {
            OrganizationId = organization.Id,
            AdminUserId = user.Id,
            TempPassword = tempPassword,
            InvitationToken = invitationToken
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

    private async Task EnsureRolesAsync()
    {
        if (!await _roleManager.RoleExistsAsync(RoleNames.SuperAdmin))
            await _roleManager.CreateAsync(new IdentityRole(RoleNames.SuperAdmin));

        if (!await _roleManager.RoleExistsAsync(RoleNames.OrganizationAdmin))
            await _roleManager.CreateAsync(new IdentityRole(RoleNames.OrganizationAdmin));

        if (!await _roleManager.RoleExistsAsync(RoleNames.OrganizationUser))
            await _roleManager.CreateAsync(new IdentityRole(RoleNames.OrganizationUser));
    }

    private static string GenerateTemporaryPassword()
    {
        var randomSegment = Guid.NewGuid().ToString("N").Substring(0, 6);
        return $"Kocr!{randomSegment}";
    }
}
