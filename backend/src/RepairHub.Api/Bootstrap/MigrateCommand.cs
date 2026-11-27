using Microsoft.EntityFrameworkCore;
using RepairHub.Infrastructure.Persistence;

namespace RepairHub.Api.Bootstrap;

/// <summary>
/// Applies pending EF Core migrations and exits: <code>dotnet RepairHub.Api.dll migrate</code>
/// Production runs it as ONE explicit step before the API starts (the "migrate" service in deploy/), so
/// API instances never race each other on the schema. Migrations are forward-only; rolling back the schema
/// means restoring the backup taken before the update.
/// </summary>
internal static class MigrateCommand
{
    public const string Name = "migrate";

    public static async Task<int> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            Console.WriteLine("Database schema is up to date.");
            return 0;
        }

        foreach (var migration in pending) Console.WriteLine($"Applying migration {migration}");
        await db.Database.MigrateAsync();
        Console.WriteLine($"Applied {pending.Count} migration(s).");
        return 0;
    }
}
