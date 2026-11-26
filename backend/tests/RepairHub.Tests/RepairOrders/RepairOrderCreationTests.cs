using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepairHub.Application.Common;
using RepairHub.Application.Customers;
using RepairHub.Application.RepairOrders;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;
using RepairHub.Infrastructure.Persistence;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.RepairOrders;

[Collection(PostgresCollection.Name)]
public sealed class RepairOrderCreationTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    [Fact]
    public async Task Create_WithNewCustomerAndNewDevice_InOneRequest()
    {
        var shop = await Api.CreateWorkshopAsync();
        var receptionist = await Api.AddUserAsync(shop.Owner, UserRole.Receptionist);
        var technician = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        var expected = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

        var response = await Api.PostAsAsync(receptionist, "/api/repair-orders", new
        {
            newCustomer = new { fullName = "Дана Ахметова", phone = RepairTestData.UniquePhone() },
            newDevice = new { type = "Smartphone", brand = "Samsung", model = "Galaxy A54", imei = "356938035643809" },
            problem = "Разбит экран",
            condition = "Трещина по диагонали",
            accessories = "Чехол",
            assignedTechnicianId = technician.UserId,
            expectedCompletionDate = expected,
            estimatedPrice = 45000.50m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.ReadAsync<RepairOrderDetailsDto>())!;
        Assert.Matches(@"^RH-\d{4}-000001$", order.Number);
        Assert.Equal(RepairOrderStatus.Received, order.Status);
        Assert.Equal("Дана Ахметова", order.CustomerName);
        Assert.Equal("Galaxy A54", order.DeviceModel);
        Assert.Equal(technician.UserId, order.AssignedTechnician!.Id);
        Assert.Equal(expected, order.ExpectedCompletionDate);
        Assert.Equal(45000.50m, order.EstimatedPrice);
        Assert.Equal("KZT", order.Currency);
        Assert.Equal(receptionist.UserId, order.CreatedBy.Id);

        var history = await Api.GetHistoryAsync(receptionist, order.Id);
        Assert.Equal([RepairOrderEventType.Created, RepairOrderEventType.TechnicianAssigned], history.Select(e => e.Type));
        Assert.Equal("Test Technician", history[1].NewValue); // ids are resolved to names

        var customer = await (await Api.GetAsAsync(receptionist, $"/api/customers/{order.CustomerId}")).ReadAsync<CustomerDetailsDto>();
        Assert.Equal(1, customer!.DeviceCount);
        Assert.Equal(1, customer.OpenOrderCount);
    }

    [Fact]
    public async Task Create_ForExistingCustomerWithNewDevice()
    {
        var shop = await Api.CreateWorkshopAsync();
        var customer = await Api.CreateCustomerAsync(shop.Owner);

        var response = await Api.PostAsAsync(shop.Owner, "/api/repair-orders", new
        {
            customerId = customer.Id,
            newDevice = new { type = "Printer", brand = "Canon", model = "PIXMA G3411" },
            problem = "Не захватывает бумагу",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(customer.Id, (await response.ReadAsync<RepairOrderDetailsDto>())!.CustomerId);
    }

    [Fact]
    public async Task Create_ValidatesCustomerAndDeviceChoice()
    {
        var shop = await Api.CreateWorkshopAsync();
        var customer = await Api.CreateCustomerAsync(shop.Owner);
        var device = await Api.CreateDeviceAsync(shop.Owner, customer.Id);

        var both = await Api.PostAsAsync(shop.Owner, "/api/repair-orders", new
        {
            customerId = customer.Id,
            newCustomer = new { fullName = "X", phone = RepairTestData.UniquePhone() },
            deviceId = device.Id,
            problem = "x",
        });
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        var neither = await Api.PostAsAsync(shop.Owner, "/api/repair-orders", new { customerId = customer.Id, problem = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);

        var pastDate = await Api.PostAsAsync(shop.Owner, "/api/repair-orders", new
        {
            customerId = customer.Id, deviceId = device.Id, problem = "x",
            expectedCompletionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
        });
        Assert.Equal(ErrorCodes.ExpectedDateInPast, await pastDate.ReadErrorCodeAsync());

        var badPrice = await Api.PostAsAsync(shop.Owner, "/api/repair-orders", new
        {
            customerId = customer.Id, deviceId = device.Id, problem = "x", estimatedPrice = 10.555m,
        });
        Assert.Equal(HttpStatusCode.BadRequest, badPrice.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateNewCustomerPhone_CreatesNothing()
    {
        var shop = await Api.CreateWorkshopAsync();
        var phone = RepairTestData.UniquePhone();
        await Api.CreateCustomerAsync(shop.Owner, phone: phone);

        var response = await Api.PostAsAsync(shop.Owner, "/api/repair-orders", new
        {
            newCustomer = new { fullName = "Двойник", phone },
            newDevice = new { type = "Laptop", brand = "HP", model = "Pavilion" },
            problem = "x",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.CustomerPhoneExists, await response.ReadErrorCodeAsync());
        var customers = await (await Api.GetAsAsync(shop.Owner, "/api/customers")).ReadAsync<PagedResult<CustomerDto>>();
        Assert.Equal(1, customers!.TotalCount);

        // No order number was consumed: the next order is still number 1.
        var first = await Api.CreateFullOrderAsync(shop.Owner);
        Assert.EndsWith("-000001", first.Number);
    }

    [Fact]
    public async Task Numbers_AreSequentialPerWorkshop()
    {
        var a = await Api.CreateWorkshopAsync();
        var b = await Api.CreateWorkshopAsync();

        var a1 = await Api.CreateFullOrderAsync(a.Owner);
        var a2 = await Api.CreateFullOrderAsync(a.Owner);
        var b1 = await Api.CreateFullOrderAsync(b.Owner);

        Assert.EndsWith("-000001", a1.Number);
        Assert.EndsWith("-000002", a2.Number);
        Assert.EndsWith("-000001", b1.Number);
    }

    [Fact]
    public async Task ConcurrentCreation_ProducesUniqueGapFreeNumbers()
    {
        const int count = 20;
        var shop = await Api.CreateWorkshopAsync();
        var customer = await Api.CreateCustomerAsync(shop.Owner);
        var device = await Api.CreateDeviceAsync(shop.Owner, customer.Id);

        using var start = new SemaphoreSlim(0);
        var tasks = Enumerable.Range(0, count).Select(async _ =>
        {
            await start.WaitAsync();
            return await Api.PostOrderAsync(shop.Owner, customer.Id, device.Id);
        }).ToList();
        start.Release(count);
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = new List<int>();
        foreach (var response in responses)
            numbers.Add(int.Parse((await response.ReadAsync<RepairOrderDetailsDto>())!.Number[^6..]));
        Assert.Equal(Enumerable.Range(1, count), numbers.Order());
    }

    [Fact]
    public void NumberingYear_UsesTheWorkshopTimeZone()
    {
        var almaty = TimeZoneInfo.FindSystemTimeZoneById("Asia/Almaty");
        var newYearLocal = new DateTime(2027, 1, 1, 0, 30, 0, DateTimeKind.Unspecified);
        var justAfter = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(newYearLocal, almaty), TimeSpan.Zero);
        var justBefore = justAfter.AddHours(-1);

        // In UTC both instants are still 31 December 2026; in Almaty the first is already 2027.
        Assert.Equal(2026, justAfter.UtcDateTime.Year);
        Assert.Equal(2027, WorkshopTime.Today(justAfter, "Asia/Almaty").Year);
        Assert.Equal(2026, WorkshopTime.Today(justBefore, "Asia/Almaty").Year);
    }

    [Fact]
    public async Task OnlyFrontOffice_CanCreateOrders()
    {
        var shop = await Api.CreateWorkshopAsync();
        var technician = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        var customer = await Api.CreateCustomerAsync(shop.Owner);
        var device = await Api.CreateDeviceAsync(shop.Owner, customer.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PostOrderAsync(technician, customer.Id, device.Id)).StatusCode);
    }

    [Fact]
    public async Task Receptionist_OrInactiveUser_CannotBeAssigned()
    {
        var shop = await Api.CreateWorkshopAsync();
        var receptionist = await Api.AddUserAsync(shop.Owner, UserRole.Receptionist);
        var formerTechnician = await Api.AddUserAsync(shop.Owner, UserRole.Technician);
        await Api.PostAsAsync(shop.Owner, $"/api/users/{formerTechnician.UserId}/deactivate");
        var customer = await Api.CreateCustomerAsync(shop.Owner);
        var device = await Api.CreateDeviceAsync(shop.Owner, customer.Id);

        foreach (var user in new[] { receptionist, formerTechnician })
        {
            var response = await Api.PostOrderAsync(shop.Owner, customer.Id, device.Id, user.UserId);
            Assert.Equal(ErrorCodes.TechnicianNotAssignable, await response.ReadErrorCodeAsync());
        }

        var assignees = await (await Api.GetAsAsync(receptionist, "/api/repair-orders/assignees")).ReadAsync<List<AssigneeDto>>();
        Assert.Equal([shop.Owner.UserId], assignees!.Select(a => a.Id));
    }

    [Fact]
    public async Task History_CannotBeModifiedThroughTheDbContext()
    {
        var shop = await Api.CreateWorkshopAsync();
        var order = await Api.CreateFullOrderAsync(shop.Owner);

        await using var scope = Api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // No current user in this scope: the fail-closed tenant filter hides everything...
        Assert.Empty(await context.RepairOrderEvents.ToListAsync());
        // ...so read it explicitly through the bypass, as only allow-listed code may.
        var created = await context.RepairOrderEvents.IgnoreQueryFilters().FirstAsync(e => e.RepairOrderId == order.Id);

        context.RepairOrderEvents.Remove(created);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }
}
