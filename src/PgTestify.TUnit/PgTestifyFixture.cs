using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using PgTestify.EfCore;
using TUnit.Core.Interfaces;

namespace PgTestify.TUnit;

/// <summary>
/// TUnit-compatible fixture that manages a <see cref="PgFixture{TContext}"/> lifetime.
///
/// <para>
/// Register with:
/// <code>
/// [ClassDataSource&lt;MyFixture&gt;(Shared = SharedType.PerTestSession)]
/// public MyFixture DbFixture { get; init; } = null!;
/// </code>
/// </para>
///
/// <para>
/// Override <see cref="GetConnectionString"/> or set <see cref="ConnectionString"/>
/// (e.g. from a Testcontainers fixture) before <c>InitializeAsync</c> is invoked.
/// </para>
/// </summary>
public abstract class PgTestifyFixture<TContext> : IAsyncInitializer, IAsyncDisposable
    where TContext : DbContext
{
    private PgFixture<TContext>? _fixture;

    /// <summary>
    /// Connection string to the maintenance database.
    /// Set this before initialization, e.g. from a Testcontainers fixture property.
    /// If null, <see cref="GetConnectionString"/> is called.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The initialized EF Core fixture. Throws if accessed before <see cref="IAsyncInitializer.InitializeAsync"/>.
    /// </summary>
    public PgFixture<TContext> Fixture =>
        _fixture ?? throw new InvalidOperationException(
            $"{GetType().Name} has not been initialized. " +
            "Ensure it is registered with [ClassDataSource(Shared = SharedType.PerTestSession)].");

    /// <summary>
    /// Override to return a connection string to the PostgreSQL maintenance database.
    /// Default: null (falls back to <see cref="ConnectionString"/> property).
    /// </summary>
    protected virtual string? GetConnectionString() => null;

    /// <summary>
    /// Override to configure <see cref="PgTestifyOptions"/> (pool sizes, template name, cache key, etc.)
    /// </summary>
    protected virtual void Configure(PgTestifyOptions options) { }

    /// <summary>
    /// Override to customize migration.
    /// Default: <c>context.Database.MigrateAsync(ct)</c>.
    /// Return null to use the default.
    /// </summary>
    protected virtual EfMigrateDelegate<TContext>? GetMigrateDelegate() => null;

    /// <summary>
    /// Override to seed data into the template after migration.
    /// Default: no-op.
    /// </summary>
    protected virtual Task SeedAsync(TContext context, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Override to configure Npgsql-specific DbContext options.
    /// </summary>
    protected virtual void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder builder) { }

    async Task IAsyncInitializer.InitializeAsync()
    {
        var connStr = GetConnectionString() ?? ConnectionString
            ?? throw new InvalidOperationException(
                $"{GetType().Name}: no connection string. " +
                "Override GetConnectionString() or set the ConnectionString property before initialization.");

        var options = new PgTestifyOptions
        {
            ConnectionString = connStr
        };
        Configure(options);

        _fixture = new PgFixture<TContext>(options, ConfigureNpgsqlInternal);

        await _fixture.InitializeAsync(
            migrate: GetMigrateDelegate(),
            seed: SeedInternalAsync,
            ct: default);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_fixture is not null)
            await _fixture.DisposeAsync();
    }

    private void ConfigureNpgsqlInternal(NpgsqlDbContextOptionsBuilder b) =>
        ConfigureNpgsql(b);

    private Task SeedInternalAsync(TContext context, CancellationToken ct) =>
        SeedAsync(context, ct);
}
