namespace PeachyGlamora.Api.Models;

public class BlogCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public ICollection<BlogPost> Posts { get; set; } = new List<BlogPost>();
}

public class BlogPost
{
    public int Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Excerpt { get; set; } = default!;
    public string ContentHtml { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public int BlogCategoryId { get; set; }
    public BlogCategory BlogCategory { get; set; } = default!;
    public string AuthorName { get; set; } = default!;
    public bool IsPublished { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}
