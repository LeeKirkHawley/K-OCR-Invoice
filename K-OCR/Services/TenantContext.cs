using K_OCR.Data;

namespace K_OCR.Services;

/// <summary>
/// Reads the current tenant from the per-circuit <see cref="WorkspaceState"/>.
/// Scoped lifetime mirrors WorkspaceState so it stays in sync with the
/// authenticated user for the full lifetime of one Blazor circuit.
/// </summary>
internal sealed class TenantContext : ITenantContext
{
    private readonly WorkspaceState _state;

    public TenantContext(WorkspaceState state)
    {
        _state = state;
    }

    public string? OrganizationId   => _state.TenantId;
    public string? OrganizationName => _state.TenantName;
    public bool IsSuperAdmin        => _state.IsSuperAdmin;
}
