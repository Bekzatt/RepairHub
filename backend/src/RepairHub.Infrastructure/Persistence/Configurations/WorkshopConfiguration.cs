using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepairHub.Domain.Workshops;

namespace RepairHub.Infrastructure.Persistence.Configurations;

internal sealed class WorkshopConfiguration : IEntityTypeConfiguration<Workshop>
{
    public void Configure(EntityTypeBuilder<Workshop> builder)
    {
        builder.ToTable("workshops");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Address).HasMaxLength(500);
        builder.Property(w => w.Phone).HasMaxLength(32);
        builder.Property(w => w.Currency).HasMaxLength(3).IsFixedLength()
            .HasDefaultValue(Workshop.DefaultCurrency).IsRequired();
        builder.Property(w => w.TimeZone).HasMaxLength(64)
            .HasDefaultValue(Workshop.DefaultTimeZone).IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();
    }
}
