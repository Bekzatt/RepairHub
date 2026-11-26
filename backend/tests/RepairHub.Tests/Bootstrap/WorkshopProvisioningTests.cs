using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepairHub.Application.Common;
using RepairHub.Application.Workshops;
using RepairHub.Domain.Users;
using RepairHub.Infrastructure.Persistence;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Bootstrap;

/// <summary>The create-workshop bootstrap path, each test on its own empty database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class WorkshopProvisioningTests(PostgresFixture db)
{
    private static CreateWorkshopRequest Request(string name = "Main workshop", string? email = null, bool allowAdditional = false) =>
        new()
        {
            WorkshopName = name,
            OwnerFullName = "First Owner",
            OwnerEmail = email ?? TestApi.UniqueEmail("owner"),
            OwnerPassword = TestApi.Password,
            AllowAdditionalWorkshop = allowAdditional,
        };

    private static async Task<ProvisionedWorkshop> ProvisionAsync(IntegrationApiFactory api, CreateWorkshopRequest request)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WorkshopProvisioningService>()
            .CreateAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task FirstRun_CreatesWorkshopWithDefaultsAndActiveOwner_WhoCanSignIn()
    {
        var api = await db.CreateDatabaseAsync();
        var request = Request();

        var result = await ProvisionAsync(api, request);

        var me = await api.LoginAsync(request.OwnerEmail, request.OwnerPassword);
        Assert.Equal(result.OwnerId, me.UserId);
        Assert.Equal(UserRole.Owner, me.Auth.User.Role);
        Assert.Equal("KZT", me.Auth.User.Workshop.Currency);
        Assert.Equal("Asia/Almaty", me.Auth.User.Workshop.TimeZone);
    }

    [Fact]
    public async Task SecondRun_IsRefusedByDefault_AndCreatesNothing()
    {
        var api = await db.CreateDatabaseAsync();
        await ProvisionAsync(api, Request());

        var error = await Assert.ThrowsAsync<ConflictException>(() => ProvisionAsync(api, Request("Another workshop")));

        Assert.Equal(ErrorCodes.WorkshopsExist, error.Code);
        Assert.Equal((1, 1), await CountAsync(api));
    }

    [Fact]
    public async Task AdditionalWorkshop_RequiresExplicitFlag_AndUniqueName()
    {
        var api = await db.CreateDatabaseAsync();
        await ProvisionAsync(api, Request("Main workshop"));

        var duplicate = await Assert.ThrowsAsync<ConflictException>(
            () => ProvisionAsync(api, Request("  MAIN WORKSHOP ", allowAdditional: true)));
        Assert.Equal(ErrorCodes.WorkshopNameTaken, duplicate.Code);

        await ProvisionAsync(api, Request("Second branch", allowAdditional: true));
        Assert.Equal((2, 2), await CountAsync(api));
    }

    [Fact]
    public async Task ExistingOwnerEmail_IsRefused()
    {
        var api = await db.CreateDatabaseAsync();
        var email = TestApi.UniqueEmail("owner");
        await ProvisionAsync(api, Request("One", email));

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => ProvisionAsync(api, Request("Two", email.ToUpperInvariant(), allowAdditional: true)));

        Assert.Equal(ErrorCodes.EmailTaken, error.Code);
        Assert.Equal((1, 1), await CountAsync(api));
    }

    [Fact]
    public async Task InvalidInput_IsRejectedBeforeAnythingIsWritten()
    {
        var api = await db.CreateDatabaseAsync();

        var weak = await Assert.ThrowsAsync<ValidationFailedException>(() => ProvisionAsync(api, new CreateWorkshopRequest
        {
            WorkshopName = "W", OwnerFullName = "O", OwnerEmail = TestApi.UniqueEmail(), OwnerPassword = "short",
        }));
        var badTimeZone = await Assert.ThrowsAsync<ValidationFailedException>(() => ProvisionAsync(api, new CreateWorkshopRequest
        {
            WorkshopName = "W", OwnerFullName = "O", OwnerEmail = TestApi.UniqueEmail(), OwnerPassword = TestApi.Password,
            TimeZone = "Nowhere/City",
        }));

        Assert.Contains("ownerPassword", weak.Errors.Keys);
        Assert.Contains("timeZone", badTimeZone.Errors.Keys);
        Assert.Equal((0, 0), await CountAsync(api));
    }

    [Fact]
    public async Task ConcurrentFirstRuns_CreateExactlyOneWorkshop()
    {
        var api = await db.CreateDatabaseAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Task.Run(async () =>
        {
            try { await ProvisionAsync(api, Request($"Workshop {i}")); return true; }
            catch (ConflictException) { return false; }
        })));

        Assert.Single(results, created => created);
        Assert.Equal((1, 1), await CountAsync(api));
    }

    private static async Task<(int Workshops, int Users)> CountAsync(IntegrationApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await context.Workshops.IgnoreQueryFilters().CountAsync(),
                await context.Users.IgnoreQueryFilters().CountAsync());
    }
}
