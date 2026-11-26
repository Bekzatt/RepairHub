using RepairHub.Domain.Common;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;

namespace RepairHub.Tests.RepairOrders;

/// <summary>Unit tests of the repair order entity and the workflow table. No database.</summary>
public sealed class RepairOrderDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    private static RepairOrder NewOrder(Guid? technicianId = null) =>
        RepairOrder.Create(
            Guid.NewGuid(), 2026, 152, Guid.NewGuid(), new CustomerSnapshot("Асель", "+7 701 000 00 00"),
            Guid.NewGuid(), new DeviceSnapshot(DeviceType.Laptop, "Lenovo", "IdeaPad 5", "SN1", null),
            "Не включается", "Царапины на крышке", "Зарядка", technicianId, null, 15000m, "KZT", Actor, Now).Order;

    private static RepairOrder OrderIn(params RepairOrderStatus[] path)
    {
        var order = NewOrder();
        foreach (var status in path)
        {
            RepairOutcome? outcome = status == RepairOrderStatus.ReadyForPickup
                ? RepairOrderWorkflow.Find(order.Status, status)!.Outcomes[0]
                : null;
            order.ChangeStatus(status, outcome, status == RepairOrderStatus.Cancelled ? "reason" : null, Actor, Now);
        }
        return order;
    }

    [Fact]
    public void Create_FormatsNumberAndRecordsCreatedEvent()
    {
        var technician = Guid.NewGuid();
        var (order, events) = RepairOrder.Create(
            Guid.NewGuid(), 2026, 7, Guid.NewGuid(), new CustomerSnapshot(" Асель ", "8 701"), Guid.NewGuid(),
            new DeviceSnapshot(DeviceType.Smartphone, "Apple", "iPhone 13", null, "35-209900-176148-1"),
            "Разбит экран", null, null, technician, null, null, "KZT", Actor, Now);

        Assert.Equal("RH-2026-000007", order.Number);
        Assert.Equal(RepairOrderStatus.Received, order.Status);
        Assert.Equal("Асель", order.CustomerName);
        Assert.Equal("352099001761481", order.DeviceImei);
        Assert.Equal([RepairOrderEventType.Created, RepairOrderEventType.TechnicianAssigned], events.Select(e => e.Type));
        Assert.All(events, e => Assert.Equal(order.Id, e.RepairOrderId));
    }

    [Fact]
    public void ClosedStatuses_HaveNoOutgoingTransitions()
    {
        Assert.DoesNotContain(RepairOrderWorkflow.Transitions, t => RepairOrderWorkflow.IsClosed(t.From));
    }

    [Fact]
    public void InRepairToCancelled_IsNotATransition()
    {
        Assert.Null(RepairOrderWorkflow.Find(RepairOrderStatus.InRepair, RepairOrderStatus.Cancelled));
        var order = OrderIn(RepairOrderStatus.InRepair);

        var error = Assert.Throws<DomainRuleException>(() =>
            order.ChangeStatus(RepairOrderStatus.Cancelled, null, "mistake", Actor, Now));
        Assert.Equal(RepairOrderErrors.InvalidStatusTransition, error.Code);
    }

    [Fact]
    public void Cancel_IsImpossibleOnceRepairWorkHasStarted()
    {
        // InRepair -> WaitingForParts: WaitingForParts -> Cancelled exists, but work has already begun.
        var order = OrderIn(RepairOrderStatus.InRepair, RepairOrderStatus.WaitingForParts);

        var error = Assert.Throws<DomainRuleException>(() =>
            order.ChangeStatus(RepairOrderStatus.Cancelled, null, "customer changed mind", Actor, Now));
        Assert.Equal(RepairOrderErrors.InvalidStatusTransition, error.Code);
        Assert.DoesNotContain(RepairOrderWorkflow.AvailableTo(order, UserRole.Owner, Actor), t => t.To == RepairOrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_BeforeWork_RequiresReasonAndIsFinal()
    {
        var order = OrderIn(RepairOrderStatus.Diagnostics);

        var noReason = Assert.Throws<DomainRuleException>(() =>
            order.ChangeStatus(RepairOrderStatus.Cancelled, null, "  ", Actor, Now));
        Assert.Equal(RepairOrderErrors.ReasonRequired, noReason.Code);

        order.ChangeStatus(RepairOrderStatus.Cancelled, null, "Created by mistake", Actor, Now);
        Assert.Equal("Created by mistake", order.CancellationReason);
        Assert.Equal(Now, order.CancelledAt);
        Assert.Empty(RepairOrderWorkflow.AvailableTo(order, UserRole.Owner, Actor));
        Assert.Throws<DomainRuleException>(() => order.UpdateDiagnostics("x", Actor, Now));
        Assert.Equal(RepairOrderEventType.NoteAdded, order.AddNote("Customer called", Actor, Now).Type);
    }

    [Theory]
    [InlineData(RepairOrderStatus.Diagnostics, RepairOutcome.Repaired, false)]
    [InlineData(RepairOrderStatus.Diagnostics, RepairOutcome.NotRepaired, true)]
    [InlineData(RepairOrderStatus.AwaitingApproval, RepairOutcome.Declined, true)]
    [InlineData(RepairOrderStatus.InRepair, RepairOutcome.Repaired, true)]
    [InlineData(RepairOrderStatus.InRepair, RepairOutcome.Declined, true)]
    public void ReadyForPickup_AcceptsOnlyOutcomesThatFitTheCurrentStatus(
        RepairOrderStatus from, RepairOutcome outcome, bool allowed)
    {
        var order = from == RepairOrderStatus.AwaitingApproval
            ? OrderIn(RepairOrderStatus.Diagnostics, RepairOrderStatus.AwaitingApproval)
            : OrderIn(from);

        if (allowed)
        {
            var events = order.ChangeStatus(RepairOrderStatus.ReadyForPickup, outcome, null, Actor, Now);
            Assert.Equal(outcome, order.Outcome);
            Assert.Equal([RepairOrderEventType.StatusChanged, RepairOrderEventType.OutcomeRecorded], events.Select(e => e.Type));
        }
        else
        {
            var error = Assert.Throws<DomainRuleException>(() =>
                order.ChangeStatus(RepairOrderStatus.ReadyForPickup, outcome, null, Actor, Now));
            Assert.Equal(RepairOrderErrors.OutcomeNotAllowed, error.Code);
        }
    }

    [Fact]
    public void ReadyForPickup_WithoutOutcome_IsRejected()
    {
        var order = OrderIn(RepairOrderStatus.InRepair);
        var error = Assert.Throws<DomainRuleException>(() =>
            order.ChangeStatus(RepairOrderStatus.ReadyForPickup, null, null, Actor, Now));
        Assert.Equal(RepairOrderErrors.OutcomeRequired, error.Code);
    }

    [Fact]
    public void Rework_ClearsOutcomeAndCompletion_ButKeepsStart()
    {
        var order = OrderIn(RepairOrderStatus.InRepair, RepairOrderStatus.ReadyForPickup);
        var started = order.StartedAt;
        Assert.NotNull(order.CompletedAt);

        order.ChangeStatus(RepairOrderStatus.InRepair, null, "Problem found at pickup", Actor, Now.AddHours(1));

        Assert.Null(order.Outcome);
        Assert.Null(order.CompletedAt);
        Assert.Equal(started, order.StartedAt);
    }

    [Fact]
    public void Issue_SetsIssuedAtAndClosesTheOrder()
    {
        var order = OrderIn(RepairOrderStatus.InRepair, RepairOrderStatus.ReadyForPickup, RepairOrderStatus.Issued);

        Assert.True(order.IsClosed);
        Assert.Equal(Now, order.IssuedAt);
        var error = Assert.Throws<DomainRuleException>(() => order.UpdateTerms(1, 1, null, Actor, Now));
        Assert.Equal(RepairOrderErrors.OrderClosed, error.Code);
    }

    [Fact]
    public void Technician_GetsTransitionsOnlyWhenAssigned()
    {
        var technician = Guid.NewGuid();
        var unassigned = NewOrder();
        var assigned = NewOrder(technician);

        Assert.Empty(RepairOrderWorkflow.AvailableTo(unassigned, UserRole.Technician, technician));
        Assert.Equal(
            [RepairOrderStatus.Diagnostics, RepairOrderStatus.InRepair],
            RepairOrderWorkflow.AvailableTo(assigned, UserRole.Technician, technician).Select(t => t.To));
    }

    [Fact]
    public void Receptionist_HandlesFrontDeskTransitionsOnly()
    {
        var received = NewOrder();
        Assert.Equal([RepairOrderStatus.Cancelled],
            RepairOrderWorkflow.AvailableTo(received, UserRole.Receptionist, Actor).Select(t => t.To));

        var ready = OrderIn(RepairOrderStatus.InRepair, RepairOrderStatus.ReadyForPickup);
        Assert.Equal([RepairOrderStatus.InRepair, RepairOrderStatus.Issued],
            RepairOrderWorkflow.AvailableTo(ready, UserRole.Receptionist, Actor).Select(t => t.To).Order());
    }

    [Fact]
    public void Managers_MayPerformEveryDefinedTransition()
    {
        Assert.All(RepairOrderWorkflow.Transitions, t =>
        {
            Assert.True(RepairOrderWorkflow.IsAllowedFor(t, UserRole.Owner, isAssignedToOrder: false));
            Assert.True(RepairOrderWorkflow.IsAllowedFor(t, UserRole.Admin, isAssignedToOrder: false));
        });
    }

    [Fact]
    public void UpdateMethods_RecordOldAndNewValues_AndNothingWhenUnchanged()
    {
        var order = NewOrder();

        var terms = order.UpdateTerms(18000m, null, new DateOnly(2026, 12, 1), Actor, Now);
        Assert.Equal([RepairOrderEventType.EstimateChanged, RepairOrderEventType.ExpectedDateChanged], terms.Select(e => e.Type));
        Assert.Equal(("15000", "18000"), (terms[0].OldValue, terms[0].NewValue));
        Assert.Equal((null, "2026-12-01"), (terms[1].OldValue, terms[1].NewValue));

        Assert.Empty(order.UpdateTerms(18000m, null, new DateOnly(2026, 12, 1), Actor, Now));
        Assert.Empty(order.UpdateIntake("Не включается", "Царапины на крышке", "Зарядка", Actor, Now));
    }

    [Fact]
    public void CorrectDeviceInfo_RecordsOnlyChangedFields()
    {
        var order = NewOrder();

        var events = order.CorrectDeviceInfo(new DeviceSnapshot(DeviceType.Laptop, "Lenovo", "IdeaPad 5 Pro", "SN1", null), Actor, Now);

        var change = Assert.Single(events);
        Assert.Equal(RepairOrderEventType.DeviceInfoCorrected, change.Type);
        Assert.Equal("""{"model":"IdeaPad 5"}""", change.OldValue);
        Assert.Equal("""{"model":"IdeaPad 5 Pro"}""", change.NewValue);
        Assert.Equal("IdeaPad 5 Pro", order.DeviceModel);
    }

    [Theory]
    [InlineData("+7 701 123 45 67", "7011234567")]
    [InlineData("8 (701) 123-45-67", "7011234567")]
    [InlineData("7011234567", "7011234567")]
    [InlineData("+49 30 1234567", "9301234567")]
    [InlineData("12-34-56", "123456")]
    public void PhoneMatchKey_UsesTheLastTenDigits(string phone, string key)
    {
        Assert.Equal(key, PhoneNumber.MatchKey(phone));
    }

    [Theory]
    [InlineData("+7 701 123 45 67", true)]
    [InlineData("(727) 300-00-00", true)]
    [InlineData("1234", false)]
    [InlineData("call me", false)]
    [InlineData("+7 701 abc", false)]
    public void PhoneValidation_AcceptsAnyCountryFormat(string phone, bool valid)
    {
        Assert.Equal(valid, PhoneNumber.IsValid(phone));
    }
}
