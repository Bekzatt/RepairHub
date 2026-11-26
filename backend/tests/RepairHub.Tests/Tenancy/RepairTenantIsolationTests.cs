using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepairHub.Application.Common;
using RepairHub.Application.Customers;
using RepairHub.Application.Devices;
using RepairHub.Application.RepairOrders;
using RepairHub.Domain.Customers;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;
using RepairHub.Infrastructure.Persistence;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Tenancy;

/// <summary>Workshop B must never see or change workshop A's customers, devices, orders or history.</summary>
[Collection(PostgresCollection.Name)]
public sealed class RepairTenantIsolationTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    private async Task<(TestWorkshop A, TestWorkshop B, RepairOrderDetailsDto OrderA)> TwoWorkshopsAsync()
    {
        var a = await Api.CreateWorkshopAsync();
        var b = await Api.CreateWorkshopAsync();
        var orderA = await Api.CreateFullOrderAsync(a.Owner);
        await Api.CreateFullOrderAsync(b.Owner);
        return (a, b, orderA);
    }

    [Fact]
    public async Task Lists_ContainOnlyOwnWorkshopRecords()
    {
        var (_, b, orderA) = await TwoWorkshopsAsync();

        var customers = await (await Api.GetAsAsync(b.Owner, "/api/customers?includeArchived=true&pageSize=100")).ReadAsync<PagedResult<CustomerDto>>();
        var devices = await (await Api.GetAsAsync(b.Owner, "/api/devices?includeArchived=true&pageSize=100")).ReadAsync<PagedResult<DeviceDto>>();
        var orders = await (await Api.GetAsAsync(b.Owner, "/api/repair-orders?pageSize=100")).ReadAsync<PagedResult<RepairOrderListItemDto>>();

        Assert.Equal(1, customers!.TotalCount);
        Assert.Equal(1, devices!.TotalCount);
        Assert.Equal(1, orders!.TotalCount);
        Assert.DoesNotContain(orders.Items, o => o.Id == orderA.Id);

        // Searching for A's order number or customer name finds nothing in B.
        var byNumber = await (await Api.GetAsAsync(b.Owner, $"/api/repair-orders?search={orderA.Number}")).ReadAsync<PagedResult<RepairOrderListItemDto>>();
        Assert.DoesNotContain(byNumber!.Items, o => o.Id == orderA.Id);
        var summary = await (await Api.GetAsAsync(b.Owner, "/api/repair-orders/summary")).ReadAsync<RepairOrderSummaryDto>();
        Assert.Equal(1, summary!.ByStatus.Sum(s => s.Count));
    }

    [Fact]
    public async Task Reads_OfOtherWorkshopRecords_Return404()
    {
        var (_, b, orderA) = await TwoWorkshopsAsync();

        foreach (var url in new[]
                 {
                     $"/api/customers/{orderA.CustomerId}", $"/api/customers/{orderA.CustomerId}/devices",
                     $"/api/customers/{orderA.CustomerId}/repair-orders", $"/api/devices/{orderA.DeviceId}",
                     $"/api/devices/{orderA.DeviceId}/repair-orders", $"/api/repair-orders/{orderA.Id}",
                     $"/api/repair-orders/{orderA.Id}/history",
                 })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Api.GetAsAsync(b.Owner, url)).StatusCode);
        }
    }

    [Fact]
    public async Task Writes_ToOtherWorkshopRecords_Return404_AndChangeNothing()
    {
        var (a, b, orderA) = await TwoWorkshopsAsync();
        var technicianB = await Api.AddUserAsync(b.Owner, UserRole.Technician);
        var order = $"/api/repair-orders/{orderA.Id}";
        var v = orderA.Version;

        var attempts = new[]
        {
            Api.PutAsAsync(b.Owner, $"/api/customers/{orderA.CustomerId}", new { fullName = "Hacked", phone = "+7 700 000 0000", allowDuplicatePhone = true }),
            Api.PostAsAsync(b.Owner, $"/api/customers/{orderA.CustomerId}/archive"),
            Api.PutAsAsync(b.Owner, $"/api/devices/{orderA.DeviceId}", new { type = "Other", brand = "x", model = "x" }),
            Api.PostAsAsync(b.Owner, $"/api/devices/{orderA.DeviceId}/archive"),
            Api.PutAsAsync(b.Owner, $"{order}/intake", new { problem = "Hacked", version = v }),
            Api.PutAsAsync(b.Owner, $"{order}/diagnostics", new { diagnostics = "Hacked", version = v }),
            Api.PutAsAsync(b.Owner, $"{order}/terms", new { estimatedPrice = 1m, version = v }),
            Api.PutAsAsync(b.Owner, $"{order}/technician", new { technicianId = technicianB.UserId, version = v }),
            Api.PostAsAsync(b.Owner, $"{order}/status", new { status = "Diagnostics", version = v }),
            Api.PostAsAsync(b.Owner, $"{order}/notes", new { text = "Hacked" }),
            Api.PostAsAsync(technicianB, $"{order}/take"),
        };
        foreach (var response in await Task.WhenAll(attempts))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var unchanged = await Api.GetOrderAsync(a.Owner, orderA.Id);
        Assert.Equal(orderA.Version, unchanged.Version);
        Assert.Null(unchanged.AssignedTechnician);
        Assert.DoesNotContain(await Api.GetHistoryAsync(a.Owner, orderA.Id), e => e.Actor.Id == b.Owner.UserId || e.Actor.Id == technicianB.UserId);
    }

    [Fact]
    public async Task CreatingAnOrder_WithOtherWorkshopReferences_IsRejected()
    {
        var (a, b, orderA) = await TwoWorkshopsAsync();
        var technicianA = await Api.AddUserAsync(a.Owner, UserRole.Technician);
        var customerB = await Api.CreateCustomerAsync(b.Owner);
        var deviceB = await Api.CreateDeviceAsync(b.Owner, customerB.Id);

        // A's customer + A's device, submitted by B.
        Assert.Equal(HttpStatusCode.BadRequest, (await Api.PostOrderAsync(b.Owner, orderA.CustomerId, orderA.DeviceId)).StatusCode);
        // B's customer with A's device.
        Assert.Equal(HttpStatusCode.BadRequest, (await Api.PostOrderAsync(b.Owner, customerB.Id, orderA.DeviceId)).StatusCode);
        // B's customer and device, A's technician.
        var foreignTechnician = await Api.PostOrderAsync(b.Owner, customerB.Id, deviceB.Id, technicianA.UserId);
        Assert.Equal(ErrorCodes.TechnicianNotAssignable, await foreignTechnician.ReadErrorCodeAsync());
        // A device for A's customer, created by B.
        Assert.Equal(HttpStatusCode.BadRequest, (await Api.PostAsAsync(b.Owner, "/api/devices",
            new { customerId = orderA.CustomerId, type = "Laptop", brand = "x", model = "x" })).StatusCode);
    }

    [Fact]
    public async Task Database_RejectsCrossWorkshopReferences_EvenIfApplicationChecksAreBypassed()
    {
        var (a, b, orderA) = await TwoWorkshopsAsync();
        var now = DateTimeOffset.UtcNow;

        await using var scope = Api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A device in workshop B that points at workshop A's customer.
        context.Devices.Add(Device.Create(b.WorkshopId, orderA.CustomerId, DeviceType.Laptop, "x", "x", null, null, null, now));
        var deviceError = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains("23503", deviceError.InnerException!.Message); // foreign_key_violation
        context.ChangeTracker.Clear();

        // An order in workshop B for A's customer and device, created by B's owner.
        var (order, events) = RepairOrder.Create(
            b.WorkshopId, 2099, 1, orderA.CustomerId, new CustomerSnapshot("x", "+7 700 000 0000"),
            orderA.DeviceId, new DeviceSnapshot(DeviceType.Laptop, "x", "x", null, null),
            "x", null, null, null, null, null, "KZT", b.Owner.UserId, now);
        context.RepairOrders.Add(order);
        context.RepairOrderEvents.AddRange(events);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        // A customer of B, but A's owner as the order's technician.
        var customerB = Customer.Create(b.WorkshopId, "x", "+7 700 000 0001", null, null, null, now);
        var deviceB = Device.Create(b.WorkshopId, customerB.Id, DeviceType.Laptop, "x", "x", null, null, null, now);
        context.Customers.Add(customerB);
        context.Devices.Add(deviceB);
        await context.SaveChangesAsync();
        var (withForeignTechnician, _) = RepairOrder.Create(
            b.WorkshopId, 2099, 2, customerB.Id, CustomerSnapshot.Of(customerB), deviceB.Id, DeviceSnapshot.Of(deviceB),
            "x", null, null, a.Owner.UserId, null, null, "KZT", b.Owner.UserId, now);
        context.RepairOrders.Add(withForeignTechnician);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Assignees_AreOnlyOwnWorkshopStaff()
    {
        var (a, b, _) = await TwoWorkshopsAsync();
        await Api.AddUserAsync(a.Owner, UserRole.Technician);

        var assignees = await (await Api.GetAsAsync(b.Owner, "/api/repair-orders/assignees")).ReadAsync<List<AssigneeDto>>();

        Assert.Equal([b.Owner.UserId], assignees!.Select(x => x.Id));
    }
}
