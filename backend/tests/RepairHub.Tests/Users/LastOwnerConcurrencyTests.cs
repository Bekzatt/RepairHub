using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepairHub.Domain.Users;
using RepairHub.Infrastructure.Persistence;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Users;

/// <summary>
/// Two Owners act against each other at the same moment. Without the workshop row lock both requests
/// would see "another active Owner exists" and both succeed, leaving the workshop without an Owner.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LastOwnerConcurrencyTests(PostgresFixture db)
{
    private const int Rounds = 10;
    private IntegrationApiFactory Api => db.Api;

    [Fact]
    public async Task ConcurrentMutualDeactivation_LeavesExactlyOneActiveOwner()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var workshop = await Api.CreateWorkshopAsync();
            var ownerA = workshop.Owner;
            var ownerB = await Api.AddUserAsync(ownerA, UserRole.Owner);

            var responses = await RunConcurrentlyAsync(
                () => Api.PostAsAsync(ownerA, $"/api/users/{ownerB.UserId}/deactivate"),
                () => Api.PostAsAsync(ownerB, $"/api/users/{ownerA.UserId}/deactivate"));

            AssertExactlyOneSucceeded(responses);
            Assert.Equal(1, await CountActiveOwnersAsync(workshop.WorkshopId));
        }
    }

    [Fact]
    public async Task ConcurrentMutualDemotion_LeavesExactlyOneActiveOwner()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var workshop = await Api.CreateWorkshopAsync();
            var ownerA = workshop.Owner;
            var ownerB = await Api.AddUserAsync(ownerA, UserRole.Owner);

            var responses = await RunConcurrentlyAsync(
                () => Api.PutAsAsync(ownerA, $"/api/users/{ownerB.UserId}", new { fullName = "B", role = "Admin" }),
                () => Api.PutAsAsync(ownerB, $"/api/users/{ownerA.UserId}", new { fullName = "A", role = "Admin" }));

            AssertExactlyOneSucceeded(responses);
            Assert.Equal(1, await CountActiveOwnersAsync(workshop.WorkshopId));
        }
    }

    [Fact]
    public async Task ConcurrentDeactivationAndDemotion_LeavesAnActiveOwner()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var workshop = await Api.CreateWorkshopAsync();
            var ownerA = workshop.Owner;
            var ownerB = await Api.AddUserAsync(ownerA, UserRole.Owner);

            await RunConcurrentlyAsync(
                () => Api.PostAsAsync(ownerA, $"/api/users/{ownerB.UserId}/deactivate"),
                () => Api.PutAsAsync(ownerB, $"/api/users/{ownerA.UserId}", new { fullName = "A", role = "Technician" }));

            Assert.Equal(1, await CountActiveOwnersAsync(workshop.WorkshopId));
        }
    }

    private static async Task<HttpResponseMessage[]> RunConcurrentlyAsync(params Func<Task<HttpResponseMessage>>[] requests)
    {
        using var start = new ManualResetEventSlim();
        var tasks = requests.Select(r => Task.Run(async () => { start.Wait(); return await r(); })).ToArray();
        start.Set();
        return await Task.WhenAll(tasks);
    }

    private static void AssertExactlyOneSucceeded(HttpResponseMessage[] responses)
    {
        var codes = responses.Select(r => r.StatusCode).ToArray();
        Assert.Single(codes, c => c == HttpStatusCode.OK);
        // The loser is rejected: its actor lost Owner rights (403) or the invariant check fired (409).
        Assert.Single(codes, c => c is HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.Unauthorized);
    }

    private async Task<int> CountActiveOwnersAsync(Guid workshopId)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.IgnoreQueryFilters()
            .CountAsync(u => u.WorkshopId == workshopId && u.Role == UserRole.Owner && u.IsActive);
    }
}
