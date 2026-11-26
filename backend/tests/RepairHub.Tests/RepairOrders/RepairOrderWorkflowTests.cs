using System.Net;
using RepairHub.Application.Common;
using RepairHub.Application.RepairOrders;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.RepairOrders;

[Collection(PostgresCollection.Name)]
public sealed class RepairOrderWorkflowTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    private async Task<(TestWorkshop Shop, SignedIn Receptionist, SignedIn Technician)> ShopAsync()
    {
        var shop = await Api.CreateWorkshopAsync();
        var receptionist = await Api.AddUserAsync(shop.Owner, UserRole.Receptionist);
        var technician = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        return (shop, receptionist, technician);
    }

    [Fact]
    public async Task FullRepair_ByTheRolesThatDoEachStep()
    {
        var (_, receptionist, technician) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(receptionist, technician.UserId);

        var technicianView = await Api.GetOrderAsync(technician, order.Id);
        Assert.Equal([RepairOrderStatus.Diagnostics, RepairOrderStatus.InRepair], technicianView.AllowedTransitions.Select(t => t.To));
        Assert.True(technicianView.Can.EditDiagnostics);
        Assert.False(technicianView.Can.EditTerms);

        order = await Api.MoveAsync(technician, order, RepairOrderStatus.Diagnostics);
        var diag = await Api.PutAsAsync(technician, $"/api/repair-orders/{order.Id}/diagnostics",
            new { diagnostics = "Вышел из строя контроллер питания", version = order.Version });
        Assert.Equal(HttpStatusCode.OK, diag.StatusCode);
        order = (await diag.ReadAsync<RepairOrderDetailsDto>())!;

        order = await Api.MoveAsync(technician, order, RepairOrderStatus.AwaitingApproval, comment: "Нужна замена контроллера");
        order = await Api.MoveAsync(receptionist, order, RepairOrderStatus.WaitingForParts, comment: "Клиент согласен");
        order = await Api.MoveAsync(technician, order, RepairOrderStatus.InRepair);
        Assert.NotNull(order.StartedAt);
        order = await Api.MoveAsync(technician, order, RepairOrderStatus.ReadyForPickup, RepairOutcome.Repaired);
        Assert.Equal(RepairOutcome.Repaired, order.Outcome);

        var receptionView = await Api.GetOrderAsync(receptionist, order.Id);
        Assert.Contains(receptionView.AllowedTransitions, t => t.To == RepairOrderStatus.Issued);
        order = await Api.MoveAsync(receptionist, order, RepairOrderStatus.Issued);
        Assert.NotNull(order.IssuedAt);
        Assert.Empty(order.AllowedTransitions);

        var history = await Api.GetHistoryAsync(receptionist, order.Id);
        var statusChanges = history.Where(e => e.Type == RepairOrderEventType.StatusChanged).Select(e => (e.OldValue, e.NewValue)).ToList();
        Assert.Equal(
        [
            ("Received", "Diagnostics"), ("Diagnostics", "AwaitingApproval"), ("AwaitingApproval", "WaitingForParts"),
            ("WaitingForParts", "InRepair"), ("InRepair", "ReadyForPickup"), ("ReadyForPickup", "Issued"),
        ], statusChanges);
        Assert.Contains(history, e => e.Type == RepairOrderEventType.OutcomeRecorded && e.NewValue == "Repaired");
        Assert.Contains(history, e => e.Type == RepairOrderEventType.DiagnosticsUpdated && e.Actor.Id == technician.UserId);
        Assert.Contains(history, e => e.Comment == "Клиент согласен" && e.Actor.Id == receptionist.UserId);
    }

    [Fact]
    public async Task InvalidTransitions_AreRejected()
    {
        var (shop, _, _) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner);

        var skip = await Api.ChangeStatusAsync(shop.Owner, order, RepairOrderStatus.Issued);
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal(RepairOrderErrors.InvalidStatusTransition, await skip.ReadErrorCodeAsync());

        order = await Api.MoveAsync(shop.Owner, order, RepairOrderStatus.InRepair);
        var noOutcome = await Api.ChangeStatusAsync(shop.Owner, order, RepairOrderStatus.ReadyForPickup);
        Assert.Equal(RepairOrderErrors.OutcomeRequired, await noOutcome.ReadErrorCodeAsync());

        var cancelAfterWork = await Api.ChangeStatusAsync(shop.Owner, order, RepairOrderStatus.Cancelled, comment: "Передумал");
        Assert.Equal(RepairOrderErrors.InvalidStatusTransition, await cancelAfterWork.ReadErrorCodeAsync());

        // Work started and the customer declined: ReadyForPickup (Declined) -> Issued, never Cancelled.
        order = await Api.MoveAsync(shop.Owner, order, RepairOrderStatus.WaitingForParts);
        Assert.DoesNotContain(order.AllowedTransitions, t => t.To == RepairOrderStatus.Cancelled);
        order = await Api.MoveAsync(shop.Owner, order, RepairOrderStatus.ReadyForPickup, RepairOutcome.Declined);
        order = await Api.MoveAsync(shop.Owner, order, RepairOrderStatus.Issued);
        Assert.Equal(RepairOutcome.Declined, order.Outcome);

        var reopen = await Api.ChangeStatusAsync(shop.Owner, order, RepairOrderStatus.InRepair);
        Assert.Equal(RepairOrderErrors.InvalidStatusTransition, await reopen.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Cancel_BeforeWork_RequiresReason_AndClosesTheOrder()
    {
        var (_, receptionist, _) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(receptionist);

        var noReason = await Api.ChangeStatusAsync(receptionist, order, RepairOrderStatus.Cancelled);
        Assert.Equal(RepairOrderErrors.ReasonRequired, await noReason.ReadErrorCodeAsync());

        order = await Api.MoveAsync(receptionist, order, RepairOrderStatus.Cancelled, comment: "Оформлен по ошибке");
        Assert.Equal("Оформлен по ошибке", order.CancellationReason);

        var edit = await Api.PutAsAsync(receptionist, $"/api/repair-orders/{order.Id}/terms",
            new { estimatedPrice = 1, version = order.Version });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(RepairOrderErrors.OrderClosed, await edit.ReadErrorCodeAsync());

        // Notes are still allowed on closed orders.
        var note = await Api.PostAsAsync(receptionist, $"/api/repair-orders/{order.Id}/notes", new { text = "Клиент забрал устройство" });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
    }

    [Fact]
    public async Task Technician_CanOnlyChangeOrdersAssignedToThem()
    {
        var (shop, _, technician) = await ShopAsync();
        var colleague = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        var order = await Api.CreateFullOrderAsync(shop.Owner, colleague.UserId);

        // Can see it...
        var view = await Api.GetOrderAsync(technician, order.Id);
        Assert.Empty(view.AllowedTransitions);
        Assert.False(view.Can.EditDiagnostics);

        // ...but not change it.
        var move = await Api.ChangeStatusAsync(technician, order, RepairOrderStatus.Diagnostics);
        Assert.Equal(HttpStatusCode.Forbidden, move.StatusCode);
        Assert.Equal(ErrorCodes.NotAssigned, await move.ReadErrorCodeAsync());
        var diag = await Api.PutAsAsync(technician, $"/api/repair-orders/{order.Id}/diagnostics",
            new { diagnostics = "x", version = order.Version });
        Assert.Equal(HttpStatusCode.Forbidden, diag.StatusCode);
    }

    [Fact]
    public async Task RolePermissions_OnOrderFields()
    {
        var (shop, receptionist, technician) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner, technician.UserId);
        var url = $"/api/repair-orders/{order.Id}";

        // Technician: no prices or expected date, no intake edits, no assignment, no snapshot corrections.
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PutAsAsync(technician, $"{url}/terms",
            new { estimatedPrice = 1, version = order.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PutAsAsync(technician, $"{url}/intake",
            new { problem = "x", version = order.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PutAsAsync(technician, $"{url}/technician",
            new { technicianId = (Guid?)null, version = order.Version })).StatusCode);

        // Receptionist: no diagnostics, no floor transitions.
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PutAsAsync(receptionist, $"{url}/diagnostics",
            new { diagnostics = "x", version = order.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Api.ChangeStatusAsync(receptionist, order, RepairOrderStatus.Diagnostics)).StatusCode);

        // Receptionist: prices and expected date.
        var terms = await Api.PutAsAsync(receptionist, $"{url}/terms", new
        {
            estimatedPrice = 20000m, finalPrice = 19500m,
            expectedCompletionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), version = order.Version,
        });
        Assert.Equal(HttpStatusCode.OK, terms.StatusCode);
        var history = await Api.GetHistoryAsync(receptionist, order.Id);
        Assert.Contains(history, e => e.Type == RepairOrderEventType.EstimateChanged && e.OldValue == "15000" && e.NewValue == "20000");
        Assert.Contains(history, e => e.Type == RepairOrderEventType.FinalPriceChanged && e.OldValue == null);
        Assert.Contains(history, e => e.Type == RepairOrderEventType.ExpectedDateChanged);
    }

    [Fact]
    public async Task StaleVersion_IsRejected()
    {
        var (shop, receptionist, _) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner);

        var first = await Api.PutAsAsync(shop.Owner, $"/api/repair-orders/{order.Id}/intake",
            new { problem = "Не включается после залития", version = order.Version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await Api.PutAsAsync(receptionist, $"/api/repair-orders/{order.Id}/terms",
            new { estimatedPrice = 1m, version = order.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await stale.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task ConcurrentAssignment_WithTheSameVersion_OnlyOneWins()
    {
        var (shop, receptionist, technician) = await ShopAsync();
        var other = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        var order = await Api.CreateFullOrderAsync(shop.Owner);

        using var start = new SemaphoreSlim(0);
        var attempts = new[] { (shop.Owner, technician), (receptionist, other) }.Select(async pair =>
        {
            await start.WaitAsync();
            return await Api.PutAsAsync(pair.Item1, $"/api/repair-orders/{order.Id}/technician",
                new { technicianId = pair.Item2.UserId, version = order.Version });
        }).ToList();
        start.Release(2);
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var history = await Api.GetHistoryAsync(shop.Owner, order.Id);
        Assert.Single(history, e => e.Type == RepairOrderEventType.TechnicianAssigned);
    }

    [Fact]
    public async Task TakeOrder_AssignsTheCallerAndRecordsHistory()
    {
        var (shop, receptionist, technician) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner);

        Assert.True((await Api.GetOrderAsync(technician, order.Id)).Can.Take);
        Assert.False((await Api.GetOrderAsync(receptionist, order.Id)).Can.Take);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PostAsAsync(receptionist, $"/api/repair-orders/{order.Id}/take")).StatusCode);

        var taken = await Api.PostAsAsync(technician, $"/api/repair-orders/{order.Id}/take");
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        var details = (await taken.ReadAsync<RepairOrderDetailsDto>())!;
        Assert.Equal(technician.UserId, details.AssignedTechnician!.Id);
        Assert.Contains(details.AllowedTransitions, t => t.To == RepairOrderStatus.Diagnostics);

        var again = await Api.PostAsAsync(shop.Owner, $"/api/repair-orders/{order.Id}/take");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, await again.ReadErrorCodeAsync());

        var history = await Api.GetHistoryAsync(technician, order.Id);
        var assigned = Assert.Single(history, e => e.Type == RepairOrderEventType.TechnicianAssigned);
        Assert.Equal((null, "Test Technician"), (assigned.OldValue, assigned.NewValue));
        Assert.Equal(technician.UserId, assigned.Actor.Id);

        // The order's version changed, so a stale edit based on the pre-take version fails.
        var stale = await Api.PutAsAsync(shop.Owner, $"/api/repair-orders/{order.Id}/terms", new { estimatedPrice = 1m, version = order.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task ConcurrentTake_ExactlyOneTechnicianWins()
    {
        const int technicians = 8;
        var shop = await Api.CreateWorkshopAsync();
        var team = new List<SignedIn>();
        for (var i = 0; i < technicians; i++) team.Add(await Api.AddUserAsync(shop.Owner, UserRole.Technician));

        for (var round = 0; round < 3; round++)
        {
            var order = await Api.CreateFullOrderAsync(shop.Owner);
            using var start = new SemaphoreSlim(0);
            var attempts = team.Select(async t =>
            {
                await start.WaitAsync();
                return (t, await Api.PostAsAsync(t, $"/api/repair-orders/{order.Id}/take"));
            }).ToList();
            start.Release(technicians);
            var results = await Task.WhenAll(attempts);

            var winner = Assert.Single(results, r => r.Item2.StatusCode == HttpStatusCode.OK);
            Assert.All(results.Where(r => r != winner), r => Assert.Equal(HttpStatusCode.Conflict, r.Item2.StatusCode));

            var final = await Api.GetOrderAsync(shop.Owner, order.Id);
            Assert.Equal(winner.t.UserId, final.AssignedTechnician!.Id);
            Assert.Single(await Api.GetHistoryAsync(shop.Owner, order.Id), e => e.Type == RepairOrderEventType.TechnicianAssigned);
        }
    }

    [Fact]
    public async Task Notes_AreAppendOnlyTimelineEvents()
    {
        var (shop, _, technician) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner, technician.UserId);

        var note = await Api.PostAsAsync(technician, $"/api/repair-orders/{order.Id}/notes", new { text = "Обнаружена дополнительная неисправность" });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        var created = (await note.ReadAsync<RepairOrderEventDto>())!;
        Assert.Equal(RepairOrderEventType.NoteAdded, created.Type);
        Assert.Equal(technician.UserId, created.Actor.Id);

        // A note does not change the order, so it never causes concurrency conflicts for others.
        Assert.Equal(order.Version, (await Api.GetOrderAsync(shop.Owner, order.Id)).Version);

        var client = Api.CreateApiClient().As(shop.Owner);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.DeleteAsync($"/api/repair-orders/{order.Id}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/repair-orders/{order.Id}/history/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task SnapshotCorrection_IsRecordedWithOldAndNewValues()
    {
        var (_, receptionist, _) = await ShopAsync();
        var order = await Api.CreateFullOrderAsync(receptionist);

        var response = await Api.PutAsAsync(receptionist, $"/api/repair-orders/{order.Id}/snapshot", new
        {
            customerName = order.CustomerName,
            customerPhone = order.CustomerPhone,
            deviceType = "Laptop",
            deviceBrand = "Lenovo",
            deviceModel = "IdeaPad 5 Pro",
            deviceSerialNumber = "PF3ABC",
            version = order.Version,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("IdeaPad 5 Pro", (await response.ReadAsync<RepairOrderDetailsDto>())!.DeviceModel);
        var correction = Assert.Single(await Api.GetHistoryAsync(receptionist, order.Id), e => e.Type == RepairOrderEventType.DeviceInfoCorrected);
        Assert.Equal("""{"model":"IdeaPad 5","serialNumber":null}""", correction.OldValue);
        Assert.Equal("""{"model":"IdeaPad 5 Pro","serialNumber":"PF3ABC"}""", correction.NewValue);
    }
}
