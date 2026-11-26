using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using RepairHub.Domain.Common;
using RepairHub.Domain.Customers;
using RepairHub.Domain.Devices;

namespace RepairHub.Domain.RepairOrders;

/// <summary>Customer details copied onto an order when it is created.</summary>
public sealed record CustomerSnapshot(string Name, string Phone)
{
    public static CustomerSnapshot Of(Customer customer) => new(customer.FullName, customer.Phone);
}

/// <summary>Device details copied onto an order when it is created.</summary>
public sealed record DeviceSnapshot(DeviceType Type, string Brand, string Model, string? SerialNumber, string? Imei)
{
    public static DeviceSnapshot Of(Device device) =>
        new(device.Type, device.Brand, device.Model, device.SerialNumber, device.Imei);
}

public static class RepairOrderErrors
{
    public const string InvalidStatusTransition = "invalid_status_transition";
    public const string OutcomeRequired = "outcome_required";
    public const string OutcomeNotAllowed = "outcome_not_allowed";
    public const string ReasonRequired = "reason_required";
    public const string OrderClosed = "order_closed";
}

/// <summary>
/// A repair order: the central record of the workshop. Every mutating method returns the history events it
/// produced; the caller persists them in the same transaction as the order.
/// <para>
/// Customer and device details are SNAPSHOTS taken at creation. Editing the customer or device record never
/// changes them; corrections are made on the order itself and recorded in its history.
/// </para>
/// </summary>
public sealed class RepairOrder : ITenantOwned
{
    private RepairOrder() { } // EF Core

    public Guid Id { get; private set; }
    public Guid WorkshopId { get; private set; }

    /// <summary>Human-readable number, e.g. RH-2026-000152. Never changes.</summary>
    public string Number { get; private set; } = null!;
    public int NumberYear { get; private set; }
    public int NumberSequence { get; private set; }

    public Guid CustomerId { get; private set; }
    public Guid DeviceId { get; private set; }

    public string CustomerName { get; private set; } = null!;
    public string CustomerPhone { get; private set; } = null!;
    public DeviceType DeviceType { get; private set; }
    public string DeviceBrand { get; private set; } = null!;
    public string DeviceModel { get; private set; } = null!;
    public string? DeviceSerialNumber { get; private set; }
    public string? DeviceImei { get; private set; }

    public string Problem { get; private set; } = null!;
    public string? Condition { get; private set; }
    public string? Accessories { get; private set; }
    public string? Diagnostics { get; private set; }

    public decimal? EstimatedPrice { get; private set; }
    public decimal? FinalPrice { get; private set; }

    /// <summary>Workshop currency at the time the order was created.</summary>
    public string Currency { get; private set; } = null!;

