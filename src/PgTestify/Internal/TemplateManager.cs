using System.Reflection;
using PgTestify.Internal;

namespace PgTestify.Internal;

/// <summary>
/// Manages the lifecycle of a single PostgreSQL template database:
/// creation, timestamp-based cache invalidation, migration, seeding, and disposal.
///
/// Template databases are marked IS_TEMPLATE = true and ALLOW_CONNECTIONS = false.
/// The cache key is stored as a COMMENT ON DATABASE.
///
/// A SemaphoreSlim(1,1) ensures only one caller creates/rebuilds the template,
/// even when multiple test classes initialise concurrently.
/// </summary>
internal sealed class TemplateManager : IAsyncDisposable
{
    private readonly string _maintenanceConnectionString;
    private readonly string _templateName;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Task _initTask = Task.CompletedTask;
    private bool _initialized;

    internal TemplateManager(string maintenanceConnectionString, string templateName)
    {
        _maintenanceConnectionString = maintenanceConnectionString;
        _templateName = templateName;
    }

    internal string TemplateName => _templateName;

    /// <summary>
    /// Ensures the template database exists and matches the cache key.
    /// Safe to call concurrently — only one caller performs work; others wait.
    /// Returns true if the template was (re)created, false if it was reused from cache.
    /// If cacheKey is null, the template is always rebuilt (no caching).
    /// </summary>
    internal async Task<bool> EnsureTemplateAsync(
        string? cacheKey,
        MigrateDelegate? migrate,
        SeedDelegate? seed,
        CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_initialized) return false;

            var recreated = await CreateOrVerifyTemplateAsync(cacheKey, migrate, seed, ct);
            _initialized = true;
            return recreated;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<bool> CreateOrVerifyTemplateAsync(
        string? cacheKey,
        MigrateDelegate? migrate,
        SeedDelegate? seed,
        CancellationToken ct)
    {
        await using var admin = await SqlHelper.OpenConnectionAsync(
            _maintenanceConnectionString, ct);

        var exists = await admin.DatabaseExistsAsync(_templateName, ct);

        if (exists)
        {
            // If cacheKey is null, always rebuild (no caching)
            if (cacheKey is null)
            {
                await DropTemplateAsync(admin, ct);
            }
            else
            {
                var storedKey = await admin.GetDatabaseCommentAsync(_templateName, ct);
                if (storedKey == cacheKey)
                {
                    // Cache hit — template is valid, nothing to do
                    return false;
                }

                // Cache miss — drop and recreate
                await DropTemplateAsync(admin, ct);
            }
        }

        // Create fresh template
        await admin.ExecuteNonQueryAsync(
            $"""CREATE DATABASE "{_templateName}" """, ct);

        var builder = new NpgsqlConnectionStringBuilder(_maintenanceConnectionString)
        {
            Database = _templateName,
            Pooling = false
        };
        var templateConnStr = builder.ToString();

        await using (var templateConn = await SqlHelper.OpenConnectionAsync(templateConnStr, ct))
        {
            if (migrate is not null)
                await migrate(templateConn, ct);

            if (seed is not null)
                await seed(templateConn, ct);

            // Compact the template to minimise per-test CREATE DATABASE copy time
            await templateConn.ExecuteNonQueryAsync("VACUUM FULL", ct);
        }

        // Store cache key (if provided) and mark as template
        if (cacheKey is not null)
        {
            var escapedKey = cacheKey.Replace("'", "''");
            await admin.ExecuteNonQueryAsync(
                $"""COMMENT ON DATABASE "{_templateName}" IS '{escapedKey}' """, ct);
        }

        await admin.ExecuteNonQueryAsync(
            $"""ALTER DATABASE "{_templateName}" WITH ALLOW_CONNECTIONS false IS_TEMPLATE true """, ct);

        return true;
    }

    /// <summary>
    /// Drops all databases whose names are in <paramref name="databaseNames"/> using
    /// the admin connection. Each database is dropped with FORCE to terminate active connections.
    /// </summary>
    internal async Task DropDatabasesAsync(IEnumerable<string> databaseNames, CancellationToken ct = default)
    {
        await using var admin = await SqlHelper.OpenConnectionAsync(
            _maintenanceConnectionString, ct);

        foreach (var name in databaseNames)
        {
            try
            {
                await admin.ExecuteNonQueryAsync(
                    $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE) """, ct);
            }
            catch (Exception ex)
            {
                // Best-effort cleanup — log and continue
                System.Diagnostics.Trace.TraceWarning($"PgTestify: failed to drop database '{name}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Creates a new database from the template. Returns a connection string to it.
    /// </summary>
    internal async Task<string> CreateFromTemplateAsync(string dbName, CancellationToken ct = default)
    {
        await using var admin = await SqlHelper.OpenConnectionAsync(
            _maintenanceConnectionString, ct);

        // Drop if exists (handles aborted previous creation)
        await admin.ExecuteNonQueryAsync(
            $"""DROP DATABASE IF EXISTS "{dbName}" WITH (FORCE) """, ct);

        await admin.ExecuteNonQueryAsync(
            $"""CREATE DATABASE "{dbName}" TEMPLATE "{_templateName}" """, ct);

        return SqlHelper.BuildConnectionString(_maintenanceConnectionString, dbName);
    }

    private async Task DropTemplateAsync(NpgsqlConnection admin, CancellationToken ct)
    {
        // Must un-mark as template before dropping
        if (await admin.IsDatabaseTemplateAsync(_templateName, ct))
        {
            await admin.ExecuteNonQueryAsync(
                $"""ALTER DATABASE "{_templateName}" IS_TEMPLATE false """, ct);
        }

        await admin.ExecuteNonQueryAsync(
            $"""DROP DATABASE IF EXISTS "{_templateName}" WITH (FORCE) """, ct);
    }

    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
