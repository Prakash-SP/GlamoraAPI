using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/coupons")]
[Authorize(Roles = "Admin")]
public class AdminCouponsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminCouponsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var query = _db.Coupons.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Code.Contains(search.ToUpper()));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Code, c.Type, c.Value, c.ScopeType, c.MinOrderValue, c.MaxDiscountAmount,
                c.ValidFrom, c.ValidTo, c.UsageLimitPerUser, c.TotalUsageLimit, c.TimesUsed, c.IsActive
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        // .Select() projection, per the project convention — never return raw
        // Coupon entities (lazy-loading proxies + this shape would round-trip
        // through CouponProducts -> Product -> ... and risk the usual cycle bug).
        var coupon = await _db.Coupons
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id, c.Code, c.Type, c.Value, c.ScopeType, c.MinOrderValue, c.MaxDiscountAmount,
                c.ValidFrom, c.ValidTo, c.UsageLimitPerUser, c.TotalUsageLimit, c.TimesUsed, c.IsActive,
                ProductIds = c.CouponProducts.Select(cp => cp.ProductId),
                CategoryIds = c.CouponCategories.Select(cc => cc.CategoryId)
            })
            .FirstOrDefaultAsync();

        return coupon == null ? NotFound() : Ok(coupon);
    }

    public record CouponUpsertDto(
        string Code, CouponType Type, decimal Value, CouponScopeType ScopeType,
        decimal? MinOrderValue, decimal? MaxDiscountAmount, DateTime ValidFrom, DateTime ValidTo,
        int? UsageLimitPerUser, int? TotalUsageLimit, bool IsActive,
        List<int>? ProductIds, List<int>? CategoryIds);

    [HttpPost]
    public async Task<IActionResult> Create(CouponUpsertDto dto)
    {
        var code = dto.Code.Trim().ToUpper();
        if (await _db.Coupons.AnyAsync(c => c.Code == code))
            return BadRequest(new { error = "A coupon with this code already exists." });

        if (dto.ScopeType != CouponScopeType.WholeCart)
        {
            var hasScope = dto.ScopeType == CouponScopeType.SpecificProducts
                ? dto.ProductIds?.Count > 0
                : dto.CategoryIds?.Count > 0;
            if (!hasScope)
                return BadRequest(new { error = "Select at least one product or category for a scoped coupon." });
        }

        var coupon = new Coupon
        {
            Code = code, Type = dto.Type, Value = dto.Value, ScopeType = dto.ScopeType,
            MinOrderValue = dto.MinOrderValue, MaxDiscountAmount = dto.MaxDiscountAmount,
            ValidFrom = dto.ValidFrom, ValidTo = dto.ValidTo,
            UsageLimitPerUser = dto.UsageLimitPerUser, TotalUsageLimit = dto.TotalUsageLimit,
            IsActive = dto.IsActive
        };

        if (dto.ScopeType == CouponScopeType.SpecificProducts && dto.ProductIds != null)
            coupon.CouponProducts = dto.ProductIds.Distinct().Select(pid => new CouponProduct { ProductId = pid }).ToList();
        if (dto.ScopeType == CouponScopeType.SpecificCategories && dto.CategoryIds != null)
            coupon.CouponCategories = dto.CategoryIds.Distinct().Select(cid => new CouponCategory { CategoryId = cid }).ToList();

        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync();
        return Ok(new { coupon.Id, message = "Coupon created." });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CouponUpsertDto dto)
    {
        var coupon = await _db.Coupons
            .Include(c => c.CouponProducts)
            .Include(c => c.CouponCategories)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (coupon == null) return NotFound();

        var code = dto.Code.Trim().ToUpper();
        if (code != coupon.Code && await _db.Coupons.AnyAsync(c => c.Code == code && c.Id != id))
            return BadRequest(new { error = "A coupon with this code already exists." });

        coupon.Code = code;
        coupon.Type = dto.Type;
        coupon.Value = dto.Value;
        coupon.ScopeType = dto.ScopeType;
        coupon.MinOrderValue = dto.MinOrderValue;
        coupon.MaxDiscountAmount = dto.MaxDiscountAmount;
        coupon.ValidFrom = dto.ValidFrom;
        coupon.ValidTo = dto.ValidTo;
        coupon.UsageLimitPerUser = dto.UsageLimitPerUser;
        coupon.TotalUsageLimit = dto.TotalUsageLimit;
        coupon.IsActive = dto.IsActive;

        // Replace scope rows wholesale rather than diffing — simplest correct
        // approach for a small admin-managed list, avoids stale rows.
        _db.CouponProducts.RemoveRange(coupon.CouponProducts);
        _db.CouponCategories.RemoveRange(coupon.CouponCategories);
        if (dto.ScopeType == CouponScopeType.SpecificProducts && dto.ProductIds != null)
            foreach (var pid in dto.ProductIds.Distinct())
                _db.CouponProducts.Add(new CouponProduct { CouponId = id, ProductId = pid });
        if (dto.ScopeType == CouponScopeType.SpecificCategories && dto.CategoryIds != null)
            foreach (var cid in dto.CategoryIds.Distinct())
                _db.CouponCategories.Add(new CouponCategory { CouponId = id, CategoryId = cid });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Coupon updated." });
    }

    // No hard delete. A coupon that's actually been used is blocked at the DB
    // level (CouponUsage -> Coupon is Restrict), and even an unused one is
    // safer to deactivate — matches the IsActive-flag pattern already used
    // for Products/Categories rather than physical deletion.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon == null) return NotFound();
        coupon.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Coupon deactivated." });
    }
}
