using System.Collections.Concurrent;
using System.Threading.Channels;
using PgTestify.Internal;

namespace PgTestify.Internal;

/// <summary>
/// Maintains a pool of pre-created PostgreSQL databases cloned from a template.
///
/// Lifecycle:
///   1. WarmUpAsync        — parallel-creates MinPoolSize databases into the channel
///   2. RentAsync          — takes from channel (or creates on-demand if empty)
///   3. ReturnAsync        — checks dirty, recycles or drops + replenishes
///   4. DropAllAsync       — called on fixture dispose, drops all owned databases
/// </summary>
internal sealed class DatabasePool : IAsyncDisposable
{
    private readonly TemplateManager _template;
    private readonly string _maintenanceConnectionString;
    private readonly int _minPoolSize;
    private readonly int _maxPoolSize;

    // Channel of available (ready-to-rent) database names
    private readonly Channel<string> _available =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    // All databases ever created this session (for cleanup on dispose)
    private readonly ConcurrentDictionary<string, byte> _allOwned = new();

    // Monotonically increasing counter for pool DB name generation
    private int _nextId = 0;

    internal DatabasePool(
        TemplateManager template,
        string maintenanceConnectionString,
        int minPoolSize,
        int maxPoolSize)
    {
        _template = template;
        _maintenanceConnectionString = maintenanceConnectionString;
        _minPoolSize = minPoolSize;
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Pre-creates MinPoolSize databases in parallel and populates the pool channel.
    /// </summary>
    internal async Task WarmUpAsync(CancellationToken ct = default)
    {
        var tasks = Enumerable.Range(0, _minPoolSize)
            .Select(_ => CreateAndEnqueueAsync(ct));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Rents a database from the pool. If the pool is empty, creates one on-demand (blocks).
    /// Returns the connection string and a pre-captured stats snapshot.
    /// </summary>
    internal async Task<(string connectionString, string dbName, StatsSnapshot snapshot)> RentAsync(
        CancellationToken ct = default)
    {
        string dbName;

        if (_available.Reader.TryRead(out var available))
        {
            dbName = available;
        }
        else
        {
            // Pool empty — create on-demand
            dbName = await CreatePoolDatabaseAsync(ct);
        }

        // Open admin connection to snapshot stats before handing out
        await using var admin = await SqlHelper.OpenConnectionAsync(
            _maintenanceConnectionString, ct);

        var snapshot = await StatsSnapshot.CaptureAsync(admin, dbName, ct);
        var connStr = SqlHelper.BuildConnectionString(_maintenanceConnectionString, dbName);

        return (connStr, dbName, snapshot);
    }

    /// <summary>
    /// Returns a database to the pool (if clean) or drops it and replenishes (if dirty).
    /// </summary>
    internal void Return(string dbName, StatsSnapshot snapshot, bool forceDirty)
    {
        // Fire-and-forget: don't block the test teardown path
        _ = Task.Run(async () =>
        {
            try
            {
                await ReturnInternalAsync(dbName, snapshot, forceDirty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"PgTestify: error returning database '{dbName}': {ex.Message}");
            }
        });
    }

    private async Task ReturnInternalAsync(string dbName, StatsSnapshot snapshot, bool forceDirty)
    {
        bool dirty = forceDirty;

        if (!dirty)
        {
            await using var admin = await SqlHelper.OpenConnectionAsync(
                _maintenanceConnectionString);

            var current = await StatsSnapshot.CaptureAsync(admin, dbName);
            dirty = snapshot.IsDirty(current);
        }

        if (!dirty && _available.Reader.Count < _maxPoolSize)
        {
            // Clean and pool has room — return it
            _available.Writer.TryWrite(dbName);
        }
        else
        {
            // Dirty or pool full — drop and optionally replenish
            _ = Task.Run(async () =>
            {
                try
                {
                    await _template.DropDatabasesAsync([dbName]);
                    _allOwned.TryRemove(dbName, out _);

                    // Replenish if pool is below minimum
                    if (_available.Reader.Count < _minPoolSize)
                    {
                        await CreateAndEnqueueAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        $"PgTestify: error dropping database '{dbName}': {ex.Message}");
                }
            });
        }
    }

    /// <summary>
    /// Drops all database owned by this pool. Called during fixture disposal.
    /// </summary>
    internal async Task DropAllAsync(CancellationToken ct = default)
    {
        // Drain the channel
        while (_available.Reader.TryRead(out _)) { }

        var names = _allOwned.Keys.ToList();
        if (names.Count > 0)
        {
            await _template.DropDatabasesAsync(names, ct);
        }

        _allOwned.Clear();
    }

    private async Task CreateAndEnqueueAsync(CancellationToken ct = default)
    {
        var dbName = await CreatePoolDatabaseAsync(ct);
        _available.Writer.TryWrite(dbName);
    }

    private async Task<string> CreatePoolDatabaseAsync(CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var dbName = DbNamer.PoolDatabaseName(_template.TemplateName, id);

        await _template.CreateFromTemplateAsync(dbName, ct);
        _allOwned.TryAdd(dbName, 0);

        return dbName;
    }

    public async ValueTask DisposeAsync()
    {
        await DropAllAsync();
        _available.Writer.Complete();
    }
}
