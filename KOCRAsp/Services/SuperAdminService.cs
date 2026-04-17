using K_OCR.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using K_OCR.Data;
using K_OCR.Services;
using KOCRAsp.Data;
using KOCRAsp.Identity;
using KOCRAsp.Models.Api.SuperAdmin;
using KOCRAsp.Security;

namespace KOCRAsp.Services;

public class SuperAdminService : ISuperAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IEmailService _emailService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly IPathService _pathService;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly ILogger<SuperAdminService> _logger;

    public SuperAdminService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IEmailService emailService,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        IPathService pathService,
        IOrgConfigService orgConfigSvc,
        ILogger<SuperAdminService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _pathService = pathService;
        _orgConfigSvc = orgConfigSvc;
        _logger = logger;
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
            setupLink = $"{baseUrl}/auth/setpassword?userId={Uri.EscapeDataString(user.Id)}&token={encodedToken}";

            try
            {
                await _emailService.SendOrgAdminInviteAsync(
                    user.Email!, request.AdminName.Trim(), organization.Name, setupLink);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send invite email to {Email}; setup link will be provided manually.", user.Email);
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
            _logger.LogError(ex, "Failed to create org folder {OrgPath} for organization {OrgId}.", orgPath, organization.Id);
            // Roll back: delete user and org
            if (!reusingExistingUser)
                await _userManager.DeleteAsync(user);
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException(
                $"Organization was created but the folder could not be created at \"{orgPath}\": {ex.Message}");
        }

        // Write default OrgConfig.json
        try
        {
            await _orgConfigSvc.SaveAsync(organization.Name, new K_OCR.Configuration.OrgConfig());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write OrgConfig.json for organization {OrgId}; defaults will be used.", organization.Id);
            // Non-fatal: the service falls back to defaults when the file is absent.
        }

        // Create and migrate the per-org SQLite database
        var orgDbPath = _pathService.GetOrgDbPath(organization.Name);
        try
        {
            var dbOptions = new DbContextOptionsBuilder<KOCRDbContext>()
                .UseSqlite($"Data Source={orgDbPath}")
                .Options;
            await using var orgDb = new KOCRDbContext(dbOptions);
            await orgDb.Database.MigrateAsync();
            _logger.LogInformation("Per-org database created at {DbPath} for organization {OrgId}.", orgDbPath, organization.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate per-org database at {DbPath} for organization {OrgId}.", orgDbPath, organization.Id);
            // Roll back: remove folder, user, and org record
            if (Directory.Exists(orgPath))
                Directory.Delete(orgPath, recursive: true);
            if (!reusingExistingUser)
                await _userManager.DeleteAsync(user);
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException(
                $"Organization folder was created but the database could not be migrated at \"{orgDbPath}\": {ex.Message}");
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

        foreach (var user in organization.Users.ToList())
            await _userManager.DeleteAsync(user);

        _dbContext.Organizations.Remove(organization);
        await _dbContext.SaveChangesAsync();

        // Release any pooled SQLite connections to the org's database before
        // deleting the folder. On Windows, open file handles prevent directory
        // deletion; EF Core returns connections to the pool rather than closing
        // them, so the handles stay alive until the pool is cleared.
        SqliteConnection.ClearAllPools();

        var orgPath = _pathService.GetOrgFolderPath(organization.Name);
        if (Directory.Exists(orgPath))
            Directory.Delete(orgPath, recursive: true);
    }

    public async Task<int> CleanupExpiredGuestAccountsAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        if (retention <= TimeSpan.Zero)
            return 0;

        // Phase 2: Delete orgs already marked as pending from a previous cycle.
        // This runs first so we don't immediately delete what we just marked.
        // Orgs are already revoked from Phase 1, but guard with IsActive in case of
        // an unexpected state (e.g. app crashed between mark and revoke).
        var pendingOrgs = await _dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.IsGuestOrganization && o.IsPendingDeletion)
            .Select(o => new { o.Id, o.IsActive })
            .ToArrayAsync(cancellationToken);

        var deletedCount = 0;
        foreach (var org in pendingOrgs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (org.IsActive)
                await RevokeOrganizationAsync(org.Id);
            await DeleteOrganizationAsync(org.Id);
            deletedCount++;
        }

        // Phase 1: Mark newly-expired orgs as pending and immediately revoke them
        // (locks user accounts) so users cannot log back in after dismissing the dialog.
        var cutoff = DateTime.UtcNow - retention;
        var newlyExpired = await _dbContext.Organizations
            .Where(o => o.IsGuestOrganization && !o.IsPendingDeletion && o.CreatedAtUtc <= cutoff)
            .ToListAsync(cancellationToken);

        foreach (var org in newlyExpired)
        {
            org.IsPendingDeletion = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await RevokeOrganizationAsync(org.Id);
        }

        return deletedCount;
    }

    public async Task<bool> IsGuestOrgPendingDeletionAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Id == organizationId && o.IsGuestOrganization)
            .Select(o => o.IsPendingDeletion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static readonly SemaphoreSlim _guestLock = new(1, 1);

    private string GuestNoFilePath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CurrentGuestNo.txt");

    public async Task<int> PeekNextGuestNumberAsync()
    {
        await _guestLock.WaitAsync();
        try
        {
            return ReadGuestNo() + 1;
        }
        finally
        {
            _guestLock.Release();
        }
    }

    public async Task<GuestLoginResult> CreateGuestAsync(string email)
    {
        await EnsureRolesAsync();

        await _guestLock.WaitAsync();
        int guestNo;
        try
        {
            guestNo = ReadGuestNo() + 1;
            await File.WriteAllTextAsync(GuestNoFilePath, guestNo.ToString());
        }
        finally
        {
            _guestLock.Release();
        }

        var userName = $"Guest{guestNo}";
        var orgName  = $"Guest{guestNo}Organization";

        var organization = new Organization
        {
            Name               = orgName,
            Description        = $"Guest organization for {userName}",
            IsActive           = true,
            IsGuestOrganization = true
        };

        await _dbContext.Organizations.AddAsync(organization);
        await _dbContext.SaveChangesAsync();

        var password = GenerateTemporaryPassword();
        var user = new ApplicationUser
        {
            UserName             = userName,
            Email                = email,
            FullName             = userName,
            EmailConfirmed       = true,
            OrganizationId       = organization.Id,
            IsOrganizationAdmin  = true,
            LockoutEnabled       = false
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException(
                $"Unable to create guest user: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await _userManager.AddToRoleAsync(user, RoleNames.OrganizationAdmin);

        var orgPath = _pathService.GetOrgFolderPath(organization.Name);
        try
        {
            Directory.CreateDirectory(orgPath);
        }
        catch (Exception ex)
        {
            await _userManager.DeleteAsync(user);
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException($"Could not create org folder: {ex.Message}");
        }

        try
        {
            await _orgConfigSvc.SaveAsync(organization.Name, new K_OCR.Configuration.OrgConfig());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write OrgConfig.json for guest org {OrgName}", orgName);
        }

        var orgDbPath = _pathService.GetOrgDbPath(organization.Name);
        try
        {
            var dbOptions = new DbContextOptionsBuilder<KOCRDbContext>()
                .UseSqlite($"Data Source={orgDbPath}")
                .Options;
            await using var orgDb = new KOCRDbContext(dbOptions);
            await orgDb.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            if (Directory.Exists(orgPath)) Directory.Delete(orgPath, recursive: true);
            await _userManager.DeleteAsync(user);
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException($"Could not migrate guest org database: {ex.Message}");
        }

        return new GuestLoginResult
        {
            UserName = userName,
            OrgName  = orgName,
            UserId   = user.Id,
            Password = password
        };
    }

    private int ReadGuestNo()
    {
        if (!File.Exists(GuestNoFilePath)) return 0;
        var text = File.ReadAllText(GuestNoFilePath).Trim();
        return int.TryParse(text, out var n) ? n : 0;
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
