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
        int CategoryId, string Occasion, string Material, string? Finish,
        decimal BasePrice, decimal? CompareAtPrice, int HsnTaxRateId,
        bool IsNewArrival, bool IsBestSeller, bool IsTrending, bool IsFeatured, bool IsActive,
        string? MetaTitle, string? MetaDescription);

    public record VariantUpsertDto(string Sku, string? Color, string? ColorHex, string? Size,
        decimal PriceOverride, int StockQuantity, bool IsDefault);

    public record ImageUpsertDto(string Url, string? AltText, int DisplayOrder, bool IsPrimary);

    // Admin grid: paginated, includes inactive/out-of-stock products unlike the public endpoint.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var query = _db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Name.Contains(search));

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.IsActive,
                CategoryName = p.Category.Name,
                TotalStock = p.Variants.Sum(v => v.StockQuantity),
                MinPrice = p.Variants.Min(v => (decimal?)v.PriceOverride) ?? p.BasePrice
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var product = await _db.Products.Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Description,
                p.ShortDescription,
                p.CategoryId,
                p.Occasion,
                p.Material,
                p.Finish,
                p.BasePrice,
                p.CompareAtPrice,
                p.HsnTaxRateId,
                HsnCode = p.HsnTaxRate.HsnCode,
                TaxRatePercent = p.HsnTaxRate.TaxRatePercent,
                p.IsNewArrival,
                p.IsBestSeller,
                p.IsTrending,
                p.IsFeatured,
                p.IsActive,
                p.MetaTitle,
                p.MetaDescription,
                Variants = p.Variants.Select(v => new
                {
                    v.Id,
                    v.Sku,
                    v.Color,
                    v.ColorHex,
                    v.Size,
                    v.PriceOverride,
                    v.StockQuantity,
                    v.IsDefault
                }),
                Images = p.Images.Select(i => new
                {
                    i.Id,
                    i.Url,
                    i.AltText,
                    i.DisplayOrder,
                    i.IsPrimary
                }),
            })
            .FirstOrDefaultAsync();

        return product == null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProductUpsertDto dto)
    {
        var product = Map(new Product(), dto);
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        var result = new
        {
            product.Id,
            product.Name,
            product.Slug,
            product.Description,
            product.ShortDescription,
            product.CategoryId,
            product.Occasion,
            product.Material,
            product.Finish,
            product.BasePrice,
            product.CompareAtPrice,
            product.TaxRatePercent,
            product.IsNewArrival,
            product.IsBestSeller,
            product.IsTrending,
            product.IsFeatured,
            product.IsActive,
            product.MetaTitle,
            product.MetaDescription,
            Variants = Array.Empty<object>(),
            Images = Array.Empty<object>(),
        };
        return CreatedAtAction(nameof(GetOne), new { id = product.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ProductUpsertDto dto)
    {
        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();

        var basePriceChanged = product.BasePrice != dto.BasePrice;
        Map(product, dto);

        // Base Price is a bulk "set price for every variant" action — editing
        // it cascades to every variant's PriceOverride. To change just one
        // variant, edit that variant's price directly instead.
        if (basePriceChanged)
        {
            foreach (var variant in product.Variants)
                variant.PriceOverride = dto.BasePrice;
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            product.Id,
            product.Name,
            product.Slug,
            product.Description,
            product.ShortDescription,
            product.CategoryId,
            product.Occasion,
            product.Material,
            product.Finish,
            product.BasePrice,
            product.CompareAtPrice,
            product.TaxRatePercent,
            product.IsNewArrival,
            product.IsBestSeller,
            product.IsTrending,
            product.IsFeatured,
            product.IsActive,
            product.MetaTitle,
            product.MetaDescription,
        });
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

        // Only one variant per product can be the default — clear it on
        // every existing sibling before this one takes the flag.
        if (dto.IsDefault)
            await _db.ProductVariants.Where(v => v.ProductId == id && v.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDefault, false));

        var variant = new ProductVariant
        {
            ProductId = id,
            Sku = dto.Sku,
            Color = dto.Color,
            ColorHex = dto.ColorHex,
            Size = dto.Size,
            PriceOverride = dto.PriceOverride,
            StockQuantity = dto.StockQuantity,
            IsDefault = dto.IsDefault
        };
        _db.ProductVariants.Add(variant);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            variant.Id,
            variant.Sku,
            variant.Color,
            variant.ColorHex,
            variant.Size,
            variant.PriceOverride,
            variant.StockQuantity,
            variant.IsDefault
        });
    }

    // Full edit of an already-created variant (SKU/Color/Size/Default flag,
    // not just price or stock in isolation). Reuses the same VariantUpsertDto
    // shape as AddVariant.
    [HttpPut("variants/{variantId:int}")]
    public async Task<IActionResult> UpdateVariant(int variantId, VariantUpsertDto dto)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return NotFound();

        // Same one-default-per-product rule as AddVariant — clear siblings
        // (excluding this variant itself) before applying the flag here.
        if (dto.IsDefault)
            await _db.ProductVariants
                .Where(v => v.ProductId == variant.ProductId && v.Id != variantId && v.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDefault, false));

        variant.Sku = dto.Sku;
        variant.Color = dto.Color;
        variant.ColorHex = dto.ColorHex;
        variant.Size = dto.Size;
        variant.PriceOverride = dto.PriceOverride;
        variant.StockQuantity = dto.StockQuantity;
        variant.IsDefault = dto.IsDefault;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            variant.Id,
            variant.Sku,
            variant.Color,
            variant.ColorHex,
            variant.Size,
            variant.PriceOverride,
            variant.StockQuantity,
            variant.IsDefault
        });
    }

    [HttpPut("variants/{variantId:int}/stock")]
    public async Task<IActionResult> UpdateStock(int variantId, [FromBody] int newQuantity)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return NotFound();
        variant.StockQuantity = newQuantity;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            variant.Id,
            variant.Sku,
            variant.Color,
            variant.ColorHex,
            variant.Size,
            variant.PriceOverride,
            variant.StockQuantity,
            variant.IsDefault
        });
    }

    // Previously the ONLY way to change a variant's actual selling price was
    // direct SQL — the admin "Base Price" field on the product form only sets
    // Product.BasePrice, which the storefront never reads (PDP/cart/checkout
    // all read Variants.Min(v => v.PriceOverride)). This closes that gap.
    [HttpPut("variants/{variantId:int}/price")]
    public async Task<IActionResult> UpdateVariantPrice(int variantId, [FromBody] decimal newPrice)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return NotFound();
        variant.PriceOverride = newPrice;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            variant.Id,
            variant.Sku,
            variant.Color,
            variant.ColorHex,
            variant.Size,
            variant.PriceOverride,
            variant.StockQuantity,
            variant.IsDefault
        });
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

        return Ok(new { image.Id, image.Url, image.AltText, image.DisplayOrder, image.IsPrimary });
    }

    private static Product Map(Product p, ProductUpsertDto dto)
    {
        p.Name = dto.Name; p.Slug = dto.Slug; p.Description = dto.Description; p.ShortDescription = dto.ShortDescription;
        p.CategoryId = dto.CategoryId; p.Occasion = dto.Occasion; p.Material = dto.Material; p.Finish = dto.Finish;
        p.BasePrice = dto.BasePrice; p.CompareAtPrice = dto.CompareAtPrice; p.HsnTaxRateId = dto.HsnTaxRateId;
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
    public async Task<IActionResult> GetAll() =>
    Ok(await _db.Categories
        .OrderBy(c => c.DisplayOrder)
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.Slug,
            c.Description,
            c.ImageUrl,
            c.ParentCategoryId,
            c.DisplayOrder,
            c.IsActive,
        })
        .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(CategoryUpsertDto dto)
    {
        var category = new Category
        {
            Name = dto.Name,
            Slug = dto.Slug,
            Description = dto.Description,
            ImageUrl = dto.ImageUrl,
            ParentCategoryId = dto.ParentCategoryId,
            DisplayOrder = dto.DisplayOrder,
            IsActive = dto.IsActive
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.ImageUrl,
            category.ParentCategoryId,
            category.DisplayOrder,
            category.IsActive
        });
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

        return Ok(new
        {
            category.Id,
            category.Name,
            category.Slug,
            category.Description,
            category.ImageUrl,
            category.ParentCategoryId,
            category.DisplayOrder,
            category.IsActive
        });
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
