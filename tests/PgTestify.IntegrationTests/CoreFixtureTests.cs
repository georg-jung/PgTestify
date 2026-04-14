using PgTestify.IntegrationTests.Infrastructure;
using PgTestify.IntegrationTests.Model;

namespace PgTestify.IntegrationTests;

/// <summary>
/// Tests that exercise the core PgFixture directly (without the EF Core / TUnit wrappers),
/// and tests for specific pool/lifecycle behaviors.
/// </summary>
public class CoreFixtureTests
{
    [ClassDataSource<PostgresContainer>(Shared = SharedType.PerTestSession)]
    public PostgresContainer Postgres { get; init; } = null!;

    [Test]
    public async Task Core_PgFixture_Can_Rent_And_Return()
    {
        var options = new PgTestifyOptions
        {
            ConnectionString = Postgres.ConnectionString,
            TemplateName = "core_test_basic",
            MinPoolSize = 1,
            MaxPoolSize = 4,
        };

        await using var fixture = new PgFixture(options);
        await fixture.InitializeAsync(
            migrate: async (conn, ct) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE test_table (id SERIAL PRIMARY KEY, name TEXT NOT NULL)";
                await cmd.ExecuteNonQueryAsync(ct);
            },
            seed: async (conn, ct) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO test_table (name) VALUES ('seed')";
                await cmd.ExecuteNonQueryAsync(ct);
            });

        // Rent a database
        await using var db = await fixture.RentAsync();

        // Verify seed data
        await using var queryCmd = db.Connection.CreateCommand();
        queryCmd.CommandText = "SELECT COUNT(*) FROM test_table";
        var count = (long)(await queryCmd.ExecuteScalarAsync())!;
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task Core_PgFixture_Isolation_Between_Rents()
    {
        var options = new PgTestifyOptions
        {
            ConnectionString = Postgres.ConnectionString,
            TemplateName = "core_test_isolation",
            MinPoolSize = 1,
            MaxPoolSize = 4,
        };

        await using var fixture = new PgFixture(options);
        await fixture.InitializeAsync(
            migrate: async (conn, ct) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE iso_table (id SERIAL PRIMARY KEY, val TEXT)";
                await cmd.ExecuteNonQueryAsync(ct);
            });

        // First rent: insert data
        {
            await using var db = await fixture.RentAsync();
            await using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO iso_table (val) VALUES ('first')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Second rent: should not see the insert from the first rent
        // (dirty DB was discarded, fresh clone from template)
        //
        // Small delay to let the pool's async return/replenish complete
        await Task.Delay(500);

        {
            await using var db = await fixture.RentAsync();
            await using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM iso_table";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            await Assert.That(count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task MarkDirty_Prevents_Pool_Reuse()
    {
        var options = new PgTestifyOptions
        {
            ConnectionString = Postgres.ConnectionString,
            TemplateName = "core_test_markdirty",
            MinPoolSize = 1,
            MaxPoolSize = 2,
        };

        await using var fixture = new PgFixture(options);
        await fixture.InitializeAsync(
            migrate: async (conn, ct) =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE dirty_table (id SERIAL PRIMARY KEY)";
                await cmd.ExecuteNonQueryAsync(ct);
            });

        string firstDbName;
        {
            await using var db = await fixture.RentAsync();
            firstDbName = db.DatabaseName;
            // Don't write anything but mark dirty anyway
            db.MarkDirty();
        }

        await Task.Delay(500);

        {
            await using var db = await fixture.RentAsync();
            // Different database since the first one was forcibly discarded
            await Assert.That(db.DatabaseName).IsNotEqualTo(firstDbName);
        }
    }

    [Test]
    public async Task IDbContextFactory_Creates_Working_Contexts()
    {
        var options = new PgTestifyOptions
        {
            ConnectionString = Postgres.ConnectionString,
            TemplateName = "core_test_factory",
            MinPoolSize = 1,
            MaxPoolSize = 2,
        };

        await using var fixture = new PgFixture<TestDbContext>(options);
        await fixture.InitializeAsync(
            migrate: async (ctx, ct) => await ctx.Database.EnsureCreatedAsync(ct),
            seed: async (ctx, ct) =>
            {
                ctx.BlogPosts.Add(new BlogPost { Title = "Factory Test" });
                await ctx.SaveChangesAsync(ct);
            });

        await using var db = await fixture.RentAsync();

        // Use IDbContextFactory interface
        IDbContextFactory<TestDbContext> factory = db;
        await using var ctx = factory.CreateDbContext();

        var count = await ctx.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(1);
    }
}
