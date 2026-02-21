# K-OCR Identity & Multi-Tenant Plan

## Vision
- K-OCR-API owns ASP.NET Core Identity.
- JWTs are prioritized; cookies remain optional for "remember me" or browser session needs, but each token must include `TenantId`.
- Each tenant is called an **Organization**. Organizations get an admin user with a temporary password; they can invite additional users.
- A special **super-admin** (global admin) role can create/revoke organizations and users across tenants.
- Blazor UI surfaces admin workflows: a super-admin Administration page (for organization lifecycle + revocation) and organization-level user management dialogs.

## Tasks
1. **Data/Auth model updates (K-OCR-API)**
   - Add `Organization` entity and extend `ApplicationUser` with `OrganizationId`.
   - Seed/configure a `SuperAdmin` role; each organization has an admin user already tied to it.
   - Configure ASP.NET Core Identity to issue JWTs (and optional cookies) that include `TenantId` claims.
   - Add an authorization policy `RequireTenantId` and `SuperAdminOnly` policy.

2. **Super-admin APIs**
   - Create endpoints for listing organizations, creating organizations (auto-creates tenant admin with temporary password + invitation token), and revoking access (disables org/users, bumps security stamp).
   - Protect them via `SuperAdminOnly` policy and ensure they enforce `TenantId`/organization constraints.

3. **Organization admin APIs**
   - Expose tenant-scoped endpoints so an organization admin can invite/add users and manage them.
   - Ensure every request validates the caller’s `OrganizationId` claim and forbids cross-tenant operations.

4. **Blazor UI alignment**
   - Add a super-admin “Administration” page (visible only to super-admins) to create organizations, view existing ones, and revoke access.
   - Build dialogs for inviting new organization users with temporary passwords and instructions to change them on first login.
   - Surface tenant-authenticated workspace state that carries `TenantId`, tokens, and current roles.

5. **Token handling & security recommendations**
   - Store JWTs (with `TenantId` claim) in Blazor and attach them to API requests using `Authorization: Bearer` headers.
   - Cookies remain optional for clients needing persistent browser sessions or compatibility.
   - Generate temporary passwords/invitation tokens for new admins/users and force password reset on first login.
   - Revoke access by incrementing `SecurityStamp` or maintaining a token blacklist and log organization/user lifecycle events for auditing.

### ✅ Step 5 — Token handling & security recommendations
- Added `/api/auth/login` and `/api/auth/reset-password` along with a `JwtTokenService` so every token carries tenant metadata, roles, and the security stamp.
- Blazor now stores tokens/tenant info in `WorkspaceState`, uses a shared `KocrApiClient` to add `Authorization: Bearer` headers, and exposes routes for authentication, super-admin, and organization user flows.
- Authentication UI includes both login and invitation-token password-reset forms; the super-admin/organization pages consume the new APIs and surface the temporary credentials the way Step 4 envisioned.

## Recommendations
- Treat the temporary admin password as ephemeral, delivered via email or UI invitation, and force a reset.
- When revoking access, bump the `SecurityStamp` (and optionally track refresh tokens) so outstanding JWTs become invalid.
- Maintain audit logs for who created/revoked organizations or users for compliance.
