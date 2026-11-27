using Npgsql;
using Testcontainers.PostgreSql;

namespace RepairHub.Tests.Infrastructure;

/// <summary>
/// One PostgreSQL container for all database tests (requires Docker).
/// Tests isolate themselves by creating their own workshops with unique names and emails;
/// tests that need an empty database call <see cref="CreateDatabaseAsync"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly List<IntegrationApiFactory> _factories = [];

    /// <summary>API host on the shared database (migrations applied on startup).</summary>
    public IntegrationApiFactory Api { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Api = CreateFactory(_container.GetConnectionString());
        _ = Api.Server; // starts the host, which applies migrations
    }

    /// <summary>Creates an empty, migrated database and an API host on top of it.</summary>
    public async Task<IntegrationApiFactory> CreateDatabaseAsync()
    {
        var factory = CreateFactory(await CreateEmptyDatabaseAsync());
        _ = factory.Server;
        return factory;
    }

    /// <summary>Creates an empty database WITHOUT migrations; returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "db_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    private IntegrationApiFactory CreateFactory(string connectionString)
    {
        var factory = new IntegrationApiFactory(connectionString);
        _factories.Add(factory);
        return factory;
    }

    public async Task DisposeAsync()
    {
        foreach (var factory in _factories) await factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
