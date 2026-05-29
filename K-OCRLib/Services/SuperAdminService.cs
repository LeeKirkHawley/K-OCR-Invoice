using System.Text;
using K_OCR.Configuration;
using K_OCR.Data;
using K_OCR.Identity;
using K_OCR.Models.Api.SuperAdmin;
using K_OCR.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace K_OCR.Services;

public class SuperAdminService : ISuperAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly IPathService _pathService;
    private readonly IOrgConfigService _orgConfigSvc;
    private readonly IStripeProvisioningService? _stripeProvisioning;
    private readonly ILogger<SuperAdminService> _logger;

    public SuperAdminService(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IEmailService emailService,
        IConfiguration configuration,
        IPathService pathService,
        IOrgConfigService orgConfigSvc,
        IStripeProvisioningService? stripeProvisioning,
        ILogger<SuperAdminService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _emailService = emailService;
        _configuration = configuration;
        _pathService = pathService;
        _orgConfigSvc = orgConfigSvc;
        _stripeProvisioning = stripeProvisioning;
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
                IsGuestOrganization = org.IsGuestOrganization,
                IsBetaTestOrganization = org.IsBetaTestOrganization,
                BetaMaxOcrPages = org.BetaMaxOcrPages,
                MarkedForDeletionAtUtc = org.MarkedForDeletionAtUtc,
                CreatedAtUtc = org.CreatedAtUtc,
                UserCount = org.UserMemberships.Count,
                StripeCustomerId = org.StripeCustomerId,
                StripeSubscriptionStatus = org.StripeSubscriptionStatus,
            })
            .ToArrayAsync();
    }

    public async Task<CreateOrganizationResult> CreateOrganizationAsync(
        CreateOrganizationRequest request, string? baseUrl = null)
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
            IsActive = true,
            IsBetaTestOrganization = request.IsBetaTestOrganization,
            BetaMaxOcrPages = ResolveBetaMaxOcrPages(request.BetaMaxOcrPages)
        };

        await _dbContext.Organizations.AddAsync(organization);
        await _dbContext.SaveChangesAsync();

        var existingUser = await _userManager.FindByEmailAsync(request.AdminEmail);

        ApplicationUser user;
        string invitationToken;
        string tempPassword;
        if (existingUser is { IsGlobalAdmin: true })
            throw new InvalidOperationException(
                "A global admin account cannot be reassigned to a new organization.");

        bool reusingExistingUser = existingUser is not null;

        if (reusingExistingUser)
        {
            // Re-use the existing account — add membership to the new organisation.
            user = existingUser!;
            user.FullName = request.AdminName.Trim();
            if (string.IsNullOrWhiteSpace(user.OrganizationId))
                user.OrganizationId = organization.Id;
            await _userManager.UpdateAsync(user);
            await EnsureMembershipAsync(user, organization.Id, RoleNames.OrganizationAdmin);
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

            await EnsureMembershipAsync(user, organization.Id, RoleNames.OrganizationAdmin);
            invitationToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        }

        string setupLink = string.Empty;
        bool emailSent = false;

        if (!reusingExistingUser)
        {
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(invitationToken));
            setupLink = string.IsNullOrEmpty(baseUrl)
                ? string.Empty
                : $"{baseUrl}/auth/setpassword?userId={Uri.EscapeDataString(user.Id)}&token={encodedToken}";

            try
            {
                await _emailService.SendOrgAdminInviteAsync(
                    user.Email!, request.AdminName.Trim(), organization.Name, setupLink);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send invite email to {Email}; setup link will be provided manually.", user.Email);
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
            if (!reusingExistingUser)
                await DeleteUserAndLogAsync(user, $"rollback after failed org folder creation for organization {organization.Id}");
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException(
                $"Organization was created but the folder could not be created at \"{orgPath}\": {ex.Message}");
        }

        // Write default OrgConfig.json
        try
        {
            await _orgConfigSvc.SaveAsync(organization.Name, new OrgConfig());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write OrgConfig.json for organization {OrgId}; defaults will be used.", organization.Id);
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
            if (Directory.Exists(orgPath))
                Directory.Delete(orgPath, recursive: true);
            if (!reusingExistingUser)
                await DeleteUserAndLogAsync(user, $"rollback after failed organization database migration for organization {organization.Id}");
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException(
                $"Organization folder was created but the database could not be migrated at \"{orgDbPath}\": {ex.Message}");
        }

        // Auto-provision Stripe Customer + Subscription (non-fatal — admin can retry manually)
        bool stripeProvisioned = organization.IsTrialOrganization();
        string? stripeProvisioningError = null;
        if (!organization.IsTrialOrganization())
        {
            var stripeResult = await TryProvisionStripeAsync(organization, priceId: null);
            stripeProvisioned = stripeResult.Success;
            stripeProvisioningError = stripeResult.Error;
        }

        _logger.LogInformation(
            "Organization created: OrgId={OrgId}, OrgName={OrgName}, IsGuestOrganization={IsGuestOrganization}, IsBetaTestOrganization={IsBetaTestOrganization}, AdminUserId={AdminUserId}, AdminEmail={AdminEmail}.",
            organization.Id, organization.Name, organization.IsGuestOrganization, organization.IsBetaTestOrganization, user.Id, user.Email);

        if (!reusingExistingUser)
        {
            _logger.LogInformation(
                "User created: UserId={UserId}, Email={Email}, FullName={FullName}, OrganizationId={OrgId}, Role={Role}.",
                user.Id, user.Email, user.FullName, organization.Id, RoleNames.OrganizationAdmin);
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
            EmailSent = emailSent,
            StripeProvisioned = stripeProvisioned,
            StripeProvisioningError = stripeProvisioningError
        };
    }

    public async Task RevokeOrganizationAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        organization.IsActive = false;

        await _dbContext.SaveChangesAsync();
    }

    public async Task ReEnableOrganizationAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        organization.IsActive = true;

        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteOrganizationAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        if (organization.IsActive)
            throw new InvalidOperationException("Organization must be revoked before it can be deleted.");

        var userIds = await _dbContext.UserOrganizationMemberships
            .Where(m => m.OrganizationId == organizationId)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync();

        var memberships = await _dbContext.UserOrganizationMemberships
            .Where(m => m.OrganizationId == organizationId)
            .ToListAsync();
        _dbContext.UserOrganizationMemberships.RemoveRange(memberships);

        _dbContext.Organizations.Remove(organization);
        await _dbContext.SaveChangesAsync();

        foreach (var userId in userIds)
        {
            var remainingMemberships = await _dbContext.UserOrganizationMemberships
                .AnyAsync(m => m.UserId == userId);
            if (remainingMemberships)
                continue;

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || user.IsGlobalAdmin)
                continue;

            await DeleteUserAndLogAsync(user, $"organization deletion ({organization.Id})");
        }

        // Release any pooled SQLite connections to the org's database before
        // deleting the folder. On Windows, open file handles prevent directory
        // deletion; EF Core returns connections to the pool rather than closing
        // them, so the handles stay alive until the pool is cleared.
        SqliteConnection.ClearAllPools();

        var orgPath = _pathService.GetOrgFolderPath(organization.Name);
        if (Directory.Exists(orgPath))
            Directory.Delete(orgPath, recursive: true);

        _logger.LogInformation(
            "Organization deleted: OrgId={OrgId}, OrgName={OrgName}, IsGuestOrganization={IsGuestOrganization}.",
            organization.Id, organization.Name, organization.IsGuestOrganization);
    }

    public async Task MarkOrganizationForDeletionAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        if (organization.IsActive)
            throw new InvalidOperationException("Organization must be revoked before it can be marked for deletion.");

        organization.MarkedForDeletionAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Organization marked for deletion: OrgId={OrgId}, OrgName={OrgName}, MarkedForDeletionAtUtc={MarkedForDeletionAtUtc}, IsGuestOrganization={IsGuestOrganization}.",
            organization.Id, organization.Name, organization.MarkedForDeletionAtUtc, organization.IsGuestOrganization);

        // Send deletion notification emails to organization admins for this org.
        var orgAdmins = await _dbContext.UserOrganizationMemberships
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.OrganizationId == organizationId && m.Role == RoleNames.OrganizationAdmin)
            .Select(m => m.User)
            .ToListAsync();
        foreach (var admin in orgAdmins)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(admin.Email))
                {
                    await _emailService.SendOrgDeletionNotificationAsync(
                        admin.Email,
                        admin.FullName ?? admin.UserName ?? "User",
                        organization.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Failed to send deletion notification email to admin {AdminEmail} for org {OrgId}.",
                    admin.Email, organizationId);
            }
        }
    }

    public async Task ReinstateMarkedOrganizationAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId);

        if (organization is null)
            throw new KeyNotFoundException("Organization not found.");

        if (organization.MarkedForDeletionAtUtc is null)
            throw new InvalidOperationException("Organization is not marked for deletion.");

        organization.MarkedForDeletionAtUtc = null;
        await _dbContext.SaveChangesAsync();

        await ReEnableOrganizationAsync(organizationId);
    }

    public async Task<int> CleanupExpiredSoftDeletesAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        if (retention <= TimeSpan.Zero)
            return 0;

        var cutoff = DateTime.UtcNow - retention;
        var expired = await _dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.MarkedForDeletionAtUtc != null && o.MarkedForDeletionAtUtc <= cutoff)
            .Select(o => o.Id)
            .ToArrayAsync(cancellationToken);

        var deletedCount = 0;
        foreach (var orgId in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteOrganizationAsync(orgId);
            deletedCount++;
        }

        return deletedCount;
    }

    public async Task<IReadOnlyList<string>> CleanupExpiredGuestAccountsAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        if (retention <= TimeSpan.Zero)
            return [];

        var cutoff = DateTime.UtcNow - retention;
        var newlyExpired = await _dbContext.Organizations
            .Where(o => o.IsGuestOrganization
                     && o.MarkedForDeletionAtUtc == null
                     && o.CreatedAtUtc <= cutoff)
            .Select(o => new { o.Id, o.Name, o.IsActive })
            .ToListAsync(cancellationToken);

        foreach (var org in newlyExpired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (org.IsActive)
                await RevokeOrganizationAsync(org.Id);
            await MarkOrganizationForDeletionAsync(org.Id);
        }

        return newlyExpired.Select(o => o.Name).ToList();
    }

    public async Task<bool> IsOrgMarkedForDeletionAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => o.MarkedForDeletionAtUtc != null)
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

        await EnsureMembershipAsync(user, organization.Id, RoleNames.OrganizationAdmin);

        var orgPath = _pathService.GetOrgFolderPath(organization.Name);
        try
        {
            Directory.CreateDirectory(orgPath);
        }
        catch (Exception ex)
        {
            await DeleteUserAndLogAsync(user, $"rollback after failed guest org folder creation for organization {organization.Id}");
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException($"Could not create org folder: {ex.Message}");
        }

        try
        {
            await _orgConfigSvc.SaveAsync(organization.Name, new OrgConfig());
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
            await DeleteUserAndLogAsync(user, $"rollback after failed guest organization database migration for organization {organization.Id}");
            _dbContext.Organizations.Remove(organization);
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException($"Could not migrate guest org database: {ex.Message}");
        }

        _logger.LogInformation(
            "Organization created: OrgId={OrgId}, OrgName={OrgName}, IsGuestOrganization={IsGuestOrganization}, AdminUserId={AdminUserId}, AdminEmail={AdminEmail}.",
            organization.Id, organization.Name, organization.IsGuestOrganization, user.Id, user.Email);
        _logger.LogInformation(
            "User created: UserId={UserId}, Email={Email}, FullName={FullName}, OrganizationId={OrgId}, Role={Role}.",
            user.Id, user.Email, user.FullName, organization.Id, RoleNames.OrganizationAdmin);

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

    private async Task EnsureRolesAsync()
    {
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.OrganizationUser })
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));
    }

    private async Task EnsureMembershipAsync(ApplicationUser user, string organizationId, string role)
    {
        var existingMembership = await _dbContext.UserOrganizationMemberships
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.OrganizationId == organizationId);

        if (existingMembership is null)
        {
            _dbContext.UserOrganizationMemberships.Add(new UserOrganizationMembership
            {
                UserId = user.Id,
                OrganizationId = organizationId,
                Role = role
            });
        }
        else
        {
            existingMembership.Role = role;
        }

        await _dbContext.SaveChangesAsync();
        await _userManager.UpdateSecurityStampAsync(user);
    }

    private static string GenerateTemporaryPassword()
    {
        var segment = Guid.NewGuid().ToString("N")[..6];
        return $"Kocr!{segment}";
    }

    private async Task DeleteUserAndLogAsync(ApplicationUser user, string reason)
    {
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Unable to delete user: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        _logger.LogInformation(
            "User deleted: UserId={UserId}, Email={Email}, FullName={FullName}, OrganizationId={OrgId}, Reason={Reason}.",
            user.Id, user.Email, user.FullName, user.OrganizationId, reason);
    }

    public async Task<StripeStatusResult> SyncStripeStatusAsync(string organizationId)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId)
            ?? throw new KeyNotFoundException("Organization not found.");

        // Trial orgs (guest/beta) are never Stripe-billed — no status check needed.
        if (organization.IsTrialOrganization()) return StripeStatusResult.NotApplicable;

        // Stripe not configured or no subscription provisioned → treat as inactive.
        if (_stripeProvisioning is null || string.IsNullOrEmpty(organization.StripeSubscriptionId))
        {
            _logger.LogWarning(
                "Cannot sync Stripe status for org {OrgId}: {Reason}.",
                organizationId,
                _stripeProvisioning is null ? "Stripe not configured" : "No subscription ID");
            return new StripeStatusResult(true, organization.StripeSubscriptionStatus); // cached value (default "none")
        }

        try
        {
            var status = await _stripeProvisioning.GetSubscriptionStatusAsync(organization.StripeSubscriptionId);
            organization.StripeSubscriptionStatus = status;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Synced Stripe subscription status for org {OrgId}: status={Status}.",
                organizationId, status);

            return new StripeStatusResult(true, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch Stripe subscription status for org {OrgId}. OCR will be blocked.",
                organizationId);
            // Stripe is the authority — if we can't reach it, surface it as inactive.
            return new StripeStatusResult(true, "error");
        }
    }

    public async Task ProvisionStripeAsync(string organizationId, string? priceId = null)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId)
            ?? throw new KeyNotFoundException("Organization not found.");

        if (organization.IsTrialOrganization())
            throw new InvalidOperationException("Trial organizations are not billed via Stripe.");

        if (_stripeProvisioning is null)
            throw new InvalidOperationException("Stripe provisioning service is not configured.");

        var configuredPriceId = _configuration["Stripe:DefaultPriceId"];
        var resolvedPriceId = !string.IsNullOrWhiteSpace(priceId) ? priceId
            : !string.IsNullOrWhiteSpace(configuredPriceId) ? configuredPriceId
            : throw new InvalidOperationException(
                $"No Stripe price ID provided and Stripe:DefaultPriceId is not configured (raw config value: \"{configuredPriceId ?? "null"}\").");

        _logger.LogInformation(
            "Provisioning Stripe for org {OrgId} with priceId={PriceId} (from {Source}).",
            organizationId, resolvedPriceId,
            !string.IsNullOrWhiteSpace(priceId) ? "caller" : "Stripe:DefaultPriceId config");

        var customerId = organization.StripeCustomerId;
        if (customerId is null)
        {
            customerId = await _stripeProvisioning.CreateCustomerAsync(organization.Id, organization.Name);
            organization.StripeCustomerId = customerId;
            await _dbContext.SaveChangesAsync();
        }

        var sub = await _stripeProvisioning.CreateSubscriptionAsync(customerId, resolvedPriceId);
        organization.StripeSubscriptionId = sub.SubscriptionId;
        organization.StripeSubscriptionItemId = sub.SubscriptionItemId;
        organization.StripePriceId = resolvedPriceId;
        organization.StripeSubscriptionStatus = sub.Status;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Stripe subscription {SubId} provisioned for org {OrgId} (status={Status}).",
            sub.SubscriptionId, organizationId, sub.Status);
    }

    private async Task<(bool Success, string? Error)> TryProvisionStripeAsync(Organization organization, string? priceId)
    {
        try
        {
            await ProvisionStripeAsync(organization.Id, priceId);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Auto-provisioning Stripe failed for org {OrgId}. Admin can retry manually from the Admin console.",
                organization.Id);
            return (false, ex.Message);
        }
    }

    public async Task UpdateBetaMaxOcrPagesAsync(string organizationId, int maxOcrPages)
    {
        if (maxOcrPages <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxOcrPages), "Beta max OCR pages must be greater than 0.");

        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId)
            ?? throw new KeyNotFoundException("Organization not found.");

        if (!organization.IsBetaTestOrganization)
            throw new InvalidOperationException("Only beta-test organizations support a beta OCR page limit.");

        organization.BetaMaxOcrPages = maxOcrPages;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<PromoteOrganizationResult> PromoteOrganizationAsync(string organizationId, string? newOrganizationName = null)
    {
        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId)
            ?? throw new KeyNotFoundException("Organization not found.");

        if (!organization.IsTrialOrganization())
            throw new InvalidOperationException("Only guest or beta-test organizations can be promoted.");

        var oldName = organization.Name;
        var targetName = string.IsNullOrWhiteSpace(newOrganizationName)
            ? oldName
            : newOrganizationName.Trim();

        if (!string.Equals(oldName, targetName, StringComparison.OrdinalIgnoreCase))
            await EnsureOrganizationNameAvailableAsync(organizationId, targetName);

        var oldOrgPath = _pathService.GetOrgFolderPath(oldName);
        var newOrgPath = _pathService.GetOrgFolderPath(targetName);

        var folderMoved = false;
        if (!string.Equals(oldOrgPath, newOrgPath, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(newOrgPath))
                throw new InvalidOperationException($"Cannot promote organization: target folder already exists at \"{newOrgPath}\".");

            if (Directory.Exists(oldOrgPath))
            {
                Directory.Move(oldOrgPath, newOrgPath);
                folderMoved = true;
            }
            else
            {
                Directory.CreateDirectory(newOrgPath);
            }
        }

        try
        {
            if (!string.Equals(oldOrgPath, newOrgPath, StringComparison.OrdinalIgnoreCase))
                await RewriteOrgStoragePathsAsync(newOrgPath, oldOrgPath, targetName);

            organization.Name = targetName;
            organization.IsGuestOrganization = false;
            organization.IsBetaTestOrganization = false;
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            if (folderMoved && Directory.Exists(newOrgPath) && !Directory.Exists(oldOrgPath))
                Directory.Move(newOrgPath, oldOrgPath);
            throw;
        }

        var stripeResult = await TryProvisionStripeAsync(organization, priceId: null);
        return new PromoteOrganizationResult
        {
            OrganizationName = organization.Name,
            StripeProvisioned = stripeResult.Success,
            StripeProvisioningError = stripeResult.Error
        };
    }

    private int ResolveBetaMaxOcrPages(int? requestedMaxOcrPages)
    {
        var configuredDefault = _configuration.GetValue<int?>("Limits:Beta:MaxOcrPages") ?? 500;
        if (configuredDefault <= 0)
            configuredDefault = 500;

        if (!requestedMaxOcrPages.HasValue)
            return configuredDefault;

        if (requestedMaxOcrPages.Value <= 0)
            throw new InvalidOperationException("Beta max OCR pages must be greater than 0.");

        return requestedMaxOcrPages.Value;
    }

    private async Task EnsureOrganizationNameAvailableAsync(string currentOrganizationId, string targetName)
    {
        var existingNames = await _dbContext.Organizations
            .Where(o => o.Id != currentOrganizationId)
            .Select(o => o.Name)
            .ToArrayAsync();

        if (existingNames.Any(n => string.Equals(n, targetName, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateOrganizationNameException(
                $"An organization named \"{targetName}\" already exists.");

        var sanitizedNew = _pathService.SanitizeName(targetName);
        if (existingNames.Any(n => _pathService.SanitizeName(n) == sanitizedNew))
            throw new DuplicateOrganizationNameException(
                $"The organization name \"{targetName}\" would produce a folder name that conflicts with an existing organization. Choose a different name.");
    }

    private async Task RewriteOrgStoragePathsAsync(string newOrgPath, string oldOrgPath, string targetName)
    {
        var orgDbPath = Path.Combine(newOrgPath, "kocr.db");
        if (!File.Exists(orgDbPath))
            return;

        var dbOptions = new DbContextOptionsBuilder<KOCRDbContext>()
            .UseSqlite($"Data Source={orgDbPath}")
            .Options;

        await using var orgDb = new KOCRDbContext(dbOptions);
        await orgDb.Database.MigrateAsync();

        static string ReplacePrefix(string value, string oldPrefix, string newPrefix)
        {
            if (!value.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                return value;
            return newPrefix + value[oldPrefix.Length..];
        }

        var batches = await orgDb.Batches.ToListAsync();
        foreach (var batch in batches)
            batch.FolderPath = ReplacePrefix(batch.FolderPath, oldOrgPath, newOrgPath);

        var invoices = await orgDb.Invoices
            .Where(i => i.FilePath != null)
            .ToListAsync();
        foreach (var invoice in invoices)
            invoice.FilePath = ReplacePrefix(invoice.FilePath!, oldOrgPath, newOrgPath);

        var jobs = await orgDb.OcrJobs.ToListAsync();
        foreach (var job in jobs)
        {
            job.FilePath = ReplacePrefix(job.FilePath, oldOrgPath, newOrgPath);
            job.OrgName = targetName;
        }

        await orgDb.SaveChangesAsync();
    }
}
