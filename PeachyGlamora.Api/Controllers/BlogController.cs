using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/blog")]
public class BlogController : ControllerBase
{
    private readonly AppDbContext _db;
    public BlogController(AppDbContext db) => _db = db;

    [HttpGet("posts")]
    public async Task<IActionResult> GetPosts([FromQuery] string? category, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 9)
    {
        var query = _db.BlogPosts.Include(p => p.BlogCategory).Where(p => p.IsPublished).AsQueryable();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.BlogCategory.Slug == category);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Title.Contains(search) || p.Excerpt.Contains(search));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(p => p.PublishedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new { p.Title, p.Slug, p.Excerpt, p.CoverImageUrl, p.PublishedAt, CategoryName = p.BlogCategory.Name })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("posts/{slug}")]
    public async Task<IActionResult> GetPost(string slug)
    {
        var post = await _db.BlogPosts.Include(p => p.BlogCategory).FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished);
        if (post == null) return NotFound();

        var related = await _db.BlogPosts
            .Where(p => p.BlogCategoryId == post.BlogCategoryId && p.Id != post.Id && p.IsPublished)
            .OrderByDescending(p => p.PublishedAt).Take(3)
            .Select(p => new { p.Title, p.Slug, p.CoverImageUrl }).ToListAsync();

        return Ok(new { post, related });
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories() => Ok(await _db.BlogCategories.ToListAsync());
}