    public RepairOrderStatus Status { get; private set; }
    public RepairOutcome? Outcome { get; private set; }
    public Guid? AssignedTechnicianId { get; private set; }
    public Guid CreatedById { get; private set; }
    public Guid UpdatedById { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>A calendar date in the workshop's time zone, not an instant.</summary>
    public DateOnly? ExpectedCompletionDate { get; private set; }

    /// <summary>First time the order entered InRepair. Once set, the order can no longer be cancelled.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the order last became ReadyForPickup; cleared on rework.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public static string FormatNumber(int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"RH-{year}-{sequence:D6}");

    public static (RepairOrder Order, IReadOnlyList<RepairOrderEvent> Events) Create(
        Guid workshopId,
        int numberYear,
        int numberSequence,
        Guid customerId,
        CustomerSnapshot customer,
        Guid deviceId,
        DeviceSnapshot device,
        string problem,
        string? condition,
        string? accessories,
        Guid? assignedTechnicianId,
        DateOnly? expectedCompletionDate,
        decimal? estimatedPrice,
        string currency,
        Guid actorId,
        DateTimeOffset now)
    {
        var order = new RepairOrder
        {
            Id = Guid.CreateVersion7(now),
            WorkshopId = workshopId,
            Number = FormatNumber(numberYear, numberSequence),
            NumberYear = numberYear,
            NumberSequence = numberSequence,
            CustomerId = customerId,
            DeviceId = deviceId,
            Problem = Text.Required(problem),
            Condition = Text.Optional(condition),
            Accessories = Text.Optional(accessories),
            AssignedTechnicianId = assignedTechnicianId,
            ExpectedCompletionDate = expectedCompletionDate,
            EstimatedPrice = EnsurePrice(estimatedPrice),
            Currency = currency,
            Status = RepairOrderStatus.Received,
            CreatedById = actorId,
            UpdatedById = actorId,
            ReceivedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        order.ApplyCustomerSnapshot(customer);
        order.ApplyDeviceSnapshot(device);

        var events = new List<RepairOrderEvent> { order.Event(RepairOrderEventType.Created, null, order.Number, null, actorId, now) };
        if (assignedTechnicianId is { } technicianId)
            events.Add(order.Event(RepairOrderEventType.TechnicianAssigned, null, technicianId.ToString(), null, actorId, now));
        return (order, events);
    }

    public bool IsClosed => RepairOrderWorkflow.IsClosed(Status);

    /// <summary>
    /// Moves the order to <paramref name="to"/>. Validates that the transition exists and that outcome and reason
    /// are given where required. WHO may perform it is checked by the caller with <see cref="RepairOrderWorkflow"/>.
    /// </summary>
    public IReadOnlyList<RepairOrderEvent> ChangeStatus(
        RepairOrderStatus to, RepairOutcome? outcome, string? comment, Guid actorId, DateTimeOffset now)
    {
        var transition = RepairOrderWorkflow.Find(Status, to);
        if (transition is null || !RepairOrderWorkflow.IsPossible(this, transition))
            throw new DomainRuleException(RepairOrderErrors.InvalidStatusTransition,
                $"Cannot change status from {Status} to {to}.");

        comment = Text.Optional(comment);
        if (transition.RequiresReason && comment is null)
            throw new DomainRuleException(RepairOrderErrors.ReasonRequired, "A reason is required to cancel an order.");

        if (transition.RequiresOutcome)
        {
            if (outcome is null)
                throw new DomainRuleException(RepairOrderErrors.OutcomeRequired, "An outcome is required.");
            if (!transition.Outcomes.Contains(outcome.Value))
                throw new DomainRuleException(RepairOrderErrors.OutcomeNotAllowed,
                    $"Outcome {outcome} is not possible when moving from {Status}.");
        }
        else if (outcome is not null)
        {
            throw new DomainRuleException(RepairOrderErrors.OutcomeNotAllowed, "An outcome is only set when the order becomes ready for pickup.");
        }

        var from = Status;
        Status = to;
        switch (to)
        {
            case RepairOrderStatus.InRepair:
                StartedAt ??= now;
                if (from == RepairOrderStatus.ReadyForPickup)
                {
                    // Rework: the previous result no longer applies.
                    CompletedAt = null;
                    Outcome = null;
                }
                break;
            case RepairOrderStatus.ReadyForPickup:
                CompletedAt = now;
                Outcome = outcome;
                break;
            case RepairOrderStatus.Issued:
                IssuedAt = now;
                break;
            case RepairOrderStatus.Cancelled:
                CancelledAt = now;
                CancellationReason = comment;
                break;
        }
        Touch(actorId, now);

        var events = new List<RepairOrderEvent>
        {
            Event(RepairOrderEventType.StatusChanged, from.ToString(), to.ToString(), comment, actorId, now),
        };
        if (outcome is { } recorded)
            events.Add(Event(RepairOrderEventType.OutcomeRecorded, null, recorded.ToString(), null, actorId, now));
        return events;
    }

    public IReadOnlyList<RepairOrderEvent> AssignTechnician(Guid? technicianId, Guid actorId, DateTimeOffset now)
    {
        EnsureOpen();
        if (AssignedTechnicianId == technicianId) return [];

        var previous = AssignedTechnicianId;
        AssignedTechnicianId = technicianId;
        Touch(actorId, now);
        return [Event(RepairOrderEventType.TechnicianAssigned, previous?.ToString(), technicianId?.ToString(), null, actorId, now)];
    }

    /// <summary>
    /// History event for an assignment performed atomically in the database ("take order"), where the
    /// order row was updated with a conditional UPDATE instead of through this entity.
    /// </summary>
    public static RepairOrderEvent TakenEvent(RepairOrder order, Guid technicianId, DateTimeOffset now) =>
        order.Event(RepairOrderEventType.TechnicianAssigned, null, technicianId.ToString(), null, technicianId, now);

    public IReadOnlyList<RepairOrderEvent> UpdateDiagnostics(string? diagnostics, Guid actorId, DateTimeOffset now)
    {
        EnsureOpen();
        var events = new List<RepairOrderEvent>();
        Diagnostics = Change(Diagnostics, Text.Optional(diagnostics), RepairOrderEventType.DiagnosticsUpdated, events, actorId, now);
        if (events.Count > 0) Touch(actorId, now);
        return events;
    }

    public IReadOnlyList<RepairOrderEvent> UpdateIntake(
        string problem, string? condition, string? accessories, Guid actorId, DateTimeOffset now)
    {
        EnsureOpen();
        var events = new List<RepairOrderEvent>();
        Problem = Change(Problem, Text.Required(problem), RepairOrderEventType.ProblemUpdated, events, actorId, now)!;
        Condition = Change(Condition, Text.Optional(condition), RepairOrderEventType.ConditionUpdated, events, actorId, now);
        Accessories = Change(Accessories, Text.Optional(accessories), RepairOrderEventType.AccessoriesUpdated, events, actorId, now);
        if (events.Count > 0) Touch(actorId, now);
        return events;
    }

    public IReadOnlyList<RepairOrderEvent> UpdateTerms(
        decimal? estimatedPrice, decimal? finalPrice, DateOnly? expectedCompletionDate, Guid actorId, DateTimeOffset now)
    {
        EnsureOpen();
        var events = new List<RepairOrderEvent>();

        estimatedPrice = EnsurePrice(estimatedPrice);
        if (EstimatedPrice != estimatedPrice)
        {
            events.Add(Event(RepairOrderEventType.EstimateChanged, Format(EstimatedPrice), Format(estimatedPrice), null, actorId, now));
            EstimatedPrice = estimatedPrice;
        }

        finalPrice = EnsurePrice(finalPrice);
        if (FinalPrice != finalPrice)
        {
            events.Add(Event(RepairOrderEventType.FinalPriceChanged, Format(FinalPrice), Format(finalPrice), null, actorId, now));
            FinalPrice = finalPrice;
        }

        if (ExpectedCompletionDate != expectedCompletionDate)
        {
            events.Add(Event(RepairOrderEventType.ExpectedDateChanged,
                Format(ExpectedCompletionDate), Format(expectedCompletionDate), null, actorId, now));
            ExpectedCompletionDate = expectedCompletionDate;
        }

        if (events.Count > 0) Touch(actorId, now);
        return events;
    }

    /// <summary>Corrects the customer details stored on this order (e.g. a typo made at intake).</summary>
    public IReadOnlyList<RepairOrderEvent> CorrectCustomerInfo(CustomerSnapshot snapshot, Guid actorId, DateTimeOffset now)
    {
        EnsureOpen();
        var before = new Dictionary<string, string?>();
        var after = new Dictionary<string, string?>();
        Compare("name", CustomerName, Text.Required(snapshot.Name), before, after);
        Compare("phone", CustomerPhone, Text.Required(snapshot.Phone), before, after);
        if (after.Count == 0) return [];

        ApplyCustomerSnapshot(snapshot);
        Touch(actorId, now);
        return [Event(RepairOrderEventType.CustomerInfoCorrected, Json(before), Json(after), null, actorId, now)];
    }

    /// <summary>Corrects the device details stored on this order.</summary>
    public IReadOnlyList<RepairOrderEvent> CorrectDeviceInfo(DeviceSnapshot snapshot, Guid actorId, DateTimeOffset now)
    {
        EnsureOpen();
        var before = new Dictionary<string, string?>();
        var after = new Dictionary<string, string?>();
        Compare("type", DeviceType.ToString(), snapshot.Type.ToString(), before, after);
        Compare("brand", DeviceBrand, Text.Required(snapshot.Brand), before, after);
        Compare("model", DeviceModel, Text.Required(snapshot.Model), before, after);
        Compare("serialNumber", DeviceSerialNumber, Text.Optional(snapshot.SerialNumber), before, after);
        Compare("imei", DeviceImei, Device.NormalizeImei(snapshot.Imei), before, after);
        if (after.Count == 0) return [];

        ApplyDeviceSnapshot(snapshot);
        Touch(actorId, now);
        return [Event(RepairOrderEventType.DeviceInfoCorrected, Json(before), Json(after), null, actorId, now)];
    }

    /// <summary>Notes are allowed on closed orders too (e.g. "customer called after pickup").</summary>
    public RepairOrderEvent AddNote(string text, Guid actorId, DateTimeOffset now) =>
        Event(RepairOrderEventType.NoteAdded, null, Text.Required(text), null, actorId, now);

    private void EnsureOpen()
    {
        if (IsClosed)
            throw new DomainRuleException(RepairOrderErrors.OrderClosed, "The order is closed and can no longer be changed.");
    }

    private void Touch(Guid actorId, DateTimeOffset now)
    {
        UpdatedById = actorId;
        UpdatedAt = now;
    }

    private void ApplyCustomerSnapshot(CustomerSnapshot snapshot)
    {
        CustomerName = Text.Required(snapshot.Name);
        CustomerPhone = Text.Required(snapshot.Phone);
    }

    private void ApplyDeviceSnapshot(DeviceSnapshot snapshot)
    {
        DeviceType = snapshot.Type;
        DeviceBrand = Text.Required(snapshot.Brand);
        DeviceModel = Text.Required(snapshot.Model);
        DeviceSerialNumber = Text.Optional(snapshot.SerialNumber);
        DeviceImei = Device.NormalizeImei(snapshot.Imei);
    }

    private string? Change(
        string? current, string? next, RepairOrderEventType type, List<RepairOrderEvent> events, Guid actorId, DateTimeOffset now)
    {
        if (current != next)
            events.Add(Event(type, current, next, null, actorId, now));
        return next;
    }

    private static void Compare(
        string field, string? current, string? next, Dictionary<string, string?> before, Dictionary<string, string?> after)
    {
        if (current == next) return;
        before[field] = current;
        after[field] = next;
    }

    private RepairOrderEvent Event(
        RepairOrderEventType type, string? oldValue, string? newValue, string? comment, Guid actorId, DateTimeOffset now) =>
        RepairOrderEvent.Create(this, type, oldValue, newValue, comment, actorId, now);

    private static decimal? EnsurePrice(decimal? price) =>
        price is < 0 ? throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.") : price;

    private static string? Format(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? Format(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Relaxed escaping keeps Cyrillic readable in the database; the value is data, never rendered as HTML.
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Json(Dictionary<string, string?> values) => JsonSerializer.Serialize(values, JsonOptions);
}
