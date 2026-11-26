using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepairHub.Domain.Customers;
using RepairHub.Domain.Devices;

namespace RepairHub.Infrastructure.Persistence.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices", t => t.HasCheckConstraint("ck_devices_type", EnumCheck.Sql<DeviceType>("type")));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.HasAlternateKey(d => new { d.WorkshopId, d.Id });

        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Brand).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Model).HasMaxLength(150).IsRequired();
        builder.Property(d => d.SerialNumber).HasMaxLength(100);
        builder.Property(d => d.Imei).HasMaxLength(16);
        builder.Property(d => d.Description).HasMaxLength(1000);

        builder.HasIndex(d => new { d.WorkshopId, d.CustomerId });
        builder.HasIndex(d => new { d.WorkshopId, d.SerialNumber });
        builder.HasIndex(d => new { d.WorkshopId, d.Imei });

        // Same-workshop FK: a device can only belong to a customer of its own workshop.
        // (workshop_id is covered by the customer's own FK to workshops.)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(d => new { d.WorkshopId, d.CustomerId })
            .HasPrincipalKey(c => new { c.WorkshopId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
