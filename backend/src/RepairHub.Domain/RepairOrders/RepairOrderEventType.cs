namespace RepairHub.Domain.RepairOrders;

public enum RepairOrderEventType
{
    /// <summary>NewValue: the order number.</summary>
    Created,

    /// <summary>Old/New: status names. Comment: the reason or comment given with the change.</summary>
    StatusChanged,

    /// <summary>NewValue: the outcome; recorded together with the change to ReadyForPickup.</summary>
    OutcomeRecorded,

    /// <summary>Old/New: user ids (null = unassigned).</summary>
    TechnicianAssigned,

    DiagnosticsUpdated,
    ProblemUpdated,
    ConditionUpdated,
    AccessoriesUpdated,

    /// <summary>Old/New: invariant decimal strings.</summary>
    EstimateChanged,
    FinalPriceChanged,

    /// <summary>Old/New: ISO dates (yyyy-MM-dd).</summary>
    ExpectedDateChanged,

    /// <summary>Old/New: JSON objects containing only the corrected snapshot fields.</summary>
    CustomerInfoCorrected,
    DeviceInfoCorrected,

    /// <summary>NewValue: the note text.</summary>
    NoteAdded,
}
