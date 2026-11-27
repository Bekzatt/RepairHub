using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RepairHub.Infrastructure.Persistence;

namespace RepairHub.Api.Bootstrap;

/// <summary>
/// Applies pending EF Core migrations and exits: <code>dotnet RepairHub.Api.dll migrate</code>
/// Production runs it as ONE explicit step before the API starts (the "migrate" service in deploy/), so
/// API instances never race each other on the schema. Migrations are forward-only; rolling back the schema
/// means restoring the backup taken before the update.
/// Exit code 0: the schema is up to date. Exit code 1: anything failed; the API must not be started (compose
/// starts it only after this command completed successfully).
/// </summary>
internal static class MigrateCommand
{
    public const string Name = "migrate";

    public const int Success = 0;
    public const int Failure = 1;

    public static async Task<int> RunAsync(
        IServiceProvider services, TextWriter? output = null, TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        NpgsqlConnectionStringBuilder target;
        try
        {
            target = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        }
        catch (ArgumentException ex)
        {
            // The parser's message may quote part of a value (e.g. of a password containing an unquoted ';'),
            // so only the exception type is printed.
            await error.WriteLineAsync($"Migration FAILED. The connection string ConnectionStrings__Default is invalid ({ex.GetType().Name}).");
            await error.WriteLineAsync("  What to do: check its keywords and quote values that contain ';' (e.g. Password='a;b').");
            return Failure;
        }

        var stage = "Checking the database for pending migrations";
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                await output.WriteLineAsync("Database schema is up to date.");
                return Success;
            }

            foreach (var migration in pending) await output.WriteLineAsync($"Applying migration {migration}");
            stage = $"Applying {pending.Count} migration(s)";
            await db.Database.MigrateAsync(cancellationToken);
            await output.WriteLineAsync($"Applied {pending.Count} migration(s).");
            return Success;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"Migration FAILED. {stage} on database '{target.Database}' at {target.Host}:{target.Port} failed.");
            await error.WriteLineAsync($"  Cause: {Redact(Describe(ex), target.Password)}");
            await error.WriteLineAsync($"  What to do: {Redact(Advice(ex, target), target.Password)}");
            await error.WriteLineAsync("  The application must not be started until this command succeeds.");
            return Failure;
        }
    }

    /// <summary>The exception chain as one line of messages (no stack trace, no connection string).</summary>
    private static string Describe(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current is PostgresException pg ? $"{pg.SqlState}: {pg.MessageText}" : current.Message;
            if (!string.IsNullOrWhiteSpace(message) && !messages.Contains(message)) messages.Add(message.Trim());
        }

        return $"{ex.GetType().Name}: {string.Join(" -> ", messages)}";
    }

    private static string Advice(Exception ex, NpgsqlConnectionStringBuilder target)
    {
        var postgres = Find<PostgresException>(ex);
        if (postgres is not null)
        {
            return postgres.SqlState switch
            {
                PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification =>
                    $"Check POSTGRES_USER / POSTGRES_PASSWORD (deploy/.env). User '{target.Username}' was rejected; " +
                    "the password is fixed when the database volume is first created.",
                PostgresErrorCodes.InvalidCatalogName =>
                    $"Database '{target.Database}' does not exist on this server. Check POSTGRES_DB (deploy/.env).",
                PostgresErrorCodes.CannotConnectNow or PostgresErrorCodes.AdminShutdown =>
                    "The database server is starting or shutting down. Wait until it is healthy and run migrate again.",
                _ => "A migration statement failed. Fix the cause shown above and run migrate again. If the schema " +
                     "is in an unexpected state, restore the backup taken before the update (README, Restore).",
            };
        }

        if (ex is OperationCanceledException)
            return "The command was cancelled. Run migrate again.";

        if (Find<SocketException>(ex) is not null || Find<TimeoutException>(ex) is not null)
            return $"Cannot reach the database server at {target.Host}:{target.Port}. Is the postgres container " +
                   "running and healthy? (docker compose ps postgres; docker compose logs postgres)";

        if (ex is NpgsqlException && ex.Message.Contains("authentication method", StringComparison.OrdinalIgnoreCase))
            return $"The server at {target.Host}:{target.Port} asked for an authentication method other than SCRAM, which " +
                   "the connection string forbids (Require Auth=ScramSHA256). It may not be the RepairHub database: " +
                   "check that the postgres container is running.";

        return "Fix the cause shown above and run migrate again.";
    }

    private static T? Find<T>(Exception ex) where T : Exception
    {
        for (var current = ex; current is not null; current = current.InnerException)
            if (current is T match) return match;
        return null;
    }

    // Defence in depth: driver messages do not contain the password, but never print it if one ever does.
    private static string Redact(string text, string? password) =>
        string.IsNullOrEmpty(password) ? text : text.Replace(password, "***", StringComparison.Ordinal);
}
