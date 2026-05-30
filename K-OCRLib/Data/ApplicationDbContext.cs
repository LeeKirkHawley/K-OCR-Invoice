using K_OCR.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace K_OCR.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations { get; set; } = null!;
    public DbSet<UserOrganizationMembership> UserOrganizationMemberships { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Organization)
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<UserOrganizationMembership>()
            .HasKey(m => new { m.UserId, m.OrganizationId });

        builder.Entity<UserOrganizationMembership>()
            .HasOne(m => m.User)
            .WithMany(u => u.OrganizationMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserOrganizationMembership>()
            .HasOne(m => m.Organization)
            .WithMany(o => o.UserMemberships)
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserOrganizationMembership>()
            .Property(m => m.Role)
            .IsRequired()
            .HasMaxLength(64);
    }
}
