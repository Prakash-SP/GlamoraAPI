using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = "Admin")]
public class AdminProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminProductsController(AppDbContext db) => _db = db;

    public record ProductUpsertDto(
        string Name, string Slug, string Description, string ShortDescription,
        int CategoryId, ProductOccasion Occasion, ProductMaterial Material,
        decimal BasePrice, decimal? CompareAtPrice, decimal TaxRatePercent,
        bool IsNewArrival, bool IsBestSeller, bool IsTrending, bool IsFeatured, bool IsActive,
        string? MetaTitle, string? MetaDescription);

    public record VariantUpsertDto(string Sku, string? Color, string? ColorHex, string? Size,
        decimal PriceOverride, int StockQuantity, bool IsDefault);

    public record ImageUpsertDto(string Url, string? AltText, int DisplayOrder, bool IsPrimary);

    // Admin grid: paginated, includes inactive/out-of-stock products unlike the public endpoint.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var query = _db.Products.Include(p => p.Category).Include(p => p.Variants).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Name.Contains(search));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.IsActive, CategoryName = p.Category.Name,
                TotalStock = p.Variants.Sum(v => v.StockQuantity),
                MinPrice = p.Variants.Min(v => (decimal?)v.PriceOverride) ?? p.BasePrice
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var product = await _db.Products.Include(p => p.Variants).Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);
        return product == null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProductUpsertDto dto)
    {
        var product = Map(new Product(), dto);
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetOne), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ProductUpsertDto dto)
    {
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();
        Map(product, dto);
        await _db.SaveChangesAsync();
        return Ok(product);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        // Soft-delete: keeps historical order line items (OrderItem snapshots) intact and valid.
        var product = await _db.Products.FindAsync(id);
        if (product == null) return NotFound();
        product.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Product deactivated." });
    }

    [HttpPost("{id:int}/variants")]
    public async Task<IActionResult> AddVariant(int id, VariantUpsertDto dto)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == id)) return NotFound();
        var variant = new ProductVariant
        {
            ProductId = id, Sku = dto.Sku, Color = dto.Color, ColorHex = dto.ColorHex,
            Size = dto.Size, PriceOverride = dto.PriceOverride, StockQuantity = dto.StockQuantity, IsDefault = dto.IsDefault
        };
        _db.ProductVariants.Add(variant);
        await _db.SaveChangesAsync();
        return Ok(variant);
    }

    [HttpPut("variants/{variantId:int}/stock")]
    public async Task<IActionResult> UpdateStock(int variantId, [FromBody] int newQuantity)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return NotFound();
        variant.StockQuantity = newQuantity;
        await _db.SaveChangesAsync();
        return Ok(variant);
    }

    // Actual file upload to Cloudinary happens in a dedicated MediaController (signed upload);
    // this just attaches the resulting URL to the product once the client has it.
    [HttpPost("{id:int}/images")]
    public async Task<IActionResult> AddImage(int id, ImageUpsertDto dto)
    {
        if (!await _db.Products.AnyAsync(p => p.Id == id)) return NotFound();
        var image = new ProductImage { ProductId = id, Url = dto.Url, AltText = dto.AltText, DisplayOrder = dto.DisplayOrder, IsPrimary = dto.IsPrimary };
        _db.ProductImages.Add(image);
        await _db.SaveChangesAsync();
        return Ok(image);
    }

    private static Product Map(Product p, ProductUpsertDto dto)
    {
        p.Name = dto.Name; p.Slug = dto.Slug; p.Description = dto.Description; p.ShortDescription = dto.ShortDescription;
        p.CategoryId = dto.CategoryId; p.Occasion = dto.Occasion; p.Material = dto.Material;
        p.BasePrice = dto.BasePrice; p.CompareAtPrice = dto.CompareAtPrice; p.TaxRatePercent = dto.TaxRatePercent;
        p.IsNewArrival = dto.IsNewArrival; p.IsBestSeller = dto.IsBestSeller; p.IsTrending = dto.IsTrending;
        p.IsFeatured = dto.IsFeatured; p.IsActive = dto.IsActive;
        p.MetaTitle = dto.MetaTitle; p.MetaDescription = dto.MetaDescription;
        return p;
    }
}

[ApiController]
[Route("api/admin/categories")]
[Authorize(Roles = "Admin")]
public class AdminCategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminCategoriesController(AppDbContext db) => _db = db;

    public record CategoryUpsertDto(string Name, string Slug, string? Description, string? ImageUrl,
        int? ParentCategoryId, int DisplayOrder, bool IsActive);

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _db.Categories.OrderBy(c => c.DisplayOrder).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(CategoryUpsertDto dto)
    {
        var category = new Category
        {
            Name = dto.Name, Slug = dto.Slug, Description = dto.Description, ImageUrl = dto.ImageUrl,
            ParentCategoryId = dto.ParentCategoryId, DisplayOrder = dto.DisplayOrder, IsActive = dto.IsActive
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return Ok(category);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CategoryUpsertDto dto)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();
        category.Name = dto.Name; category.Slug = dto.Slug; category.Description = dto.Description;
        category.ImageUrl = dto.ImageUrl; category.ParentCategoryId = dto.ParentCategoryId;
        category.DisplayOrder = dto.DisplayOrder; category.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return Ok(category);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();
        if (await _db.Products.AnyAsync(p => p.CategoryId == id))
            return BadRequest(new { error = "Cannot delete a category that still has products. Reassign or deactivate them first." });
        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
