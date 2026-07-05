using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CategoriesController(AppDbContext db) => _db = db;

    // Powers the header mega-menu and homepage category circles.
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await _db.Categories
            .Where(c => c.IsActive && c.ParentCategoryId == null)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.ImageUrl,
                SubCategories = c.SubCategories.Where(s => s.IsActive)
                    .Select(s => new { s.Id, s.Name, s.Slug })
            })
            .ToListAsync();

        return Ok(categories);
    }
}

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    // Backs the Collection page: every filter/sort control maps to a query param here.
    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] ProductQueryParams q)
    {
        var query = _db.Products.Include(p => p.Images).Include(p => p.Variants)
            .Include(p => p.Reviews).Include(p => p.Category)
            .Where(p => p.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q.CategorySlug))
            query = query.Where(p => p.Category.Slug == q.CategorySlug);

        if (q.MinPrice.HasValue) query = query.Where(p => p.Variants.Any(v => v.PriceOverride >= q.MinPrice));
        if (q.MaxPrice.HasValue) query = query.Where(p => p.Variants.Any(v => v.PriceOverride <= q.MaxPrice));

        if (q.Materials is { Length: > 0 })
        {
            var materials = q.Materials.Select(m => Enum.Parse<ProductMaterial>(m, true)).ToArray();
            query = query.Where(p => materials.Contains(p.Material));
        }
        if (q.Occasions is { Length: > 0 })
        {
            var occasions = q.Occasions.Select(o => Enum.Parse<ProductOccasion>(o, true)).ToArray();
            query = query.Where(p => occasions.Contains(p.Occasion));
        }
        if (q.Colors is { Length: > 0 })
            query = query.Where(p => p.Variants.Any(v => q.Colors.Contains(v.Color)));

        if (q.IsNewArrival == true) query = query.Where(p => p.IsNewArrival);
        if (q.IsBestSeller == true) query = query.Where(p => p.IsBestSeller);
        if (q.IsTrending == true) query = query.Where(p => p.IsTrending);
        if (q.InStockOnly == true) query = query.Where(p => p.Variants.Any(v => v.StockQuantity > 0));

        query = q.SortBy switch
        {
            "priceLow" => query.OrderBy(p => p.Variants.Min(v => v.PriceOverride)),
            "priceHigh" => query.OrderByDescending(p => p.Variants.Min(v => v.PriceOverride)),
            "bestselling" => query.OrderByDescending(p => p.IsBestSeller).ThenByDescending(p => p.Reviews.Count),
            "popular" => query.OrderByDescending(p => p.Reviews.Count),
            _ => query.OrderByDescending(p => p.CreatedAt) // "newest"
        };

        var totalCount = await query.CountAsync();
        var page = query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize);

        var items = await page.Select(p => new ProductListItemDto(
            p.Id, p.Name, p.Slug, p.Category.Name,
            p.Variants.Min(v => v.PriceOverride),
            p.CompareAtPrice,
            p.Images.Where(i => i.IsPrimary).Select(i => i.Url).FirstOrDefault()
                ?? p.Images.Select(i => i.Url).FirstOrDefault() ?? "",
            p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
            p.Reviews.Count,
            p.Variants.Any(v => v.StockQuantity > 0),
            p.IsNewArrival ? "New" : p.IsBestSeller ? "Bestseller" : null
        )).ToListAsync();

        // Rating filter applied post-projection since it's an aggregate.
        if (q.MinRating.HasValue) items = items.Where(i => i.AverageRating >= q.MinRating).ToList();

        return Ok(new PagedResult<ProductListItemDto>(items, totalCount, q.Page, q.PageSize));
    }

    // Backs the Product Detail page.
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        var p = await _db.Products
            .Include(x => x.Images).Include(x => x.Variants).Include(x => x.Reviews)
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive);

        if (p == null) return NotFound();

        var dto = new ProductDetailDto(
            p.Id, p.Name, p.Slug, p.Description,
            p.Variants.FirstOrDefault(v => v.IsDefault)?.Sku ?? p.Variants.First().Sku,
            p.Variants.Min(v => v.PriceOverride), p.CompareAtPrice, p.TaxRatePercent,
            p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Url).ToList(),
            p.Variants.Select(v => new ProductVariantDto(v.Id, v.Color, v.ColorHex, v.Size, v.PriceOverride, v.StockQuantity)).ToList(),
            p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
            p.Reviews.Count,
            p.Variants.Sum(v => v.StockQuantity)
        );

        return Ok(dto);
    }

    // Powers "You May Also Like" — same category, excluding the current product.
    [HttpGet("{id:int}/related")]
    public async Task<IActionResult> GetRelated(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();

        var related = await _db.Products
            .Include(p => p.Images).Include(p => p.Variants)
            .Where(p => p.CategoryId == product.CategoryId && p.Id != id && p.IsActive)
            .Take(4)
            .Select(p => new ProductListItemDto(
                p.Id, p.Name, p.Slug, "", p.Variants.Min(v => v.PriceOverride), p.CompareAtPrice,
                p.Images.Select(i => i.Url).FirstOrDefault() ?? "", 0, 0,
                p.Variants.Any(v => v.StockQuantity > 0), null))
            .ToListAsync();

        return Ok(related);
    }
}
