using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace PgTestify.IntegrationTests.Infrastructure;

/// <summary>
/// Manages a PostgreSQL Testcontainer shared across all tests in the session.
/// </summary>
public class PostgresContainer : IAsyncInitializer, IAsyncDisposable
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync() => await Container.StartAsync();

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
