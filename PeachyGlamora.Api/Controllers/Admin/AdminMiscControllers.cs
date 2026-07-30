using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers.Admin;

//[ApiController]
//[Route("api/admin/coupons")]
//[Authorize(Roles = "Admin")]
//public class AdminCouponsController : ControllerBase
//{
//    private readonly AppDbContext _db;
//    public AdminCouponsController(AppDbContext db) => _db = db;

//    public record CouponUpsertDto(string Code, CouponType Type, decimal Value, decimal? MinOrderValue,
//        decimal? MaxDiscountAmount, DateTime ValidFrom, DateTime ValidTo, int? UsageLimitPerUser,
//        int? TotalUsageLimit, bool IsActive);

//    [HttpGet]
//    public async Task<IActionResult> GetAll() => Ok(await _db.Coupons.OrderByDescending(c => c.ValidFrom).ToListAsync());

//    [HttpPost]
//    public async Task<IActionResult> Create(CouponUpsertDto dto)
//    {
//        if (await _db.Coupons.AnyAsync(c => c.Code == dto.Code.ToUpper()))
//            return BadRequest(new { error = "A coupon with this code already exists." });

//        var coupon = new Coupon
//        {
//            Code = dto.Code.ToUpper(), Type = dto.Type, Value = dto.Value, MinOrderValue = dto.MinOrderValue,
//            MaxDiscountAmount = dto.MaxDiscountAmount, ValidFrom = dto.ValidFrom, ValidTo = dto.ValidTo,
//            UsageLimitPerUser = dto.UsageLimitPerUser, TotalUsageLimit = dto.TotalUsageLimit, IsActive = dto.IsActive
//        };
//        _db.Coupons.Add(coupon);
//        await _db.SaveChangesAsync();
//        return Ok(coupon);
//    }

//    [HttpPut("{id:int}")]
//    public async Task<IActionResult> Update(int id, CouponUpsertDto dto)
//    {
//        var coupon = await _db.Coupons.FindAsync(id);
//        if (coupon == null) return NotFound();
//        coupon.Value = dto.Value; coupon.MinOrderValue = dto.MinOrderValue; coupon.MaxDiscountAmount = dto.MaxDiscountAmount;
//        coupon.ValidFrom = dto.ValidFrom; coupon.ValidTo = dto.ValidTo; coupon.UsageLimitPerUser = dto.UsageLimitPerUser;
//        coupon.TotalUsageLimit = dto.TotalUsageLimit; coupon.IsActive = dto.IsActive;
//        await _db.SaveChangesAsync();
//        return Ok(coupon);
//    }

//    [HttpDelete("{id:int}")]
//    public async Task<IActionResult> Delete(int id)
//    {
//        var coupon = await _db.Coupons.FindAsync(id);
//        if (coupon == null) return NotFound();
//        coupon.IsActive = false; // soft-delete so past orders that used it stay meaningful
//        await _db.SaveChangesAsync();
//        return Ok();
//    }
//}

[ApiController]
[Route("api/admin/reviews")]
[Authorize(Roles = "Admin")]
public class AdminReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminReviewsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(await _db.Reviews.Include(r => r.Product).Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.CreatedAt, ProductName = r.Product.Name, CustomerName = r.User.FullName })
            .ToListAsync());

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review == null) return NotFound();
        _db.Reviews.Remove(review);
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("questions/{id:int}/answer")]
    public async Task<IActionResult> AnswerQuestion(int id, [FromBody] string answer)
    {
        var question = await _db.ProductQuestions.FindAsync(id);
        if (question == null) return NotFound();
        question.Answer = answer;
        question.AnsweredAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(question);
    }
}

[ApiController]
[Route("api/admin/blog")]
[Authorize(Roles = "Admin")]
public class AdminBlogController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminBlogController(AppDbContext db) => _db = db;

    public record PostUpsertDto(string Title, string Slug, string Excerpt, string ContentHtml,
        string CoverImageUrl, int BlogCategoryId, string AuthorName, bool IsPublished,
        string? MetaTitle, string? MetaDescription);

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _db.BlogPosts.OrderByDescending(p => p.PublishedAt).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(PostUpsertDto dto)
    {
        var post = new BlogPost
        {
            Title = dto.Title, Slug = dto.Slug, Excerpt = dto.Excerpt, ContentHtml = dto.ContentHtml,
            CoverImageUrl = dto.CoverImageUrl, BlogCategoryId = dto.BlogCategoryId, AuthorName = dto.AuthorName,
            IsPublished = dto.IsPublished, MetaTitle = dto.MetaTitle, MetaDescription = dto.MetaDescription
        };
        _db.BlogPosts.Add(post);
        await _db.SaveChangesAsync();
        return Ok(post);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PostUpsertDto dto)
    {
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound();
        post.Title = dto.Title; post.Slug = dto.Slug; post.Excerpt = dto.Excerpt; post.ContentHtml = dto.ContentHtml;
        post.CoverImageUrl = dto.CoverImageUrl; post.BlogCategoryId = dto.BlogCategoryId; post.AuthorName = dto.AuthorName;
        post.IsPublished = dto.IsPublished; post.MetaTitle = dto.MetaTitle; post.MetaDescription = dto.MetaDescription;
        await _db.SaveChangesAsync();
        return Ok(post);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound();
        _db.BlogPosts.Remove(post);
        await _db.SaveChangesAsync();
        return Ok();
    }
}

[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = "Admin")]
public class AdminAnalyticsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminAnalyticsController(AppDbContext db) => _db = db;

    // Headline numbers for the admin dashboard landing page.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var ordersInRange = _db.Orders.Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled);

        var totalRevenue = await ordersInRange.SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
        var orderCount = await ordersInRange.CountAsync();
        var newCustomers = await _db.Users.CountAsync(u => u.CreatedAt >= since);
        var pendingOrders = await _db.Orders.CountAsync(o => o.Status == OrderStatus.Pending);
        var lowStockVariants = await _db.ProductVariants.CountAsync(v => v.StockQuantity <= 5 && v.StockQuantity > 0);
        var outOfStockVariants = await _db.ProductVariants.CountAsync(v => v.StockQuantity == 0);

        return Ok(new
        {
            periodDays = days, totalRevenue, orderCount,
            averageOrderValue = orderCount > 0 ? Math.Round(totalRevenue / orderCount, 2) : 0,
            newCustomers, pendingOrders, lowStockVariants, outOfStockVariants
        });
    }

    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProducts([FromQuery] int take = 10)
    {
        var top = await _db.OrderItems
            .GroupBy(i => i.ProductNameSnapshot)
            .Select(g => new { ProductName = g.Key, UnitsSold = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.Quantity * i.UnitPriceSnapshot) })
            .OrderByDescending(x => x.UnitsSold).Take(take).ToListAsync();
        return Ok(top);
    }

    [HttpGet("revenue-by-day")]
    public async Task<IActionResult> GetRevenueByDay([FromQuery] int days = 14)
    {
        var since = DateTime.UtcNow.Date.AddDays(-days);
        var data = await _db.Orders
            .Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.TotalAmount), Orders = g.Count() })
            .OrderBy(x => x.Date).ToListAsync();
        return Ok(data);
    }
}
