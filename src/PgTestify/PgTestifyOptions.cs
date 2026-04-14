namespace PgTestify;

/// <summary>
/// Configuration options for <see cref="PgFixture"/>.
/// </summary>
public sealed class PgTestifyOptions
{
    /// <summary>
    /// Connection string to a PostgreSQL maintenance database (e.g. "postgres").
    /// The role must have CREATE DATABASE privileges.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Override the template database name. If null, a name is derived from the
    /// calling assembly and DbContext type:  pgtestify_{assembly}_{context}
    /// Use distinct names when running multiple fixtures with different seeds.
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>
    /// Cache key for template invalidation. If the stored key matches this value,
    /// migration and seeding are skipped and the existing template is reused.
    /// If null, defaults to the last-write-time of the calling assembly (ISO8601).
    /// Accept any string: git commit hash, semver, content hash, etc.
    /// </summary>
    public string? CacheKey { get; set; }

    /// <summary>
    /// Number of databases to pre-create in the pool at startup (parallel).
    /// Default: 4.
    /// </summary>
    public int MinPoolSize { get; set; } = 4;

    /// <summary>
    /// Maximum number of clean databases to keep in the pool.
    /// Returned-clean databases beyond this limit are dropped instead of pooled.
    /// Default: 16.
    /// </summary>
    public int MaxPoolSize { get; set; } = 16;
}
