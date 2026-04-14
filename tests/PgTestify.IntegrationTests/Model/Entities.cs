namespace PgTestify.IntegrationTests.Model;

public class BlogPost
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Comment> Comments { get; set; } = [];
}

public class Comment
{
    public int Id { get; set; }
    public required string Author { get; set; }
    public required string Text { get; set; }
    public int BlogPostId { get; set; }
    public BlogPost BlogPost { get; set; } = null!;
}
