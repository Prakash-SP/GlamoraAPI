using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/products/{productId:int}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReviewsController(AppDbContext db) => _db = db;

    public record CreateReviewDto(int Rating, string? Title, string Comment);

    [HttpGet]
    public async Task<IActionResult> GetReviews(int productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var query = _db.Reviews.Where(r => r.ProductId == productId).OrderByDescending(r => r.CreatedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.CreatedAt, UserName = r.User.FullName })
            .ToListAsync();

        var breakdown = await _db.Reviews.Where(r => r.ProductId == productId)
            .GroupBy(r => r.Rating).Select(g => new { Stars = g.Key, Count = g.Count() }).ToListAsync();

        return Ok(new { total, average = total > 0 ? await query.AverageAsync(r => r.Rating) : 0, breakdown, items });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> AddReview(int productId, CreateReviewDto dto)
    {
        var userId = User.FindFirst("sub")!.Value;

        // Only customers who actually bought this product get the "Verified Purchase" badge.
        var isVerified = await _db.OrderItems.AnyAsync(i =>
            i.Order.UserId == userId && i.Order.Status == OrderStatus.Delivered &&
            i.ProductVariant.ProductId == productId);

        _db.Reviews.Add(new Review
        {
            ProductId = productId, UserId = userId, Rating = dto.Rating,
            Title = dto.Title, Comment = dto.Comment, IsVerifiedPurchase = isVerified
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Review submitted." });
    }
}

[ApiController]
[Route("api/products/{productId:int}/questions")]
public class ProductQuestionsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductQuestionsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetQuestions(int productId) =>
        Ok(await _db.ProductQuestions.Where(q => q.ProductId == productId && q.Answer != null)
            .OrderByDescending(q => q.AskedAt).ToListAsync());

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> AskQuestion(int productId, [FromBody] string question)
    {
        var userId = User.FindFirst("sub")!.Value;
        _db.ProductQuestions.Add(new ProductQuestion { ProductId = productId, UserId = userId, Question = question });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Question submitted — our team typically answers within 24 hours." });
    }
}

[ApiController]
[Route("api/wishlist")]
[Authorize]
public class WishlistController : ControllerBase
{
    private readonly AppDbContext _db;
    public WishlistController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirst("sub")!.Value;

    [HttpGet]
    public async Task<IActionResult> GetWishlist()
    {
        var items = await _db.WishlistItems
            .Include(w => w.Product).ThenInclude(p => p.Images)
            .Include(w => w.Product).ThenInclude(p => p.Variants)
            .Where(w => w.UserId == UserId)
            .OrderByDescending(w => w.AddedAt)
            .Select(w => new WishlistItemDto(
                w.Product.Id,
                w.Product.Name,
                w.Product.Slug,
                w.Product.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault()
                    ?? w.Product.Images.Select(i => i.Url).FirstOrDefault() ?? "",
                w.Product.Variants.Min(v => v.PriceOverride),
                w.Product.CompareAtPrice,
                w.Product.Variants.Any(v => v.StockQuantity > 0),
                // Prefer the default variant if it's actually in stock; otherwise
                // fall back to whichever variant does have stock; null if none do.
                w.Product.Variants.Where(v => v.StockQuantity > 0)
                    .OrderByDescending(v => v.IsDefault)
                    .Select(v => (int?)v.Id)
                    .FirstOrDefault(),
                w.AddedAt))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("{productId:int}")]
    public async Task<IActionResult> Add(int productId)
    {
        if (!await _db.WishlistItems.AnyAsync(w => w.UserId == UserId && w.ProductId == productId))
        {
            _db.WishlistItems.Add(new WishlistItem { UserId = UserId, ProductId = productId });
            await _db.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpDelete("{productId:int}")]
    public async Task<IActionResult> Remove(int productId)
    {
        var item = await _db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == UserId && w.ProductId == productId);
        if (item != null) { _db.WishlistItems.Remove(item); await _db.SaveChangesAsync(); }
        return Ok();
    }
}
