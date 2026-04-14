using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using PgTestify.EfCore.Internal;

namespace PgTestify.EfCore;

/// <summary>
/// EF Core-aware fixture. Wraps <see cref="PgFixture"/> and adds EF Core migration
/// and seeding hooks, plus typed <see cref="TestDatabase{TContext}"/> rental.
///
/// <para>
/// Typical usage:
/// <code>
/// public class MyFixture : PgTestifyFixture&lt;AppDbContext&gt;
/// {
///     protected override string? GetConnectionString() => "Host=...";
///     protected override Task SeedAsync(AppDbContext ctx, CancellationToken ct)
///         => ctx.Users.AddAsync(new User { … }).AsTask().ContinueWith(_ => ctx.SaveChangesAsync(ct)).Unwrap();
/// }
/// </code>
/// </para>
/// </summary>
public sealed class PgFixture<TContext> : IAsyncDisposable
    where TContext : DbContext
{
    private readonly PgTestifyOptions _options;
    private readonly Action<NpgsqlDbContextOptionsBuilder>? _configureNpgsql;
    private PgFixture? _core;
    private bool _initialized;
    private bool _disposed;

    public PgFixture(PgTestifyOptions options, Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = EnsureTemplateName(options);
        _configureNpgsql = configureNpgsql;
    }

    /// <summary>The underlying core fixture.</summary>
    public PgFixture Core =>
        _core ?? throw new InvalidOperationException("Call InitializeAsync first.");

    /// <summary>
    /// Initializes the fixture with EF Core migration and seed hooks.
    ///
    /// <para>
    /// If <paramref name="migrate"/> is null, <c>context.Database.MigrateAsync()</c> is called.
    /// If <paramref name="seed"/> is null, no seeding is performed.
    /// </para>
    /// </summary>
    public async Task InitializeAsync(
        EfMigrateDelegate<TContext>? migrate = null,
        EfSeedDelegate<TContext>? seed = null,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            throw new InvalidOperationException("InitializeAsync has already been called.");

        var effectiveNpgsqlConfig = configureNpgsql ?? _configureNpgsql;

        // Core-level delegates that operate on NpgsqlConnection
        MigrateDelegate? coreDelegate = null;
        SeedDelegate? seedDelegate = null;

        if (migrate is not null || seed is not null)
        {
            var effectiveMigrate = migrate ?? ((ctx, token) => ctx.Database.MigrateAsync(token));
            var effectiveSeed = seed;

            coreDelegate = async (connection, token) =>
            {
                await using var ctx = ContextFactory.CreateContextWithConnection<TContext>(
                    connection, effectiveNpgsqlConfig);
                await effectiveMigrate(ctx, token);
            };

            if (effectiveSeed is not null)
            {
                seedDelegate = async (connection, token) =>
                {
                    await using var ctx = ContextFactory.CreateContextWithConnection<TContext>(
                        connection, effectiveNpgsqlConfig);
                    await effectiveSeed(ctx, token);
                };
            }
        }
        else
        {
            // Default: just run migrations
            coreDelegate = async (connection, token) =>
            {
                await using var ctx = ContextFactory.CreateContextWithConnection<TContext>(
                    connection, effectiveNpgsqlConfig);
                await ctx.Database.MigrateAsync(token);
            };
        }

        _core = new PgFixture(_options);
        await _core.InitializeAsync(coreDelegate, seedDelegate, ct);

        _initialized = true;
    }

    /// <summary>
    /// Rents a database from the pool and returns an EF Core-aware handle with pre-built contexts.
    /// </summary>
    public async Task<TestDatabase<TContext>> RentAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || _core is null)
            throw new InvalidOperationException("Call InitializeAsync before renting databases.");

        var coreDb = await _core.RentAsync(ct);

        var context = ContextFactory.CreateContext<TContext>(
            coreDb.ConnectionString, _configureNpgsql);

        var noTrackingContext = ContextFactory.CreateContext<TContext>(
            coreDb.ConnectionString, _configureNpgsql,
            QueryTrackingBehavior.NoTracking);

        return new TestDatabase<TContext>(coreDb, context, noTrackingContext, _configureNpgsql);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_core is not null)
            await _core.DisposeAsync();
    }

    // Ensure the template name is derived from TContext if not user-supplied.
    private static PgTestifyOptions EnsureTemplateName(PgTestifyOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TemplateName))
            return options;

        // Derive from assembly + context type name
        var assembly = typeof(TContext).Assembly;
        var assemblyName = assembly.GetName().Name ?? "unknown";
        var contextName = typeof(TContext).Name;

        options.TemplateName = $"pgtestify_{Sanitize(assemblyName)[..Math.Min(16, Sanitize(assemblyName).Length)]}_{Sanitize(contextName)}";
        return options;

        static string Sanitize(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString().Trim('_');
        }
    }
}
