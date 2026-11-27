using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RepairHub.Api.Bootstrap;
using RepairHub.Application.Abstractions;
using RepairHub.Domain.Users;
using RepairHub.Infrastructure;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Bootstrap;

/// <summary>The migrate command: exit code 0 only when the schema is up to date, 1 with a clear message otherwise.</summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrateCommandTests(PostgresFixture db)
{
    private const string SecretPassword = "Sup3r-Secret-Value-For-Tests";

    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? WorkshopId => null;
        public UserRole? Role => null;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(connectionString);
        services.AddScoped<ICurrentUser, NoCurrentUser>();
        await using var provider = services.BuildServiceProvider();

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await MigrateCommand.RunAsync(provider, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>A local port with nothing listening on it (bound, then released).</summary>
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task EmptyDatabase_AppliesMigrations_ThenReportsUpToDate()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync();

        var first = await RunAsync(connectionString);
        Assert.Equal(MigrateCommand.Success, first.ExitCode);
        Assert.Contains("Applied", first.Output);
        Assert.Empty(first.Error);

        var second = await RunAsync(connectionString);
        Assert.Equal(MigrateCommand.Success, second.ExitCode);
        Assert.Contains("Database schema is up to date.", second.Output);
    }

    [Fact]
    public async Task UnreachableServer_ExitsWithFailure_AndExplains_WithoutPassword()
    {
        var connectionString = $"Host=127.0.0.1;Port={FreePort()};Database=repairhub;Username=repairhub;" +
                               $"Password={SecretPassword};Timeout=5";

        var (exitCode, _, error) = await RunAsync(connectionString);

        Assert.Equal(MigrateCommand.Failure, exitCode);
        Assert.Contains("Migration FAILED", error);
        Assert.Contains("Cannot reach the database server", error);
        Assert.DoesNotContain(SecretPassword, error);
    }

    [Fact]
    public async Task WrongPassword_ExitsWithFailure_AndPointsToCredentials_WithoutPassword()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await db.CreateEmptyDatabaseAsync())
        {
            Password = SecretPassword,
        }.ConnectionString;

        var (exitCode, _, error) = await RunAsync(connectionString);

        Assert.Equal(MigrateCommand.Failure, exitCode);
        Assert.Contains("28P01", error);
        Assert.Contains("POSTGRES_PASSWORD", error);
        Assert.DoesNotContain(SecretPassword, error);
    }

    [Fact]
    public async Task InvalidConnectionString_ExitsWithFailure_WithoutQuotingValues()
    {
        // An unquoted ';' splits the password; the tail becomes an unknown "keyword".
        var (exitCode, _, error) = await RunAsync("Host=127.0.0.1;Password=abc;secret-tail-value");

        Assert.Equal(MigrateCommand.Failure, exitCode);
        Assert.Contains("ConnectionStrings__Default is invalid", error);
        Assert.DoesNotContain("secret-tail-value", error);
    }

    [Fact]
    public async Task FailingMigration_ExitsWithFailure()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync();
        // A table the first migration wants to create already exists: its CREATE TABLE fails.
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE TABLE workshops (id int)", connection);
            await command.ExecuteNonQueryAsync();
        }

        var (exitCode, _, error) = await RunAsync(connectionString);

        Assert.Equal(MigrateCommand.Failure, exitCode);
        Assert.Contains("Applying", error);
        Assert.Contains("42P07", error); // duplicate_table
    }
}
