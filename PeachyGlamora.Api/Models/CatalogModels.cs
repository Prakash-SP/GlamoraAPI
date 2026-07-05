namespace PeachyGlamora.Api.Models;

/// <summary>Product category, self-referencing for subcategories (e.g. Necklaces → Chokers).</summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public enum ProductOccasion { Bridal, Party, Office, Daily, Festive }
public enum ProductMaterial { RoseGoldPlated, GoldPlated, Kundan, Pearl, AmericanDiamond, Oxidised, Beaded }

/// <summary>The sellable product. Price/stock live on ProductVariant so a product can have
/// multiple colours / sizes, each with its own SKU and inventory count.</summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string ShortDescription { get; set; } = default!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = default!;

    public ProductOccasion Occasion { get; set; }
    public ProductMaterial Material { get; set; }

    public decimal BasePrice { get; set; }
    public decimal? CompareAtPrice { get; set; }   // "was" price, for showing % off
    public decimal TaxRatePercent { get; set; } = 3m; // GST slab for artificial jewellery

    public bool IsNewArrival { get; set; }
    public bool IsBestSeller { get; set; }
    public bool IsTrending { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<ProductQuestion> Questions { get; set; } = new List<ProductQuestion>();

    // SEO
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}

/// <summary>A purchasable SKU: a specific colour/size combination of a product.</summary>
public class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public string Sku { get; set; } = default!;
    public string? Color { get; set; }
    public string? ColorHex { get; set; }
    public string? Size { get; set; }              // e.g. chain length, ring size
    public decimal PriceOverride { get; set; }       // final sellable price for this variant
    public int StockQuantity { get; set; }
    public bool IsDefault { get; set; }

    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public string Url { get; set; } = default!;       // Cloudinary URL
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public class Review
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    public int Rating { get; set; }                    // 1-5
    public string? Title { get; set; }
    public string Comment { get; set; } = default!;
    public bool IsVerifiedPurchase { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ProductQuestion
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string Question { get; set; } = default!;
    public string? Answer { get; set; }
    public DateTime AskedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAt { get; set; }
}

public class WishlistItem
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

public class RecentlyViewed
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
