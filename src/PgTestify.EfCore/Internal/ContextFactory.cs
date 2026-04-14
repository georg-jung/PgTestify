using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace PgTestify.EfCore.Internal;

/// <summary>
/// Builds DbContextOptions and creates DbContext instances for a given connection string.
/// </summary>
internal static class ContextFactory
{
    internal static DbContextOptions<TContext> BuildOptions<TContext>(
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseNpgsql(connectionString, npgsql =>
        {
            configureNpgsql?.Invoke(npgsql);
        });
        return builder.Options;
    }

    internal static TContext CreateContext<TContext>(
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null,
        QueryTrackingBehavior? tracking = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseNpgsql(connectionString, npgsql =>
        {
            configureNpgsql?.Invoke(npgsql);
        });

        if (tracking.HasValue)
            builder.UseQueryTrackingBehavior(tracking.Value);

        return CreateInstance<TContext>(builder.Options);
    }

    internal static TContext CreateContextWithConnection<TContext>(
        System.Data.Common.DbConnection connection,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null,
        QueryTrackingBehavior? tracking = null)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseNpgsql(connection, npgsql =>
        {
            configureNpgsql?.Invoke(npgsql);
        });

        if (tracking.HasValue)
            builder.UseQueryTrackingBehavior(tracking.Value);

        return CreateInstance<TContext>(builder.Options);
    }

    internal static TContext CreateInstance<TContext>(DbContextOptions<TContext> options)
        where TContext : DbContext
    {
        // Try ctor(DbContextOptions<TContext>) first (standard pattern)
        var typedCtor = typeof(TContext).GetConstructor([typeof(DbContextOptions<TContext>)]);
        if (typedCtor is not null)
            return (TContext)typedCtor.Invoke([options]);

        // Fallback: ctor(DbContextOptions)
        var baseCtor = typeof(TContext).GetConstructor([typeof(DbContextOptions)]);
        if (baseCtor is not null)
            return (TContext)baseCtor.Invoke([options]);

        throw new InvalidOperationException(
            $"Cannot construct {typeof(TContext).Name}. " +
            $"Ensure it has a constructor accepting DbContextOptions<{typeof(TContext).Name}> or DbContextOptions.");
    }
}
