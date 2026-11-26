using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;

namespace RepairHub.Infrastructure.Persistence.Configurations;

internal sealed class RepairOrderEventConfiguration : IEntityTypeConfiguration<RepairOrderEvent>
{
    public void Configure(EntityTypeBuilder<RepairOrderEvent> builder)
    {
        builder.ToTable("repair_order_events", t =>
            t.HasCheckConstraint("ck_repair_order_events_type", EnumCheck.Sql<RepairOrderEventType>("type")));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.OldValue).HasMaxLength(4000);
        builder.Property(e => e.NewValue).HasMaxLength(4000);
        builder.Property(e => e.Comment).HasMaxLength(2000);

        builder.HasIndex(e => new { e.RepairOrderId, e.OccurredAt });

        builder.HasOne<RepairOrder>().WithMany()
            .HasForeignKey(e => new { e.WorkshopId, e.RepairOrderId })
            .HasPrincipalKey(o => new { o.WorkshopId, o.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(e => new { e.WorkshopId, e.ActorUserId })
            .HasPrincipalKey(u => new { u.WorkshopId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
