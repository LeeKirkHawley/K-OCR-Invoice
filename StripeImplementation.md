# Stripe Metered Billing Implementation Plan

## Overview

Integrate Stripe metered billing into K-OCR so that each page OCR'd is reported
to Stripe. Each organization has its own Stripe Customer and Subscription. The
platform operator (super-admin) provisions billing when onboarding an org. Org
admins can view invoices and manage their payment method via the Stripe Customer
Portal. OCR is blocked for any org whose subscription is not active.

---

## Billing Model

| Concept | Value |
|---|---|
| Stripe product | "K-OCR Pages" (one product, multiple metered prices — one per tier) |
| Price type | Metered, per-unit (1 unit = 1 page), aggregate = sum |
| Billing interval | Monthly |
| Customer granularity | One Stripe Customer + Subscription per Organization |
| Price tier | Assigned per org; can be changed by super-admin at any time |
| Usage reporting trigger | After every successful OCR operation (batch or single-file) |
| Gate | OCR blocked if `StripeSubscriptionStatus` ≠ `active` or `trialing` |

---

## Architecture Notes

- **K-OCRLib** — `IStripeUsageService` + `StripeUsageService`. All processing
  logic lives here. Stripe SDK (`Stripe.net`) added to `K-OCRLib.csproj`.
- **KOCRAsp** — DI registration, configuration, webhook controller, billing
  controller, super-admin UI changes. No Stripe business logic.
- **Master DB** — Stripe fields added to `Organization` (in `KOCRAsp`,
  `ApplicationDbContext`). These must remain in KOCRAsp since `Organization`
  lives there and uses `ApplicationDbContext`.

---

## Phase 1 — Stripe Account & Product Setup (Manual, One-Time)

1. Create Stripe account (or use existing).
2. In Stripe Dashboard → Products: create **"Hardscrabble Pages"**.
3. Add **one metered price per tier** (per unit, aggregate `sum`, monthly):
   - e.g. `price_starter`, `price_professional`, `price_enterprise`
   - Note each Price ID — they will be entered into config.
4. Enable the **Customer Portal** in Stripe Dashboard settings.
5. Add keys to `appsettings.json` (real values in user secrets / Key Vault):

```json
"Stripe": {
  "SecretKey": "sk_live_...",
  "WebhookSecret": "whsec_...",
  "Prices": {
    "Starter":      "price_xxx",
    "Professional": "price_yyy",
    "Enterprise":   "price_zzz"
  }
}
```

Price names and IDs are configuration only — adding or retiring a tier never
requires a code change, only a config update.

---

## Phase 2 — Database Schema (KOCRAsp — ApplicationDbContext)

Add billing fields to `Organization`:

```csharp
// KOCRAsp/Identity/Organization.cs
public string? StripeCustomerId         { get; set; }
public string? StripeSubscriptionId     { get; set; }
public string? StripeSubscriptionItemId { get; set; }  // needed to report usage
public string? StripePriceId            { get; set; }  // which tier this org is on
public string  StripeSubscriptionStatus { get; set; } = "none"; // cached, updated by webhook
```

`StripePriceId` is the Stripe Price ID currently active for this org's
subscription item. It is stored locally so the super-admin UI can display the
current tier and so a tier change can be detected and applied.

Generate EF migration:
```
dotnet ef migrations add AddOrganizationStripeBilling --project KOCRAsp --context ApplicationDbContext
```

---

## Phase 3 — StripeUsageService (K-OCRLib)

**`K-OCRLib/Services/IStripeUsageService.cs`**
```csharp
public interface IStripeUsageService
{
    /// <summary>Report OCR page usage to Stripe for an org's subscription item.</summary>
    Task ReportUsageAsync(string subscriptionItemId, long pageCount);

    /// <summary>True if the subscription status allows OCR to proceed.</summary>
    bool IsStatusActive(string subscriptionStatus);
}
```

**`K-OCRLib/Services/StripeUsageService.cs`**
- Add NuGet: `Stripe.net` to `K-OCRLib.csproj`
- Constructor takes `IConfiguration` and `ILogger<StripeUsageService>`
- `ReportUsageAsync`: calls `UsageRecordService.CreateAsync` with
  `quantity = pageCount`, `action = set` (or `increment`), timestamp = now
- `IsStatusActive`: returns `status is "active" or "trialing"`
- Failures are logged and swallowed — a Stripe error must never abort OCR

---

## Phase 4 — OCR Subscription Gate (K-OCRLib + KOCRAsp)

### 4a. Extend ITenantContext
Add to `K-OCRLib/Data/ITenantContext.cs`:
```csharp
string? StripeSubscriptionItemId { get; }
string  StripeSubscriptionStatus { get; }
```

Implement in `KOCRAsp/Services/TenantContext.cs` by reading from the
`Organization` entity (injected via `ApplicationDbContext` or a scoped
`IOrganizationBillingService`).

### 4b. Gate in BatchService (K-OCRLib)
At the top of `TriggerOcrAsync`, before processing:
```csharp
if (!_stripeUsage.IsStatusActive(_tenantContext.StripeSubscriptionStatus))
    throw new InvalidOperationException("OCR is unavailable: subscription inactive.");
```

### 4c. Gate in HomeController (KOCRAsp)
At the top of `StartOcr`, before calling `ProcessFileAsync`:
```csharp
if (!_stripeUsage.IsStatusActive(_tenantContext.StripeSubscriptionStatus))
    return Json(new { success = false, error = "OCR is unavailable: subscription inactive." });
```

