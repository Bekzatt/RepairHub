using RepairHub.Domain.Users;

namespace RepairHub.Domain.RepairOrders;

/// <summary>Who may perform a status transition.</summary>
[Flags]
public enum TransitionActors
{
    /// <summary>Owner and Admin.</summary>
    Managers = 1,
    Receptionist = 2,

    /// <summary>A user with the Technician role who is assigned to the order.</summary>
    AssignedTechnician = 4,

    FloorWork = Managers | AssignedTechnician,
    FrontDesk = Managers | Receptionist,
    Everyone = Managers | Receptionist | AssignedTechnician,
}

public sealed record StatusTransition(
    RepairOrderStatus From,
    RepairOrderStatus To,
    TransitionActors Actors,
    IReadOnlyList<RepairOutcome> Outcomes)
{
    public bool RequiresOutcome => To == RepairOrderStatus.ReadyForPickup;
    public bool RequiresReason => To == RepairOrderStatus.Cancelled;
}

/// <summary>
/// The single source of truth for the repair order lifecycle. The API exposes the transitions available to the
/// current user (<see cref="AvailableTo"/>); the frontend never re-implements these rules.
/// <para>
/// Cancelled = created by mistake or cancelled BEFORE repair work began: it is impossible once the order has
/// been in InRepair. When work has started and the device cannot be repaired or the customer declines, the
/// order goes to ReadyForPickup with an outcome and then to Issued, so the handover is recorded.
/// Issued and Cancelled are final.
/// </para>
/// </summary>
public static class RepairOrderWorkflow
{
    private static readonly RepairOutcome[] None = [];
    private static readonly RepairOutcome[] AnyOutcome = [RepairOutcome.Repaired, RepairOutcome.NotRepaired, RepairOutcome.Declined];
    private static readonly RepairOutcome[] Unrepaired = [RepairOutcome.NotRepaired, RepairOutcome.Declined];

    public static IReadOnlyList<StatusTransition> Transitions { get; } =
    [
        T(RepairOrderStatus.Received, RepairOrderStatus.Diagnostics, TransitionActors.FloorWork),
        T(RepairOrderStatus.Received, RepairOrderStatus.InRepair, TransitionActors.FloorWork),
        T(RepairOrderStatus.Received, RepairOrderStatus.Cancelled, TransitionActors.FrontDesk),

        T(RepairOrderStatus.Diagnostics, RepairOrderStatus.AwaitingApproval, TransitionActors.FloorWork),
        T(RepairOrderStatus.Diagnostics, RepairOrderStatus.InRepair, TransitionActors.FloorWork),
        T(RepairOrderStatus.Diagnostics, RepairOrderStatus.WaitingForParts, TransitionActors.FloorWork),
        T(RepairOrderStatus.Diagnostics, RepairOrderStatus.ReadyForPickup, TransitionActors.FloorWork, Unrepaired),
        T(RepairOrderStatus.Diagnostics, RepairOrderStatus.Cancelled, TransitionActors.FrontDesk),

        T(RepairOrderStatus.AwaitingApproval, RepairOrderStatus.InRepair, TransitionActors.Everyone),
        T(RepairOrderStatus.AwaitingApproval, RepairOrderStatus.WaitingForParts, TransitionActors.Everyone),
        T(RepairOrderStatus.AwaitingApproval, RepairOrderStatus.ReadyForPickup, TransitionActors.Everyone, Unrepaired),
        T(RepairOrderStatus.AwaitingApproval, RepairOrderStatus.Diagnostics, TransitionActors.FloorWork),
        T(RepairOrderStatus.AwaitingApproval, RepairOrderStatus.Cancelled, TransitionActors.FrontDesk),

        T(RepairOrderStatus.WaitingForParts, RepairOrderStatus.InRepair, TransitionActors.FloorWork),
        T(RepairOrderStatus.WaitingForParts, RepairOrderStatus.AwaitingApproval, TransitionActors.Everyone),
        T(RepairOrderStatus.WaitingForParts, RepairOrderStatus.ReadyForPickup, TransitionActors.FloorWork, Unrepaired),
        T(RepairOrderStatus.WaitingForParts, RepairOrderStatus.Cancelled, TransitionActors.FrontDesk),

        T(RepairOrderStatus.InRepair, RepairOrderStatus.WaitingForParts, TransitionActors.FloorWork),
        T(RepairOrderStatus.InRepair, RepairOrderStatus.AwaitingApproval, TransitionActors.FloorWork),
        T(RepairOrderStatus.InRepair, RepairOrderStatus.ReadyForPickup, TransitionActors.FloorWork, AnyOutcome),

        T(RepairOrderStatus.ReadyForPickup, RepairOrderStatus.InRepair, TransitionActors.Everyone),
        T(RepairOrderStatus.ReadyForPickup, RepairOrderStatus.Issued, TransitionActors.FrontDesk),
    ];

    private static StatusTransition T(
        RepairOrderStatus from, RepairOrderStatus to, TransitionActors actors, RepairOutcome[]? outcomes = null) =>
        new(from, to, actors, outcomes ?? None);

    public static readonly RepairOrderStatus[] ClosedStatuses = [RepairOrderStatus.Issued, RepairOrderStatus.Cancelled];

    /// <summary>Statuses in which the repair is not finished yet; used for "overdue".</summary>
    public static readonly RepairOrderStatus[] InProgressStatuses =
    [
        RepairOrderStatus.Received, RepairOrderStatus.Diagnostics, RepairOrderStatus.AwaitingApproval,
        RepairOrderStatus.WaitingForParts, RepairOrderStatus.InRepair,
    ];

    public static bool IsClosed(RepairOrderStatus status) => ClosedStatuses.Contains(status);

    public static StatusTransition? Find(RepairOrderStatus from, RepairOrderStatus to) =>
        Transitions.FirstOrDefault(t => t.From == from && t.To == to);

    /// <summary>Whether the transition is valid in the order's current state (regardless of who performs it).</summary>
    public static bool IsPossible(RepairOrder order, StatusTransition transition) =>
        transition.From == order.Status
        && !(transition.To == RepairOrderStatus.Cancelled && order.StartedAt is not null);

    public static bool IsAllowedFor(StatusTransition transition, UserRole role, bool isAssignedToOrder) => role switch
    {
        UserRole.Owner or UserRole.Admin => transition.Actors.HasFlag(TransitionActors.Managers),
        UserRole.Receptionist => transition.Actors.HasFlag(TransitionActors.Receptionist),
        UserRole.Technician => isAssignedToOrder && transition.Actors.HasFlag(TransitionActors.AssignedTechnician),
        _ => false,
    };

    /// <summary>The transitions the given user may perform on the order right now.</summary>
    public static IReadOnlyList<StatusTransition> AvailableTo(RepairOrder order, UserRole role, Guid userId) =>
        Transitions
            .Where(t => IsPossible(order, t) && IsAllowedFor(t, role, order.AssignedTechnicianId == userId))
            .ToList();
}
