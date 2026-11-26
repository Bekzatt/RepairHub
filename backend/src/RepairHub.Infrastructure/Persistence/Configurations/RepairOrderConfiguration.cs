using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepairHub.Domain.Customers;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;

namespace RepairHub.Infrastructure.Persistence.Configurations;

internal sealed class RepairOrderConfiguration : IEntityTypeConfiguration<RepairOrder>
{
    public const string UniqueNumberIndex = "ux_repair_orders_workshop_id_number";

    public void Configure(EntityTypeBuilder<RepairOrder> builder)
    {
        builder.ToTable("repair_orders", t =>
        {
            t.HasCheckConstraint("ck_repair_orders_status", EnumCheck.Sql<RepairOrderStatus>("status"));
            t.HasCheckConstraint("ck_repair_orders_outcome", "outcome IS NULL OR " + EnumCheck.Sql<RepairOutcome>("outcome"));
            t.HasCheckConstraint("ck_repair_orders_device_type", EnumCheck.Sql<DeviceType>("device_type"));
            t.HasCheckConstraint("ck_repair_orders_prices",
                "(estimated_price IS NULL OR estimated_price >= 0) AND (final_price IS NULL OR final_price >= 0)");
        });
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.HasAlternateKey(o => new { o.WorkshopId, o.Id });

        builder.Property(o => o.Number).HasMaxLength(20).IsRequired();
        builder.Property(o => o.NumberYear).HasColumnName("number_year");
        builder.Property(o => o.NumberSequence).HasColumnName("number_seq");

        builder.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.CustomerPhone).HasMaxLength(32).IsRequired();
        builder.Property(o => o.DeviceType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.DeviceBrand).HasMaxLength(100).IsRequired();
        builder.Property(o => o.DeviceModel).HasMaxLength(150).IsRequired();
        builder.Property(o => o.DeviceSerialNumber).HasMaxLength(100);
        builder.Property(o => o.DeviceImei).HasMaxLength(16);

        builder.Property(o => o.Problem).HasMaxLength(2000).IsRequired();
        builder.Property(o => o.Condition).HasMaxLength(2000);
        builder.Property(o => o.Accessories).HasMaxLength(1000);
        builder.Property(o => o.Diagnostics).HasMaxLength(4000);

        builder.Property(o => o.EstimatedPrice).HasPrecision(14, 2);
        builder.Property(o => o.FinalPrice).HasPrecision(14, 2);
        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        // Maps to PostgreSQL's xmin system column: changes on every UPDATE of the row.
        builder.Property(o => o.Version).IsRowVersion();

        builder.HasIndex(o => new { o.WorkshopId, o.Number }).IsUnique().HasDatabaseName(UniqueNumberIndex);
        builder.HasIndex(o => new { o.WorkshopId, o.Status, o.ReceivedAt });
        builder.HasIndex(o => new { o.WorkshopId, o.AssignedTechnicianId, o.Status });
        builder.HasIndex(o => new { o.WorkshopId, o.CustomerId });
        builder.HasIndex(o => new { o.WorkshopId, o.DeviceId });
        builder.HasIndex(o => new { o.WorkshopId, o.ReceivedAt });

        // Same-workshop FKs: customer, device and technician must all belong to the order's workshop.
        builder.HasOne<Customer>().WithMany()
            .HasForeignKey(o => new { o.WorkshopId, o.CustomerId })
            .HasPrincipalKey(c => new { c.WorkshopId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany()
            .HasForeignKey(o => new { o.WorkshopId, o.DeviceId })
            .HasPrincipalKey(d => new { d.WorkshopId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(o => new { o.WorkshopId, o.AssignedTechnicianId })
            .HasPrincipalKey(u => new { u.WorkshopId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(o => new { o.WorkshopId, o.CreatedById })
            .HasPrincipalKey(u => new { u.WorkshopId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(o => new { o.WorkshopId, o.UpdatedById })
            .HasPrincipalKey(u => new { u.WorkshopId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
