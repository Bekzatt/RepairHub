namespace RepairHub.Domain.RepairOrders;

public enum RepairOrderStatus
{
    Received,
    Diagnostics,
    AwaitingApproval,
    WaitingForParts,
    InRepair,
    ReadyForPickup,
    Issued,
    Cancelled,
}

/// <summary>Result of the repair, recorded when the device becomes ready for pickup.</summary>
public enum RepairOutcome
{
    Repaired,
    NotRepaired,
    Declined,
}
