using System.Net;
using RepairHub.Application.Common;
using RepairHub.Application.RepairOrders;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.RepairOrders;

[Collection(PostgresCollection.Name)]
public sealed class RepairOrderSearchTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    private Task<PagedResult<RepairOrderListItemDto>?> ListAsync(SignedIn user, string query) =>
        Api.GetAsAsync(user, "/api/repair-orders?" + query).ContinueWith(t => t.Result.ReadAsync<PagedResult<RepairOrderListItemDto>>()).Unwrap();

    [Fact]
    public async Task Search_ByNumberCustomerPhoneDeviceAndSerial()
    {
        var shop = await Api.CreateWorkshopAsync();
        var phone = RepairTestData.UniquePhone();
        var customer = await Api.CreateCustomerAsync(shop.Owner, "Бауыржан Омаров", phone);
        var device = await Api.CreateDeviceAsync(shop.Owner, customer.Id, model: "ThinkPad T14", serial: "PF-4XZ9");
        var target = await Api.CreateOrderAsync(shop.Owner, customer.Id, device.Id);
        await Api.CreateFullOrderAsync(shop.Owner);
        await Api.CreateFullOrderAsync(shop.Owner);

        foreach (var term in new[]
                 {
                     target.Number, target.Number[^6..], "омаров", phone[^7..], "thinkpad", "pf-4xz", "Lenovo ThinkPad",
                 })
        {
            var page = await ListAsync(shop.Owner, "search=" + Uri.EscapeDataString(term));
            Assert.Contains(page!.Items, o => o.Id == target.Id);
            Assert.True(page.TotalCount == 1, $"'{term}' matched {page.TotalCount} orders");
        }

        // The customer's current phone also finds old orders after the number changed.
        var newPhone = RepairTestData.UniquePhone();
        await Api.PutAsAsync(shop.Owner, $"/api/customers/{customer.Id}", new { fullName = customer.FullName, phone = newPhone });
        Assert.Contains((await ListAsync(shop.Owner, "search=" + Uri.EscapeDataString(newPhone[^7..])))!.Items, o => o.Id == target.Id);
    }

    [Fact]
    public async Task Filters_ByStatusTechnicianAndOverdue_WithPagination()
    {
        var shop = await Api.CreateWorkshopAsync();
        var technician = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        var mine = await Api.CreateFullOrderAsync(shop.Owner, technician.UserId);
        await Api.MoveAsync(technician, mine, RepairOrderStatus.Diagnostics);
        for (var i = 0; i < 4; i++) await Api.CreateFullOrderAsync(shop.Owner);

        var myOrders = await ListAsync(technician, "technician=me");
        Assert.Equal(mine.Id, Assert.Single(myOrders!.Items).Id);

        var unassigned = await ListAsync(technician, "technician=unassigned&pageSize=3&page=2");
        Assert.Equal(4, unassigned!.TotalCount);
        Assert.Single(unassigned.Items);

        var diagnostics = await ListAsync(shop.Owner, "status=Diagnostics");
        Assert.Equal(mine.Id, Assert.Single(diagnostics!.Items).Id);
        var several = await ListAsync(shop.Owner, "status=Diagnostics&status=Received");
        Assert.Equal(5, several!.TotalCount);

        var summary = await (await Api.GetAsAsync(technician, "/api/repair-orders/summary")).ReadAsync<RepairOrderSummaryDto>();
        Assert.Equal(4, summary!.ByStatus.Single(s => s.Status == RepairOrderStatus.Received).Count);
        Assert.Equal(4, summary.Unassigned);

        Assert.Equal(HttpStatusCode.BadRequest, (await Api.GetAsAsync(shop.Owner, "/api/repair-orders?technician=somebody")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Api.GetAsAsync(shop.Owner, "/api/repair-orders?pageSize=500")).StatusCode);
    }

    [Fact]
    public async Task Sorting_ByNumber()
    {
        var shop = await Api.CreateWorkshopAsync();
        for (var i = 0; i < 3; i++) await Api.CreateFullOrderAsync(shop.Owner);

        var asc = await ListAsync(shop.Owner, "sort=Number&desc=false");
        Assert.Equal(asc!.Items.Select(o => o.Number).Order(), asc.Items.Select(o => o.Number));
        var desc = await ListAsync(shop.Owner, "sort=Number&desc=true");
        Assert.Equal(asc.Items.Select(o => o.Number).Reverse(), desc!.Items.Select(o => o.Number));
    }
}
