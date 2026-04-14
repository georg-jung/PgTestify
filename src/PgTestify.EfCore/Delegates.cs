using Microsoft.EntityFrameworkCore;

namespace PgTestify.EfCore;

/// <summary>
/// Called once to build the template schema using EF Core.
/// Implement DDL via EF Core migrations or EnsureCreated.
/// </summary>
public delegate Task EfMigrateDelegate<TContext>(TContext context, CancellationToken ct)
    where TContext : DbContext;

/// <summary>
/// Called once after migration to seed data into the template using EF Core.
/// </summary>
public delegate Task EfSeedDelegate<TContext>(TContext context, CancellationToken ct)
    where TContext : DbContext;
