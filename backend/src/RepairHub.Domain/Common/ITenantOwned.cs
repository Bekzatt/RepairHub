namespace RepairHub.Domain.Common;

/// <summary>
/// Marks an entity that belongs to exactly one workshop (tenant).
/// Every such entity automatically receives the fail-closed tenant query filter.
/// </summary>
public interface ITenantOwned
{
    Guid WorkshopId { get; }
}
