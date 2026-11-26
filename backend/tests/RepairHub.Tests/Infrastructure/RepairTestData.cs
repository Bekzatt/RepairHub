using System.Net;
using RepairHub.Application.Customers;
using RepairHub.Application.Devices;
using RepairHub.Application.RepairOrders;
using RepairHub.Domain.RepairOrders;

namespace RepairHub.Tests.Infrastructure;

/// <summary>API helpers for customers, devices and repair orders.</summary>
public static class RepairTestData
{
    private static int _phoneCounter = Random.Shared.Next(1_000_000, 8_000_000);

    /// <summary>A unique Kazakhstan-style mobile number, so duplicate checks never collide between tests.</summary>
    public static string UniquePhone() => $"+7 70{Interlocked.Increment(ref _phoneCounter):D8}";

    public static async Task<CustomerDto> CreateCustomerAsync(
        this IntegrationApiFactory api, SignedIn user, string? fullName = null, string? phone = null)
    {
        var response = await api.PostAsAsync(user, "/api/customers",
            new { fullName = fullName ?? "Асель Нурланова", phone = phone ?? UniquePhone() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<CustomerDto>())!;
    }

    public static async Task<DeviceDto> CreateDeviceAsync(
        this IntegrationApiFactory api, SignedIn user, Guid customerId, string model = "IdeaPad 5", string? serial = null)
    {
        var response = await api.PostAsAsync(user, "/api/devices",
            new { customerId, type = "Laptop", brand = "Lenovo", model, serialNumber = serial });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<DeviceDto>())!;
    }

    public static Task<HttpResponseMessage> PostOrderAsync(
        this IntegrationApiFactory api, SignedIn user, Guid customerId, Guid deviceId, Guid? technicianId = null,
        string problem = "Не включается") =>
        api.PostAsAsync(user, "/api/repair-orders", new
        {
            customerId,
            deviceId,
            problem,
            condition = "Царапины на крышке",
            accessories = "Зарядное устройство",
            assignedTechnicianId = technicianId,
            estimatedPrice = 15000m,
        });

    public static async Task<RepairOrderDetailsDto> CreateOrderAsync(
        this IntegrationApiFactory api, SignedIn user, Guid customerId, Guid deviceId, Guid? technicianId = null)
    {
        var response = await api.PostOrderAsync(user, customerId, deviceId, technicianId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<RepairOrderDetailsDto>())!;
    }

    /// <summary>A customer, a device and an order, all created by <paramref name="user"/>.</summary>
    public static async Task<RepairOrderDetailsDto> CreateFullOrderAsync(
        this IntegrationApiFactory api, SignedIn user, Guid? technicianId = null)
    {
        var customer = await api.CreateCustomerAsync(user);
        var device = await api.CreateDeviceAsync(user, customer.Id);
        return await api.CreateOrderAsync(user, customer.Id, device.Id, technicianId);
    }

    public static Task<HttpResponseMessage> ChangeStatusAsync(
        this IntegrationApiFactory api, SignedIn user, RepairOrderDetailsDto order, RepairOrderStatus status,
        RepairOutcome? outcome = null, string? comment = null) =>
        api.PostAsAsync(user, $"/api/repair-orders/{order.Id}/status",
            new { status, outcome, comment, version = order.Version });

    /// <summary>Changes status and returns the updated order; asserts success.</summary>
    public static async Task<RepairOrderDetailsDto> MoveAsync(
        this IntegrationApiFactory api, SignedIn user, RepairOrderDetailsDto order, RepairOrderStatus status,
        RepairOutcome? outcome = null, string? comment = null)
    {
        var response = await api.ChangeStatusAsync(user, order, status, outcome, comment);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<RepairOrderDetailsDto>())!;
    }

    public static async Task<RepairOrderDetailsDto> GetOrderAsync(this IntegrationApiFactory api, SignedIn user, Guid id) =>
        (await (await api.GetAsAsync(user, $"/api/repair-orders/{id}")).ReadAsync<RepairOrderDetailsDto>())!;

    public static async Task<IReadOnlyList<RepairOrderEventDto>> GetHistoryAsync(this IntegrationApiFactory api, SignedIn user, Guid id) =>
        (await (await api.GetAsAsync(user, $"/api/repair-orders/{id}/history")).ReadAsync<List<RepairOrderEventDto>>())!;
}
