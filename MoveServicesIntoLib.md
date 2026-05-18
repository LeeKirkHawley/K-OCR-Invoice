# Move Services into K-OCRLib

**Goal:** KOCRAsp should contain only UI and ASP.NET host concerns. All business logic, data access, and infrastructure services belong in K-OCRLib.

---

## Motivation

K-OCRLib must support **multiple front-ends** (Blazor Server, REST API, WPF, CLI, etc.) that all share the same database schema and user base. This has two concrete consequences:

1. **All identity and user/org models must live in K-OCRLib** — `ApplicationUser`, `Organization`, `ApplicationDbContext`. A second front-end must be able to register `AddIdentity<ApplicationUser, IdentityRole>()` and point at the same SQLite schema without any dependency on KOCRAsp.
2. **Claims logic must be extractable** — different front-ends build claims in different ways (cookie auth, JWT, etc.), but the *set of custom claims* (OrganizationId, TenantName) is shared. This logic belongs in a K-OCRLib helper, not in an ASP.NET-specific factory.

K-OCRLib will expose a `AddKOCRIdentity()` DI extension so any front-end can onboard in one call.

---

## Guiding Principles

- Each phase must leave the solution building and all tests passing before the next phase starts.
- K-OCRLib must not reference KOCRAsp. KOCRAsp references K-OCRLib.
- Interface + implementation move together. Consumers in KOCRAsp update their `using` statements.
- If a service's interface is already in K-OCRLib, the implementation follows in the same phase.

---

## Phase 1 — Zero-Dependency Moves (highest ROI, no new packages required)

All dependencies for these services already exist in K-OCRLib. Simply move the files and update namespaces.

| Files to Move | From | Dependency Notes |
|---|---|---|
| `DuplicateOrganizationNameException.cs` | `KOCRAsp/Services/` | No dependencies |
| `IBatchActionService` + `BatchActionService` | `KOCRAsp/Services/BatchActionService.cs` | `KOCRDbContext`, `BatchAction` — already in K-OCRLib |
| `IInvoiceActionService` + `InvoiceActionService` | `KOCRAsp/Services/InvoiceActionService.cs` | `KOCRDbContext`, `InvoiceAction` — already in K-OCRLib |
| `OrgDbContextFactory` | `KOCRAsp/Services/OrgDbContextFactory.cs` | `ITenantContext`, `IPathService`, `DatabaseSettings`, `KOCRDbContext` — all in K-OCRLib |
| `IBatchChangeNotifier` + `BatchChangeNotifier` | `KOCRAsp/Services/BatchChangeNotifier.cs` | `System.Reactive` — add package to K-OCRLib.csproj |

**K-OCRLib.csproj change needed:**
```xml
<PackageReference Include="System.Reactive" Version="6.*" />
```

---

## Phase 2 — Email Infrastructure

`EmailService` depends only on `IConfigurationService` (K-OCRLib) and `ILogger`. Move it and add `MailKit`/`MimeKit` to K-OCRLib.

| Files to Move | From |
|---|---|
| `IEmailService.cs` | `KOCRAsp/Services/` |
| `EmailService.cs` | `KOCRAsp/Services/` |

**K-OCRLib.csproj changes needed:**
```xml
<PackageReference Include="MailKit" Version="4.*" />
<PackageReference Include="MimeKit" Version="4.*" />
```

---

## Phase 3 — Move ASP.NET Identity Models, ApplicationDbContext, and Claims Infrastructure

This is the **critical phase for multi-frontend support**. Until `ApplicationUser`, `Organization`, and `ApplicationDbContext` are in K-OCRLib, no second front-end can share the user/org identity stack.

### 3a — Move domain models

| Files to Move | From | Notes |
|---|---|---|
| `Organization.cs` | `KOCRAsp/Identity/` | Aggregate root entity — no ASP.NET deps |
| `ApplicationUser.cs` | `KOCRAsp/Identity/` | Extends `IdentityUser` — add `Microsoft.AspNetCore.Identity` to K-OCRLib |
| `AppClaimTypes.cs` | `KOCRAsp/Security/` | Claim URI constants — used by all front-ends |

**K-OCRLib.csproj change needed:**
```xml
<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="8.*" />
```

### 3b — Move ApplicationDbContext

| Files to Move | From | Notes |
|---|---|---|
| `ApplicationDbContext.cs` | `KOCRAsp/Data/` | `IdentityDbContext<ApplicationUser>` + `Organizations` DbSet |

After this step, KOCRAsp `Data/` folder should be empty. KOCRAsp references `ApplicationDbContext` from K-OCRLib.

### 3c — Extract claims logic into a K-OCRLib helper

`ApplicationUserClaimsPrincipalFactory` (KOCRAsp) bakes `OrganizationId` and `TenantName` into the auth cookie. A REST or JWT front-end would generate claims differently but needs the same custom claim set.

**Add to K-OCRLib:** `Identity/ApplicationUserClaimsExtensions.cs`
```csharp
public static class ApplicationUserClaimsExtensions
{
    // Adds k-ocr:organization-id and k-ocr:tenant-name to any ClaimsIdentity.
    public static async Task AddKOCRClaimsAsync(
        this ClaimsIdentity identity,
        ApplicationUser user,
        UserManager<ApplicationUser> userManager) { ... }
}
```

