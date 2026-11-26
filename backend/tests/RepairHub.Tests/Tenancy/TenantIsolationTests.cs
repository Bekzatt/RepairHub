using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepairHub.Application.Common;
using RepairHub.Application.Users;
using RepairHub.Application.Workshops;
using RepairHub.Domain.Users;
using RepairHub.Infrastructure.Persistence;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Tenancy;

/// <summary>Two workshops, each with an Owner and a Technician. Nothing of B may be visible to or changeable by A.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantIsolationTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    private async Task<(TestWorkshop A, TestWorkshop B, SignedIn TechnicianB)> TwoWorkshopsAsync()
    {
        var a = await Api.CreateWorkshopAsync();
        var b = await Api.CreateWorkshopAsync();
        await Api.AddUserAsync(a.Owner, UserRole.Technician);
        var technicianB = await Api.AddUserAsync(b.Owner, UserRole.Technician);
        return (a, b, technicianB);
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnWorkshopUsers()
    {
        var (a, b, technicianB) = await TwoWorkshopsAsync();

        var page = await (await Api.GetAsAsync(a.Owner, "/api/users?pageSize=100")).ReadAsync<PagedResult<UserDto>>();

        Assert.Equal(2, page!.TotalCount);
        Assert.Contains(page.Items, u => u.Id == a.Owner.UserId);
        Assert.DoesNotContain(page.Items, u => u.Id == b.Owner.UserId || u.Id == technicianB.UserId);
    }

    [Fact]
    public async Task Search_DoesNotFindOtherWorkshopUsersByEmail()
    {
        var (a, _, technicianB) = await TwoWorkshopsAsync();
        var emailB = technicianB.Auth.User.Email;

        var page = await (await Api.GetAsAsync(a.Owner, $"/api/users?search={emailB}")).ReadAsync<PagedResult<UserDto>>();

        Assert.Equal(0, page!.TotalCount);
    }

    [Fact]
    public async Task Read_OtherWorkshopUser_Returns404()
    {
        var (a, _, technicianB) = await TwoWorkshopsAsync();

        var response = await Api.GetAsAsync(a.Owner, $"/api/users/{technicianB.UserId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OtherWorkshopUser_Returns404AndChangesNothing()
    {
        var (a, b, technicianB) = await TwoWorkshopsAsync();

        var response = await Api.PutAsAsync(a.Owner, $"/api/users/{technicianB.UserId}",
            new { fullName = "Hijacked", role = "Owner" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = await (await Api.GetAsAsync(b.Owner, $"/api/users/{technicianB.UserId}")).ReadAsync<UserDto>();
        Assert.Equal("Test Technician", unchanged!.FullName);
        Assert.Equal(UserRole.Technician, unchanged.Role);
    }

    [Fact]
    public async Task Deactivate_OtherWorkshopUser_Returns404AndUserStaysActive()
    {
        var (a, _, technicianB) = await TwoWorkshopsAsync();

        var response = await Api.PostAsAsync(a.Owner, $"/api/users/{technicianB.UserId}/deactivate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.GetAsAsync(technicianB, "/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ActivateAndResetPassword_OtherWorkshopUser_Return404()
    {
        var (a, b, _) = await TwoWorkshopsAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await Api.PostAsAsync(a.Owner, $"/api/users/{b.Owner.UserId}/activate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Api.PostAsAsync(a.Owner, $"/api/users/{b.Owner.UserId}/reset-password",
                new { newPassword = "attacker-password-1" })).StatusCode);

        await Api.LoginAsync(b.Owner.Auth.User.Email, TestApi.Password); // password unchanged
    }

    [Fact]
    public async Task Workshop_ReturnsOnlyCallersOwnWorkshop()
    {
        var (a, b, technicianB) = await TwoWorkshopsAsync();

        var fromA = await (await Api.GetAsAsync(a.Owner, "/api/workshop")).ReadAsync<WorkshopDto>();
        var fromB = await (await Api.GetAsAsync(technicianB, "/api/workshop")).ReadAsync<WorkshopDto>();

        Assert.Equal(a.WorkshopId, fromA!.Id);
        Assert.Equal(b.WorkshopId, fromB!.Id);
    }

    [Fact]
    public async Task CreatedUser_AlwaysBelongsToCallersWorkshop_EvenIfBodyNamesAnother()
    {
        var (a, b, _) = await TwoWorkshopsAsync();

        var response = await Api.PostAsAsync(a.Owner, "/api/users", new
        {
            fullName = "Smuggled",
            email = TestApi.UniqueEmail(),
            role = "Technician",
            password = TestApi.Password,
            workshopId = b.WorkshopId, // not part of the contract; must be ignored
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<UserDto>();
        Assert.Equal(HttpStatusCode.NotFound, (await Api.GetAsAsync(b.Owner, $"/api/users/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.GetAsAsync(a.Owner, $"/api/users/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task QueryFilter_FailsClosed_WithoutCurrentUser()
    {
        await TwoWorkshopsAsync();

        // A scope outside any HTTP request has no current user (like a background job or CLI).
        await using var scope = Api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await context.Users.CountAsync());
        Assert.Equal(0, await context.Workshops.CountAsync());
        // The rows exist — they are hidden, not missing.
        Assert.True(await context.Users.IgnoreQueryFilters().CountAsync() >= 4);
    }

    [Fact]
    public async Task UserService_WithoutCurrentUser_IsForbidden()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();

        await Assert.ThrowsAsync<ForbiddenException>(() => users.ListAsync(new UserListQuery(), CancellationToken.None));
    }
}
