namespace PgTestify.Internal;

internal static class SqlHelper
{
    internal static async Task ExecuteNonQueryAsync(
        this NpgsqlConnection connection,
        string sql,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<object?> ExecuteScalarAsync(
        this NpgsqlConnection connection,
        string sql,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync(ct);
    }

    internal static async Task<T?> ExecuteScalarAsync<T>(
        this NpgsqlConnection connection,
        string sql,
        CancellationToken ct = default)
    {
        var result = await connection.ExecuteScalarAsync(sql, ct);
        if (result is null or DBNull)
            return default;
        return (T)result;
    }

    internal static async Task<bool> DatabaseExistsAsync(
        this NpgsqlConnection connection,
        string dbName,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
        cmd.Parameters.AddWithValue("name", dbName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    internal static async Task<string?> GetDatabaseCommentAsync(
        this NpgsqlConnection connection,
        string dbName,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT pg_catalog.shobj_description(d.oid, 'pg_database') " +
            "FROM pg_database d WHERE d.datname = @name";
        cmd.Parameters.AddWithValue("name", dbName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string s ? s : null;
    }

    internal static async Task<bool> IsDatabaseTemplateAsync(
        this NpgsqlConnection connection,
        string dbName,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT datistemplate FROM pg_database WHERE datname = @name";
        cmd.Parameters.AddWithValue("name", dbName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    internal static string BuildConnectionString(string baseConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    internal static async Task<NpgsqlConnection> OpenConnectionAsync(
        string connectionString,
        CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