---

## Phase 5 — Report Usage After OCR

Both existing call sites already call `RecordBatchOcrEventAsync` after OCR.
Extend these same locations to also call `IStripeUsageService.ReportUsageAsync`.

### In BatchService.TriggerOcrAsync (K-OCRLib)
```csharp
var totalPages = invoiceResults.Sum(r => r.PageCount);
await _stripeUsage.ReportUsageAsync(
    _tenantContext.StripeSubscriptionItemId!, totalPages);
```

### In HomeController.StartOcr (KOCRAsp)
```csharp
await _stripeUsage.ReportUsageAsync(
    _tenantContext.StripeSubscriptionItemId!, invoice?.PageCount ?? 1);
```

Both calls wrapped in try/catch — Stripe failure never aborts OCR.

---

## Phase 6 — Webhook Handler (KOCRAsp)

**`KOCRAsp/Controllers/StripeWebhookController.cs`**

- Route: `POST /stripe/webhook`
- Exempt from CSRF and authentication
- Verify Stripe signature using `WebhookSecret` from config
- Handle events:
  | Event | Action |
  |---|---|
  | `customer.subscription.updated` | Update `Organization.StripeSubscriptionStatus` |
  | `customer.subscription.deleted` | Set status to `canceled` |
  | `invoice.payment_failed` | Set status to `past_due` (or log) |
  | `invoice.payment_succeeded` | Ensure status is `active` |
- Update `Organization` via `ApplicationDbContext` and save

---

## Phase 7 — Super-Admin UI (KOCRAsp)

Add a **"Billing"** section to the org management page (existing super-admin
views under `Views/Admin/` or similar):

- **Create Stripe Customer** button: calls Stripe API to create a customer
  (`name = org.Name`, `metadata["orgId"] = org.Id`), saves `StripeCustomerId`.
- **Create Subscription** dropdown + button: super-admin selects a price tier
  from a list populated from `Stripe:Prices` config, then clicks "Create
  Subscription". Saves `StripeSubscriptionId`, `StripeSubscriptionItemId`,
  `StripePriceId`, and sets initial status.
- **Change Tier** dropdown: if a subscription already exists, selecting a
  different tier calls Stripe `SubscriptionItemService.UpdateAsync` with the new
  `Price` ID (Stripe handles proration). Updates `StripePriceId` and
  `StripeSubscriptionItemId` locally.
- **Or: manually enter** existing Stripe Customer ID / Subscription Item ID /
  Price ID (for orgs migrated from manual billing).
- Display current `StripeSubscriptionStatus` and current tier name.

---

## Phase 8 — Org Admin Billing Portal (KOCRAsp)

**`KOCRAsp/Controllers/BillingController.cs`**

- Route: `GET /billing/portal`
- Requires org-admin or higher role
- Calls Stripe `BillingPortalSessionService.CreateAsync` with
  `CustomerId = org.StripeCustomerId`,
  `ReturnUrl = <app root>`
- Redirects to the session URL (Stripe-hosted portal)

Add a **"Billing"** nav link in the org-admin layout pointing to `/billing/portal`.

---

## Phase 9 — DI Registration (KOCRAsp/Program.cs)

```csharp
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];
builder.Services.AddScoped<IStripeUsageService, StripeUsageService>();
```

---

## File Inventory

| File | Action |
|---|---|
| `K-OCRLib/Services/IStripeUsageService.cs` | Create |
| `K-OCRLib/Services/StripeUsageService.cs` | Create |
| `K-OCRLib/Data/ITenantContext.cs` | Modify — add Stripe fields |
| `K-OCRLib/Services/BatchService.cs` | Modify — gate + report usage |
| `K-OCRLib/K-OCRLib.csproj` | Modify — add `Stripe.net` |
| `KOCRAsp/Identity/Organization.cs` | Modify — add Stripe fields |
| `KOCRAsp/Services/TenantContext.cs` | Modify — implement Stripe fields |
| `KOCRAsp/Controllers/HomeController.cs` | Modify — gate + report usage |
| `KOCRAsp/Controllers/StripeWebhookController.cs` | Create |
| `KOCRAsp/Controllers/BillingController.cs` | Create |
| `KOCRAsp/Views/Admin/` (org detail view) | Modify — billing section |
| `KOCRAsp/Views/Shared/_OrgAdminLayout.cshtml` (or nav) | Modify — Billing link |
| `KOCRAsp/Program.cs` | Modify — Stripe DI registration |
| `KOCRAsp/Migrations/` | New migration for Org Stripe fields |
| `appsettings.json` / user secrets | Add `Stripe:SecretKey`, `Stripe:WebhookSecret`, `Stripe:Prices:*` |

---

## Open Questions / Decisions Deferred

- **Trial period**: Should new orgs get N free pages or a time-limited trial
  before requiring a subscription? If so, `trialing` status already handled by
  the gate — just needs Stripe trial configuration.
- **Usage granularity**: Currently reporting total pages per batch. Could also
  report per-invoice if finer Stripe usage records are desired.
- **Overage policy**: Stripe metered billing allows unlimited usage and bills
  after the fact. No enforcement cap is planned unless requested.
- **Guest organizations**: `Organization.IsGuestOrganization` orgs likely should
  be excluded from billing — confirm whether they should be gated differently.
