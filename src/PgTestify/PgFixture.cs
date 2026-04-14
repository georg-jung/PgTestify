using System.Reflection;
using PgTestify.Internal;

namespace PgTestify;

/// <summary>
/// Manages the template database and pool of pre-cloned PostgreSQL databases for testing.
///
/// <para>
/// Typical usage: one <see cref="PgFixture"/> per test suite.
/// Call <see cref="InitializeAsync"/> once (e.g. in a TUnit <c>IAsyncInitializer</c>),
/// then <see cref="RentAsync"/> in each test.
/// </para>
///
/// <para>
/// Requires PostgreSQL 15+ (statistics in shared memory).
/// The maintenance connection string must use a role with CREATE DATABASE privileges.
/// </para>
/// </summary>
public sealed class PgFixture : IAsyncDisposable
{
    private readonly PgTestifyOptions _options;
    private TemplateManager? _template;
    private DatabasePool? _pool;
    private bool _initialized;
    private bool _disposed;

    public PgFixture(PgTestifyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>The maintenance connection string.</summary>
    public string ConnectionString => _options.ConnectionString;

    /// <summary>The template database name (available after <see cref="InitializeAsync"/>).</summary>
    public string TemplateName =>
        _template?.TemplateName ?? throw new InvalidOperationException(
            "Call InitializeAsync before accessing TemplateName.");

    /// <summary>
    /// Initializes the fixture: creates or verifies the template database,
    /// then pre-warms the pool with MinPoolSize parallel clones.
    /// </summary>
    /// <param name="migrate">
    /// Callback to build the template schema. Called once when the template is (re)created.
    /// If null, no migration is performed (the template starts empty).
    /// </param>
    /// <param name="seed">
    /// Callback to seed reference/shared data. Called once immediately after migration.
    /// If null, no seeding is performed.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(
        MigrateDelegate? migrate = null,
        SeedDelegate? seed = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            throw new InvalidOperationException("InitializeAsync has already been called.");

        var templateName = ResolveTemplateName();
        var cacheKey = ResolveCacheKey();

        _template = new TemplateManager(_options.ConnectionString, templateName);
        await _template.EnsureTemplateAsync(cacheKey, migrate, seed, ct);

        _pool = new DatabasePool(
            _template,
            _options.ConnectionString,
            _options.MinPoolSize,
            _options.MaxPoolSize);

        await _pool.WarmUpAsync(ct);

        _initialized = true;
    }

    /// <summary>
    /// Rents a database from the pool. Blocks if the pool is temporarily empty.
    /// Dispose the returned <see cref="TestDatabase"/> after the test to return or discard it.
    /// </summary>
    public async Task<TestDatabase> RentAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || _pool is null)
            throw new InvalidOperationException(
                "Call InitializeAsync before renting databases.");

        var (connStr, dbName, snapshot) = await _pool.RentAsync(ct);

        // Open the primary connection for the test
        var connection = await SqlHelper.OpenConnectionAsync(connStr, ct);

        return new TestDatabase(_pool, connStr, dbName, connection, snapshot);
    }

    /// <summary>
    /// Disposes the fixture: drops all pool databases, releases resources.
    /// The template database is kept for reuse on the next run (cache key check).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pool is not null)
            await _pool.DisposeAsync();

        if (_template is not null)
            await _template.DisposeAsync();
    }

    private string ResolveTemplateName()
    {
        if (!string.IsNullOrWhiteSpace(_options.TemplateName))
            return DbNamer.SanitizeTemplateName(_options.TemplateName);

        // Default: derive from the calling assembly and a generic label
        // Subclasses (e.g. PgFixture<TContext>) override via options
        var assembly = Assembly.GetCallingAssembly();
        return DbNamer.DefaultTemplateName(assembly, "fixture");
    }

    private string? ResolveCacheKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.CacheKey))
            return _options.CacheKey;

        // Default: assembly file last-write-time (ISO8601 round-trip format)
        var assembly = Assembly.GetCallingAssembly();
        var location = assembly.Location;
        if (!string.IsNullOrEmpty(location) && File.Exists(location))
        {
            return File.GetLastWriteTimeUtc(location).ToString("O");
        }

        // Fallback for single-file publish / no physical location:
        // Return null to disable caching rather than cache with a generic key
        // that would never rebuild (e.g., "unknown" or assembly version)
        return null;
    }
}
