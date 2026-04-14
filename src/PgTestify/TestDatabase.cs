using PgTestify.Internal;

namespace PgTestify;

/// <summary>
/// Per-test database handle. Dispose to return the database to the pool (if clean)
/// or discard it (if writes were detected via pg_stat_database counters).
///
/// <para>
/// All connections to the test database must be disposed before disposing this handle
/// so that PostgreSQL flushes pending statistics to shared memory.
/// </para>
///
/// <para>
/// Note: TRUNCATE operations are NOT reflected in pg_stat tuple counters.
/// If your test uses TRUNCATE, call <see cref="MarkDirty"/> before disposing.
/// </para>
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly DatabasePool _pool;
    private readonly StatsSnapshot _snapshot;
    private bool _dirty;
    private bool _disposed;

    // The primary, pre-opened connection for this test database
    private NpgsqlConnection? _connection;

    internal TestDatabase(
        DatabasePool pool,
        string connectionString,
        string databaseName,
        NpgsqlConnection connection,
        StatsSnapshot snapshot)
    {
        _pool = pool;
        ConnectionString = connectionString;
        DatabaseName = databaseName;
        _connection = connection;
        _snapshot = snapshot;
    }

    /// <summary>The connection string to this test database.</summary>
    public string ConnectionString { get; }

    /// <summary>The database name.</summary>
    public string DatabaseName { get; }

    /// <summary>
    /// The pre-opened connection to this test database.
    /// Do not dispose this directly — it will be managed by <see cref="DisposeAsync"/>.
    /// </summary>
    public NpgsqlConnection Connection =>
        _connection ?? throw new ObjectDisposedException(nameof(TestDatabase));

    /// <summary>
    /// Opens and returns an additional connection to this test database.
    /// The caller is responsible for disposing it.
    /// </summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await SqlHelper.OpenConnectionAsync(ConnectionString, ct);
    }

    /// <summary>
    /// Force-marks this database as dirty so it will be discarded (not returned to the pool).
    /// Use when the test performs TRUNCATE or other operations not reflected in pg_stat counters.
    /// </summary>
    public void MarkDirty()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _dirty = true;
    }

    /// <summary>
    /// Closes the primary connection (triggering stats flush), then checks pg_stat_database
    /// to decide whether to return the database to the pool or discard it.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Close the primary connection first. We call pg_stat_force_next_flush()
        // before closing to ensure the PostgreSQL backend flushes its local tuple counters 
        // to shared memory immediately, making them visible to our admin connection.
        if (_connection is not null)
        {
            try
            {
                await _connection.ExecuteNonQueryAsync("SELECT pg_stat_force_next_flush()");
            }
            finally
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }

        // Now return or discard asynchronously (non-blocking for the test)
        _pool.Return(DatabaseName, _snapshot, _dirty);
    }
}
