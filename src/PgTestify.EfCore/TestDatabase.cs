using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using PgTestify.EfCore.Internal;

namespace PgTestify.EfCore;

/// <summary>
/// EF Core-aware per-test database handle.
/// Wraps <see cref="PgTestify.TestDatabase"/> and provides pre-built <see cref="DbContext"/> instances,
/// a no-tracking context for assertions, and implements <see cref="IDbContextFactory{TContext}"/>.
///
/// <para>
/// Dispose to return or discard the underlying database (same dirty-detection semantics
/// as the core <see cref="PgTestify.TestDatabase"/>).
/// </para>
/// </summary>
public sealed class TestDatabase<TContext> : IAsyncDisposable, IDbContextFactory<TContext>
    where TContext : DbContext
{
    private readonly Action<NpgsqlDbContextOptionsBuilder>? _configureNpgsql;
    private bool _disposed;

    internal TestDatabase(
        PgTestify.TestDatabase database,
        TContext context,
        TContext noTrackingContext,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql)
    {
        Database = database;
        Context = context;
        NoTrackingContext = noTrackingContext;
        _configureNpgsql = configureNpgsql;
    }

    /// <summary>The underlying core test database handle.</summary>
    public PgTestify.TestDatabase Database { get; }

    /// <summary>
    /// A tracking <typeparamref name="TContext"/> connected to this test database.
    /// Use for writes and change-tracking assertions.
    /// </summary>
    public TContext Context { get; }

    /// <summary>
    /// A no-tracking <typeparamref name="TContext"/> connected to this test database.
    /// Use for read assertions — slightly faster and avoids identity-map interference.
    /// </summary>
    public TContext NoTrackingContext { get; }

    /// <summary>
    /// Creates a new <typeparamref name="TContext"/> with its own connection each call.
    /// Tracking behavior: <see cref="QueryTrackingBehavior.TrackAll"/> (default).
    /// </summary>
    public TContext CreateDbContext() =>
        CreateDbContext(tracking: null);

    /// <summary>
    /// Creates a new <typeparamref name="TContext"/> with optional tracking behavior.
    /// </summary>
    public TContext CreateDbContext(QueryTrackingBehavior? tracking) =>
        ContextFactory.CreateContext<TContext>(Database.ConnectionString, _configureNpgsql, tracking);

    /// <summary>
    /// Force-marks this database as dirty (delegates to <see cref="PgTestify.TestDatabase.MarkDirty"/>).
    /// Use when the test performs TRUNCATE or other operations not reflected in pg_stat counters.
    /// </summary>
    public void MarkDirty() => Database.MarkDirty();

    /// <summary>
    /// Disposes EF Core contexts, then disposes the underlying <see cref="Database"/>,
    /// triggering dirty-detection and pool return/discard.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Context.DisposeAsync();
        await NoTrackingContext.DisposeAsync();
        await Database.DisposeAsync();
    }
}
