using System.ComponentModel.DataAnnotations;

namespace KOCRAsp.Identity;

public class Organization
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsGuestOrganization { get; set; }

    public DateTime? MarkedForDeletionAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
}
