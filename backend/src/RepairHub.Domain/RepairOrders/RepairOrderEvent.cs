using RepairHub.Domain.Common;

namespace RepairHub.Domain.RepairOrders;

/// <summary>
/// One entry of a repair order's history. Append-only: there are no methods that change an event,
/// no API to edit or delete one, and the persistence layer refuses to update or delete them.
/// </summary>
public sealed class RepairOrderEvent : ITenantOwned
{
    private RepairOrderEvent() { } // EF Core

    public Guid Id { get; private set; }
    public Guid WorkshopId { get; private set; }
    public Guid RepairOrderId { get; private set; }
    public RepairOrderEventType Type { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public string? Comment { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    internal static RepairOrderEvent Create(
        RepairOrder order, RepairOrderEventType type, string? oldValue, string? newValue, string? comment,
        Guid actorUserId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            WorkshopId = order.WorkshopId,
            RepairOrderId = order.Id,
            Type = type,
            OldValue = oldValue,
            NewValue = newValue,
            Comment = comment,
            ActorUserId = actorUserId,
            OccurredAt = now,
        };
}
