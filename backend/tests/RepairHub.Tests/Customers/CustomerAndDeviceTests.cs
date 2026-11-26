using System.Net;
using System.Text.Json;
using RepairHub.Application.Common;
using RepairHub.Application.Customers;
using RepairHub.Application.Devices;
using RepairHub.Application.RepairOrders;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Customers;

[Collection(PostgresCollection.Name)]
public sealed class CustomerAndDeviceTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    [Fact]
    public async Task Receptionist_CreatesAndUpdatesCustomer()
    {
        var shop = await Api.CreateWorkshopAsync();
        var receptionist = await Api.AddUserAsync(shop.Owner, UserRole.Receptionist);
        var phone = RepairTestData.UniquePhone();

        var created = await Api.CreateCustomerAsync(receptionist, "  Ерлан Сапаров ", phone);
        Assert.Equal("Ерлан Сапаров", created.FullName);
        Assert.Equal(phone, created.Phone);

        var response = await Api.PutAsAsync(receptionist, $"/api/customers/{created.Id}", new
        {
            fullName = "Ерлан Сапаров", phone, additionalPhone = "8 (727) 300-00-00", email = "erlan@example.kz", notes = "VIP",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<CustomerDto>();
        Assert.Equal("8 (727) 300-00-00", updated!.AdditionalPhone);
        Assert.Equal("VIP", updated.Notes);
    }

    [Theory]
    [InlineData("+7 701 {0}")]
    [InlineData("8 (701) {0}")]
    [InlineData("701{0}")]
    public async Task DuplicatePhone_InAnyFormat_IsDetectedAndRequiresConfirmation(string format)
    {
        var shop = await Api.CreateWorkshopAsync();
        var suffix = Random.Shared.Next(1_000_000, 9_999_999).ToString();
        var existing = await Api.CreateCustomerAsync(shop.Owner, "Айгуль", $"+7 701 {suffix}");

        var duplicate = await Api.PostAsAsync(shop.Owner, "/api/customers",
            new { fullName = "Другой человек", phone = string.Format(format, suffix) });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var body = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.CustomerPhoneExists, body.RootElement.GetProperty("code").GetString());
        var match = Assert.Single(body.RootElement.GetProperty("matches").EnumerateArray());
        Assert.Equal(existing.Id, match.GetProperty("id").GetGuid());

        var confirmed = await Api.PostAsAsync(shop.Owner, "/api/customers",
            new { fullName = "Другой человек", phone = string.Format(format, suffix), allowDuplicatePhone = true });
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
    }

    [Fact]
    public async Task DuplicatePhone_IsScopedToTheWorkshop()
    {
        var a = await Api.CreateWorkshopAsync();
        var b = await Api.CreateWorkshopAsync();
        var phone = RepairTestData.UniquePhone();
        await Api.CreateCustomerAsync(a.Owner, phone: phone);

        Assert.Equal(HttpStatusCode.Created,
            (await Api.PostAsAsync(b.Owner, "/api/customers", new { fullName = "B", phone })).StatusCode);
    }

    [Fact]
    public async Task Search_FindsByCyrillicNameFragmentAndPhoneDigits()
    {
        var shop = await Api.CreateWorkshopAsync();
        var phone = RepairTestData.UniquePhone();
        var target = await Api.CreateCustomerAsync(shop.Owner, "Мадина Жумабаева", phone);
        await Api.CreateCustomerAsync(shop.Owner, "Олег Ким");

        var byName = await (await Api.GetAsAsync(shop.Owner, "/api/customers?search=жумаба")).ReadAsync<PagedResult<CustomerDto>>();
        Assert.Equal(target.Id, Assert.Single(byName!.Items).Id);

        var lastDigits = phone[^6..];
        var byPhone = await (await Api.GetAsAsync(shop.Owner, $"/api/customers?search={lastDigits}")).ReadAsync<PagedResult<CustomerDto>>();
        Assert.Equal(target.Id, Assert.Single(byPhone!.Items).Id);

        var wildcard = await (await Api.GetAsAsync(shop.Owner, "/api/customers?search=%25")).ReadAsync<PagedResult<CustomerDto>>();
        Assert.Equal(0, wildcard!.TotalCount);
    }

    [Fact]
    public async Task Customers_ArePaged()
    {
        var shop = await Api.CreateWorkshopAsync();
        for (var i = 0; i < 5; i++) await Api.CreateCustomerAsync(shop.Owner, $"Клиент {i}");

        var page = await (await Api.GetAsAsync(shop.Owner, "/api/customers?page=2&pageSize=2")).ReadAsync<PagedResult<CustomerDto>>();

        Assert.Equal(5, page!.TotalCount);
        Assert.Equal(["Клиент 2", "Клиент 3"], page.Items.Select(c => c.FullName));
    }

    [Fact]
    public async Task Archive_IsRejectedWhileOrdersAreOpen_AndArchivedCannotBeUsedForNewOrders()
    {
        var shop = await Api.CreateWorkshopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner);

        var customerArchive = await Api.PostAsAsync(shop.Owner, $"/api/customers/{order.CustomerId}/archive");
        Assert.Equal(HttpStatusCode.Conflict, customerArchive.StatusCode);
        Assert.Equal(ErrorCodes.HasActiveOrders, await customerArchive.ReadErrorCodeAsync());
        var deviceArchive = await Api.PostAsAsync(shop.Owner, $"/api/devices/{order.DeviceId}/archive");
        Assert.Equal(ErrorCodes.HasActiveOrders, await deviceArchive.ReadErrorCodeAsync());

        await Api.MoveAsync(shop.Owner, order, RepairOrderStatus.Cancelled, comment: "Created by mistake");

        Assert.Equal(HttpStatusCode.OK, (await Api.PostAsAsync(shop.Owner, $"/api/devices/{order.DeviceId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.PostAsAsync(shop.Owner, $"/api/customers/{order.CustomerId}/archive")).StatusCode);

        // Hidden from the default list, but the old order and its history stay accessible.
        var list = await (await Api.GetAsAsync(shop.Owner, "/api/customers")).ReadAsync<PagedResult<CustomerDto>>();
        Assert.DoesNotContain(list!.Items, c => c.Id == order.CustomerId);
        Assert.Equal(HttpStatusCode.OK, (await Api.GetAsAsync(shop.Owner, $"/api/repair-orders/{order.Id}")).StatusCode);
        Assert.NotEmpty(await Api.GetHistoryAsync(shop.Owner, order.Id));

        var newOrder = await Api.PostOrderAsync(shop.Owner, order.CustomerId, order.DeviceId);
        Assert.Equal(HttpStatusCode.BadRequest, newOrder.StatusCode);
        Assert.Equal(ErrorCodes.CustomerArchived, await newOrder.ReadErrorCodeAsync());

        var newDevice = await Api.PostAsAsync(shop.Owner, "/api/devices",
            new { customerId = order.CustomerId, type = "Printer", brand = "HP", model = "LaserJet" });
        Assert.Equal(ErrorCodes.CustomerArchived, await newDevice.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task ArchivedDevice_CannotBeUsedForNewOrders()
    {
        var shop = await Api.CreateWorkshopAsync();
        var customer = await Api.CreateCustomerAsync(shop.Owner);
        var device = await Api.CreateDeviceAsync(shop.Owner, customer.Id);
        await Api.PostAsAsync(shop.Owner, $"/api/devices/{device.Id}/archive");

        var response = await Api.PostOrderAsync(shop.Owner, customer.Id, device.Id);

        Assert.Equal(ErrorCodes.DeviceArchived, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Device_IsReusedAcrossRepairs_AndSerialNumbersNeedNotBeUnique()
    {
        var shop = await Api.CreateWorkshopAsync();
        var customer = await Api.CreateCustomerAsync(shop.Owner);
        var device = await Api.CreateDeviceAsync(shop.Owner, customer.Id, serial: "SAME-SERIAL");
        await Api.CreateDeviceAsync(shop.Owner, customer.Id, serial: "SAME-SERIAL");

        var first = await Api.CreateOrderAsync(shop.Owner, customer.Id, device.Id);
        var second = await Api.CreateOrderAsync(shop.Owner, customer.Id, device.Id);

        Assert.Equal(first.DeviceId, second.DeviceId);
        var details = await (await Api.GetAsAsync(shop.Owner, $"/api/devices/{device.Id}")).ReadAsync<DeviceDetailsDto>();
        Assert.Equal(2, details!.OrderCount);
        var history = await (await Api.GetAsAsync(shop.Owner, $"/api/devices/{device.Id}/repair-orders"))
            .ReadAsync<PagedResult<RepairOrderListItemDto>>();
        Assert.Equal(2, history!.TotalCount);

        var bySerial = await (await Api.GetAsAsync(shop.Owner, "/api/devices?search=same-serial")).ReadAsync<PagedResult<DeviceDto>>();
        Assert.Equal(2, bySerial!.TotalCount);
    }

    [Fact]
    public async Task EditingCustomerOrDevice_DoesNotChangeExistingOrderSnapshots()
    {
        var shop = await Api.CreateWorkshopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner);

        await Api.PutAsAsync(shop.Owner, $"/api/customers/{order.CustomerId}",
            new { fullName = "Новое Имя", phone = RepairTestData.UniquePhone(), allowDuplicatePhone = true });
        await Api.PutAsAsync(shop.Owner, $"/api/devices/{order.DeviceId}",
            new { type = "Laptop", brand = "Lenovo", model = "ThinkPad X1" });

        var reloaded = await Api.GetOrderAsync(shop.Owner, order.Id);
        Assert.Equal(order.CustomerName, reloaded.CustomerName);
        Assert.Equal(order.CustomerPhone, reloaded.CustomerPhone);
        Assert.Equal("IdeaPad 5", reloaded.DeviceModel);
        Assert.Equal(order.Version, reloaded.Version);
    }

    [Fact]
    public async Task Device_ForAnotherCustomer_IsRejectedOnOrderCreation()
    {
        var shop = await Api.CreateWorkshopAsync();
        var alice = await Api.CreateCustomerAsync(shop.Owner);
        var bob = await Api.CreateCustomerAsync(shop.Owner);
        var bobsDevice = await Api.CreateDeviceAsync(shop.Owner, bob.Id);

        var response = await Api.PostOrderAsync(shop.Owner, alice.Id, bobsDevice.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.DeviceCustomerMismatch, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Imei_IsValidatedAndNormalized()
    {
        var shop = await Api.CreateWorkshopAsync();
        var customer = await Api.CreateCustomerAsync(shop.Owner);

        var invalid = await Api.PostAsAsync(shop.Owner, "/api/devices",
            new { customerId = customer.Id, type = "Smartphone", brand = "Apple", model = "iPhone 13", imei = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var valid = await Api.PostAsAsync(shop.Owner, "/api/devices",
            new { customerId = customer.Id, type = "Smartphone", brand = "Apple", model = "iPhone 13", imei = "35-209900-176148-1" });
        Assert.Equal("352099001761481", (await valid.ReadAsync<DeviceDto>())!.Imei);
    }

    [Fact]
    public async Task Technician_HasNoAccessToCustomersAndDevices()
    {
        var shop = await Api.CreateWorkshopAsync();
        var technician = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        var customer = await Api.CreateCustomerAsync(shop.Owner);

        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsAsync(technician, "/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsAsync(technician, $"/api/customers/{customer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsAsync(technician, "/api/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Api.PostAsAsync(technician, "/api/customers", new { fullName = "X", phone = RepairTestData.UniquePhone() })).StatusCode);
    }

    [Fact]
    public async Task Receptionist_CannotArchive()
    {
        var shop = await Api.CreateWorkshopAsync();
        var receptionist = await Api.AddUserAsync(shop.Owner, UserRole.Receptionist);
        var customer = await Api.CreateCustomerAsync(receptionist);
        var device = await Api.CreateDeviceAsync(receptionist, customer.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PostAsAsync(receptionist, $"/api/customers/{customer.Id}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PostAsAsync(receptionist, $"/api/devices/{device.Id}/archive")).StatusCode);
    }
}
