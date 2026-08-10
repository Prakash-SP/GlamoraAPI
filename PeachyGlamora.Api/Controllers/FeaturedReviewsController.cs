using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;

namespace PeachyGlamora.Api.Controllers;

// Separate from ReviewsController (which is nested under
// api/products/{productId}/reviews and scoped to one product) — this is
// cross-product, store-wide, and only ever used by the homepage.
[ApiController]
[Route("api/reviews")]
public class FeaturedReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    public FeaturedReviewsController(AppDbContext db) => _db = db;

    // Real, verified-purchase reviews only — never unverified ones, since
    // this is public-facing social proof on the homepage. Prefers 5-star,
    // backfills with 4-star if there aren't yet enough 5-star reviews to
    // fill the requested count, so a new store with few reviews still shows
    // something reasonable rather than an empty or half-filled section.
    [HttpGet("featured")]
    public async Task<IActionResult> GetFeatured([FromQuery] int count = 3)
    {
        var baseQuery = _db.Reviews
            .Include(r => r.User)
            .Include(r => r.Product)
            .Where(r => r.IsVerifiedPurchase && !string.IsNullOrWhiteSpace(r.Comment));

        var fiveStar = await baseQuery
            .Where(r => r.Rating == 5)
            .OrderByDescending(r => r.CreatedAt)
            .Take(count)
            .Select(r => new FeaturedTestimonialDto(r.Id, r.Comment, r.User.FullName, r.Product.Name, r.Rating))
            .ToListAsync();

        if (fiveStar.Count >= count) return Ok(fiveStar);

        var remaining = count - fiveStar.Count;
        var pickedIds = fiveStar.Select(f => f.Id).ToHashSet();
        var fourStar = await baseQuery
            .Where(r => r.Rating == 4 && !pickedIds.Contains(r.Id))
            .OrderByDescending(r => r.CreatedAt)
            .Take(remaining)
            .Select(r => new FeaturedTestimonialDto(r.Id, r.Comment, r.User.FullName, r.Product.Name, r.Rating))
            .ToListAsync();

        return Ok(fiveStar.Concat(fourStar).ToList());
    }
}
