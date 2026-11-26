using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepairHub.Domain.Common;
using RepairHub.Domain.Workshops;

namespace RepairHub.Infrastructure.Persistence;

/// <summary>
/// Last repair order number handed out per workshop and year. Only used through
/// <see cref="AppDbContext.NextRepairOrderSequenceAsync"/>; mapped so that migrations create the table.
/// </summary>
internal sealed class RepairOrderCounter : ITenantOwned
{
    public Guid WorkshopId { get; private set; }
    public int Year { get; private set; }
    public int LastValue { get; private set; }
}

internal sealed class RepairOrderCounterConfiguration : IEntityTypeConfiguration<RepairOrderCounter>
{
    public void Configure(EntityTypeBuilder<RepairOrderCounter> builder)
    {
        builder.ToTable("repair_order_counters");
        builder.HasKey(c => new { c.WorkshopId, c.Year });
        builder.HasOne<Workshop>().WithMany().HasForeignKey(c => c.WorkshopId).OnDelete(DeleteBehavior.Restrict);
    }
}
