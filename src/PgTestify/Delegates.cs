namespace PgTestify;

/// <summary>
/// Called once to build the template database schema.
/// Use raw SQL, Dapper, or any direct Npgsql operations.
/// </summary>
public delegate Task MigrateDelegate(NpgsqlConnection connection, CancellationToken ct);

/// <summary>
/// Called once after migration to seed shared/reference data into the template.
/// </summary>
public delegate Task SeedDelegate(NpgsqlConnection connection, CancellationToken ct);
