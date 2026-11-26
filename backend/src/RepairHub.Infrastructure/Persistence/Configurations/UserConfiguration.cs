using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepairHub.Domain.Users;
using RepairHub.Domain.Workshops;

namespace RepairHub.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string UniqueEmailIndex = "ux_users_normalized_email";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t => t.HasCheckConstraint(
            "ck_users_role", "role IN ('Owner', 'Admin', 'Technician', 'Receptionist')"));
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(u => u.Phone).HasMaxLength(32);
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(u => u.TokenVersion).IsRequired();

        // Login is by email only, so email is unique across all workshops.
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName(UniqueEmailIndex);
        builder.HasIndex(u => new { u.WorkshopId, u.Role, u.IsActive });

        builder.HasOne<Workshop>()
            .WithMany()
            .HasForeignKey(u => u.WorkshopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
