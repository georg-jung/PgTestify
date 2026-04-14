using PgTestify.IntegrationTests.Model;
using PgTestify.IntegrationTests.Infrastructure;

namespace PgTestify.IntegrationTests.Infrastructure;

/// <summary>
/// PgTestify fixture that gets its connection string from the shared Testcontainer.
/// Uses EnsureCreated for schema (no EF migrations assembly — this is a test project).
/// Seeds one BlogPost for read tests.
/// </summary>
public class SeededFixture : PgTestifyFixture<TestDbContext>
{
    [ClassDataSource<PostgresContainer>(Shared = SharedType.PerTestSession)]
    public PostgresContainer Postgres { get; init; } = null!;

    protected override string? GetConnectionString()
        => Postgres.ConnectionString;

    protected override void Configure(PgTestifyOptions options)
    {
        options.TemplateName = "pgtestify_integration_seeded";
        options.MinPoolSize = 2;
        options.MaxPoolSize = 8;
    }

    protected override EfMigrateDelegate<TestDbContext>? GetMigrateDelegate()
        => async (ctx, ct) => await ctx.Database.EnsureCreatedAsync(ct);

    protected override async Task SeedAsync(TestDbContext context, CancellationToken ct)
    {
        context.BlogPosts.Add(new BlogPost
        {
            Title = "Seeded Post",
            Content = "This post exists in every test database clone.",
            Comments =
            [
                new Comment { Author = "Alice", Text = "First comment!" }
            ]
        });
        await context.SaveChangesAsync(ct);
    }
}

/// <summary>
/// PgTestify fixture with schema only (no seed data).
/// </summary>
public class EmptyFixture : PgTestifyFixture<TestDbContext>
{
    [ClassDataSource<PostgresContainer>(Shared = SharedType.PerTestSession)]
    public PostgresContainer Postgres { get; init; } = null!;

    protected override string? GetConnectionString()
        => Postgres.ConnectionString;

    protected override void Configure(PgTestifyOptions options)
    {
        options.TemplateName = "pgtestify_integration_empty";
        options.MinPoolSize = 2;
        options.MaxPoolSize = 4;
    }

    protected override EfMigrateDelegate<TestDbContext>? GetMigrateDelegate()
        => async (ctx, ct) => await ctx.Database.EnsureCreatedAsync(ct);
}
