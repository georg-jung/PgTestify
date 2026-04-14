using PgTestify.IntegrationTests.Infrastructure;
using PgTestify.IntegrationTests.Model;

namespace PgTestify.IntegrationTests;

/// <summary>
/// Tests using the empty fixture (schema-only, no seed data).
/// Verifies the fixture system works with multiple template databases.
/// </summary>
[ClassDataSource<EmptyFixture>(Shared = SharedType.PerTestSession)]
public class EmptyDatabaseTests(EmptyFixture fixture) 
    : PgTest<EmptyFixture, TestDbContext>(fixture)
{
    [Test]
    public async Task Empty_Database_Has_No_Posts()
    {
        var count = await Context.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task Can_Insert_Into_Empty_Database()
    {
        Context.BlogPosts.Add(new BlogPost { Title = "First Post" });
        await Context.SaveChangesAsync();

        var count = await NoTrackingContext.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task Insert_In_Empty_DB_Does_Not_Leak()
    {
        // Even if Can_Insert ran first, this should still be empty
        var count = await Context.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(0);
    }
}
