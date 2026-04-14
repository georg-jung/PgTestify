namespace PgTestify.Internal;

/// <summary>
/// Captures tuple-level write statistics for a database from pg_stat_database.
/// Used to determine whether a test database was modified (dirty) after a test.
/// </summary>
internal sealed class StatsSnapshot
{
    private readonly long _inserted;
    private readonly long _updated;
    private readonly long _deleted;

    private StatsSnapshot(long inserted, long updated, long deleted)
    {
        _inserted = inserted;
        _updated = updated;
        _deleted = deleted;
    }

    /// <summary>
    /// Reads current tuple counters for the given database name.
    /// Uses stats_fetch_consistency = 'none' to bypass the reading backend's
    /// snapshot cache and read directly from shared memory (PG 15+ behaviour).
    /// </summary>
    internal static async Task<StatsSnapshot> CaptureAsync(
        NpgsqlConnection adminConnection,
        string databaseName,
        CancellationToken ct = default)
    {
        // Ensure we read the latest values from shared memory, not a cached snapshot
        await adminConnection.ExecuteNonQueryAsync(
            "SET stats_fetch_consistency = 'none'", ct);

        await using var cmd = adminConnection.CreateCommand();
        cmd.CommandText = """
            SELECT tup_inserted, tup_updated, tup_deleted
            FROM pg_stat_database
            WHERE datname = @name
            """;
        cmd.Parameters.AddWithValue("name", databaseName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Database not found in stats view - treat as zero (just created)
            return new StatsSnapshot(0, 0, 0);
        }

        var inserted = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
        var updated = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        var deleted = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);

        return new StatsSnapshot(inserted, updated, deleted);
    }

    /// <summary>
    /// Returns true if any tuple mutations occurred between this snapshot and <paramref name="current"/>.
    /// Note: TRUNCATE is NOT reflected in these counters. Use MarkDirty() for TRUNCATE-heavy tests.
    /// </summary>
    internal bool IsDirty(StatsSnapshot current) =>
        current._inserted > _inserted ||
        current._updated > _updated ||
        current._deleted > _deleted;
}
