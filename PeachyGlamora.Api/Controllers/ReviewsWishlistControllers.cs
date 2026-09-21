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
            .Include(w => w.ProductVariant)
            .Where(w => w.UserId == UserId)
            .OrderByDescending(w => w.AddedAt)
            .Select(w => new WishlistItemDto(
                w.Product.Id,
                w.Product.Name,
                w.Product.Slug,
                // Prefer a photo tagged to the SPECIFIC variant this customer
                // wishlisted (e.g. the Teal photo), before falling back to
                // the product's general primary/first image — same pattern
                // as CartService's image resolution.
                w.ProductVariantId != null
                    ? w.Product.Images.Where(i => i.ProductVariantId == w.ProductVariantId).Select(i => i.Url).FirstOrDefault()
                        ?? w.Product.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault()
                        ?? w.Product.Images.Select(i => i.Url).FirstOrDefault() ?? ""
                    : w.Product.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault()
                        ?? w.Product.Images.Select(i => i.Url).FirstOrDefault() ?? "",
                w.Product.Variants.Min(v => v.PriceOverride),
                w.Product.CompareAtPrice,
                w.Product.Variants.Any(v => v.StockQuantity > 0),
                // Prefer the exact variant the customer wishlisted, as long as
                // it's still in stock; otherwise fall back to the previous
                // "any in-stock default" behavior — this keeps wishlist rows
                // added before this feature existed (ProductVariantId = null)
                // working exactly as they did before.
                (w.ProductVariant != null && w.ProductVariant.StockQuantity > 0)
                    ? w.ProductVariantId
                    : w.Product.Variants.Where(v => v.StockQuantity > 0)
                        .OrderByDescending(v => v.IsDefault)
                        .Select(v => (int?)v.Id)
                        .FirstOrDefault(),
                w.AddedAt,
                w.ProductVariant != null ? w.ProductVariant.Color : null,
                w.ProductVariant != null ? w.ProductVariant.Size : null))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("{productId:int}")]
    public async Task<IActionResult> Add(int productId, [FromQuery] int? variantId)
    {
        // Guard against a variant id belonging to a DIFFERENT product being
        // sent by mistake — same check used for image tagging.
        if (variantId.HasValue &&
            !await _db.ProductVariants.AnyAsync(v => v.Id == variantId && v.ProductId == productId))
            return BadRequest(new { error = "That variant does not belong to this product." });

        var existing = await _db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == UserId && w.ProductId == productId);
        if (existing == null)
        {
            _db.WishlistItems.Add(new WishlistItem { UserId = UserId, ProductId = productId, ProductVariantId = variantId });
            await _db.SaveChangesAsync();
        }
        else if (variantId.HasValue && existing.ProductVariantId != variantId)
        {
            // Already wishlisted (e.g. under a different variant) — update
            // which variant is recorded rather than silently ignoring the
            // new selection. Wishlist stays product-level/one-row-per-product,
            // so re-wishlisting under a different color just retags it.
            existing.ProductVariantId = variantId;
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
