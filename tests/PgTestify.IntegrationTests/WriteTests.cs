using PgTestify.IntegrationTests.Infrastructure;
using PgTestify.IntegrationTests.Model;

namespace PgTestify.IntegrationTests;

/// <summary>
/// Tests that write to the database. The pool should detect these as dirty
/// and discard the database after each test, so parallel/subsequent tests always
/// see only the seeded data.
/// </summary>
[ClassDataSource<SeededFixture>(Shared = SharedType.PerTestSession)]
public class WriteTests(SeededFixture fixture) 
    : PgTest<SeededFixture, TestDbContext>(fixture)
{
    [Test]
    public async Task Insert_BlogPost_Is_Visible_Within_Test()
    {
        Context.BlogPosts.Add(new BlogPost { Title = "New Post" });
        await Context.SaveChangesAsync();

        var count = await NoTrackingContext.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(2); // seeded + new
    }

    [Test]
    public async Task Insert_Does_Not_Leak_To_Other_Tests()
    {
        // If isolation works, this test only sees the 1 seeded post,
        // even if InsertBlogPost ran before it.
        var count = await Context.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(1);

        // Now insert
        Context.BlogPosts.Add(new BlogPost { Title = "Isolated Insert" });
        await Context.SaveChangesAsync();

        var newCount = await NoTrackingContext.BlogPosts.CountAsync();
        await Assert.That(newCount).IsEqualTo(2);
    }

    [Test]
    public async Task Update_Seeded_Post()
    {
        var post = await Context.BlogPosts.SingleAsync();
        post.Title = "Updated Title";
        await Context.SaveChangesAsync();

        // Verify update via no-tracking
        var reloaded = await NoTrackingContext.BlogPosts.SingleAsync();
        await Assert.That(reloaded.Title).IsEqualTo("Updated Title");
    }

    [Test]
    public async Task Delete_Seeded_Post()
    {
        var post = await Context.BlogPosts.SingleAsync();
        Context.BlogPosts.Remove(post);
        await Context.SaveChangesAsync();

        var count = await NoTrackingContext.BlogPosts.CountAsync();
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task Add_Comment_To_Seeded_Post()
    {
        var post = await Context.BlogPosts.SingleAsync();
        post.Comments.Add(new Comment { Author = "Bob", Text = "Great post!" });
        await Context.SaveChangesAsync();

        var comments = await NoTrackingContext.Comments.ToListAsync();
        await Assert.That(comments).Count().IsEqualTo(2); // seeded Alice + new Bob
    }

    [Test]
    [Repeat(3)]
    public async Task Repeated_Write_Tests_Are_Isolated()
    {
        // Each repetition: exactly 1 seeded post
        var countBefore = await Context.BlogPosts.CountAsync();
        await Assert.That(countBefore).IsEqualTo(1);

        Context.BlogPosts.Add(new BlogPost { Title = $"Repeat" });
        await Context.SaveChangesAsync();

        var countAfter = await NoTrackingContext.BlogPosts.CountAsync();
        await Assert.That(countAfter).IsEqualTo(2); // seeded + 1 inserted
    }
}
