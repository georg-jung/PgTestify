using PgTestify.IntegrationTests.Infrastructure;
using PgTestify.IntegrationTests.Model;

namespace PgTestify.IntegrationTests;

/// <summary>
/// Tests that verify seeded data is visible and that read-only tests
/// get a recycled database from the pool (no writes detected).
/// </summary>
[ClassDataSource<SeededFixture>(Shared = SharedType.PerTestSession)]
public class ReadOnlyTests(SeededFixture fixture) 
    : PgTest<SeededFixture, TestDbContext>(fixture)
{
    [Test]
    public async Task Seeded_BlogPost_Exists()
    {
        var posts = await Context.BlogPosts.ToListAsync();

        await Assert.That(posts).Count().IsEqualTo(1);
        await Assert.That(posts[0].Title).IsEqualTo("Seeded Post");
    }

    [Test]
    public async Task Seeded_Comment_Is_Loaded_Via_Include()
    {
        var post = await Context.BlogPosts
            .Include(p => p.Comments)
            .SingleAsync();

        await Assert.That(post.Comments).Count().IsEqualTo(1);
        await Assert.That(post.Comments[0].Author).IsEqualTo("Alice");
    }

    [Test]
    public async Task NoTrackingContext_Returns_Same_Data()
    {
        var count = await NoTrackingContext.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    [Repeat(3)]
    public async Task Repeated_ReadOnly_Test_Sees_Consistent_Data()
    {
        // Each repetition should see exactly the 1 seeded post,
        // proving that recycled databases are clean.
        var count = await Context.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(1);
    }
}