`ApplicationUserClaimsPrincipalFactory` in KOCRAsp becomes a thin adapter that calls `AddKOCRClaimsAsync`.

### 3d — Add `AddKOCRIdentity()` DI registration helper

Add to K-OCRLib a `KOCRIdentityExtensions.cs` extension so any front-end can register the full Identity stack in one call:

```csharp
// K-OCRLib/Extensions/KOCRIdentityExtensions.cs
public static IServiceCollection AddKOCRIdentity(
    this IServiceCollection services,
    string connectionString) { ... }
```

This registers `ApplicationDbContext`, `AddIdentity<ApplicationUser, IdentityRole>()`, and any shared Identity options — eliminating copy-paste across front-ends.

### 3e — Move TenantContext

`TenantContext` implements `ITenantContext` which is **already in K-OCRLib**. The concrete implementation follows now that `ApplicationDbContext` has moved.

| Files to Move | From |
|---|---|
| `TenantContext.cs` | `KOCRAsp/Services/` |

---

## Phase 4 — Identity-Dependent Services

All dependencies are now in K-OCRLib (Phase 3). Move these services and their interfaces.

| Files to Move | From | Key Dependencies |
|---|---|---|
| `IAuthService` + `AuthService` | `KOCRAsp/Services/` | `UserManager<ApplicationUser>` |
| `IOrganizationAdminService` + `OrganizationAdminService` | `KOCRAsp/Services/` | `ApplicationDbContext`, `UserManager`, `RoleManager`, `IEmailService` |
| `ISuperAdminService` + `SuperAdminService` | `KOCRAsp/Services/` | All of the above + `IPathService`, `IOrgConfigService`, `IStripeProvisioningService` |
| `ISuperAdminDataService` + `SuperAdminDataService` | `KOCRAsp/Services/` | `IPathService`, `DatabaseSettings`, `KOCRDbContext` |

> `SuperAdminService` is the single largest class (~700 lines). Budget extra time for namespace cleanup and verifying all helper methods compile.

**Also move supporting DTO models** (currently in `KOCRAsp/Models/Api/`):
- `KOCRAsp/Models/Api/Auth/*` → K-OCRLib
- `KOCRAsp/Models/Api/OrganizationAdmin/*` → K-OCRLib
- `KOCRAsp/Models/Api/SuperAdmin/*` → K-OCRLib

---

## Phase 5 — Cleanup Services

`BatchNotificationService` needs `ApplicationDbContext` (now in K-OCRLib after Phase 3) and `IEmailService` (Phase 2). `BatchCleanupService` needs `IBatchNotificationService`. Both can move now.

| Files to Move | From | Key Dependencies |
|---|---|---|
| `IBatchNotificationService` + `BatchNotificationService` | `KOCRAsp/Services/` | `ApplicationDbContext`, `IEmailService`, `IPathService` |
| `IBatchCleanupService` + `BatchCleanupService` | `KOCRAsp/Services/` | `IBatchNotificationService`, `IPathService`, `DatabaseSettings`, `KOCRDbContext` |

**Partial move — `GuestAccountCleanupService`:**
- Move the `RunCleanupCycleAsync` cleanup orchestration logic into a new `IGuestCleanupService` in K-OCRLib.
- Keep `GuestAccountCleanupService : BackgroundService` in KOCRAsp as a thin host that calls the library service.

---

## What Stays in KOCRAsp (by design)

| Class | Reason |
|---|---|
| `ApplicationUserClaimsPrincipalFactory` | `UserClaimsPrincipalFactory<>` is ASP.NET cookie-auth specific; becomes a thin adapter calling K-OCRLib's `AddKOCRClaimsAsync` |
| `RevalidatingAuthenticationStateProvider` | Blazor Server circuit lifecycle — a REST front-end would have its own token validation middleware |
| `StartupErrorState` | ASP.NET startup/host lifecycle only |
| `GuestAccountCleanupService` (`BackgroundService` host) | `IHostedService` is an ASP.NET hosting abstraction; core logic extracts to K-OCRLib |
| `Models/Validation/EmailOrGuestUserNameAttribute` | `ValidationAttribute` — ASP.NET model binding only |
| View-models (`LoginViewModel`, `HomeIndexViewModel`, etc.) | Razor/Blazor view binding only |
| All Controllers, Views, Pages, Components | UI by definition |

**Every other front-end implements its own equivalents of the ASP.NET-specific items above**, but shares all business logic, models, and services from K-OCRLib.

---

## Dependency Order Summary

```
Phase 1  →  Phase 2  →  Phase 3a/3b/3c  →  Phase 4  →  Phase 5
(no-dep)    (email)     (Identity models)   (services)   (cleanup)
```

Phases 1 and 2 are independent and can be done in either order or in parallel branches.
Phase 3 must complete before Phases 4 and 5.
Phase 5 can start after Phases 2 and 3 are complete.

---

## Checklist Before Each Phase

- [ ] `dotnet build KOCRAsp\KOCRAsp.csproj` passes
- [ ] `dotnet test KOCRAsp.Tests\KOCRAsp.Tests.csproj` passes (excluding pre-existing failures)
- [ ] All moved types have updated `namespace` and `using` declarations
- [ ] KOCRAsp `using` statements updated to reference K-OCRLib namespace
- [ ] No circular references (K-OCRLib must not reference KOCRAsp)
