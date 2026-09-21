namespace PeachyGlamora.Api.DTOs;

// Query params the collection page's filter sidebar maps directly onto.
public class ProductQueryParams
{
    public string? CategorySlug { get; set; }
    public string? Search { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string[]? Colors { get; set; }
    public string[]? Materials { get; set; }
    public string[]? Occasions { get; set; }
    public bool? IsNewArrival { get; set; }
    public bool? IsBestSeller { get; set; }
    public bool? IsTrending { get; set; }
    public bool? InStockOnly { get; set; }
    public int? MinRating { get; set; }
    public string SortBy { get; set; } = "newest"; // newest | priceLow | priceHigh | bestselling | popular
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public record ProductListItemDto(
    int Id, string Name, string Slug, string CategoryName,
    decimal Price, decimal? CompareAtPrice, string PrimaryImageUrl,
    double AverageRating, int ReviewCount, bool InStock, string? Tag);

public record ProductDetailDto(
    int Id, string Name, string Slug, string Description, string Sku,
    decimal Price, decimal? CompareAtPrice, decimal TaxRatePercent,
    List<ProductImageDto> Images, List<ProductVariantDto> Variants,
    double AverageRating, int ReviewCount, int StockQuantity);

// NEW — replaces the old flat List<string> ImageUrls. ProductVariantId is
// null for shared/general images (shown regardless of which variant is
// selected); set for images that belong to one specific color/size.
public record ProductImageDto(string Url, int? ProductVariantId);

public record ProductVariantDto(int Id, string? Color, string? ColorHex, string? Size, decimal Price, int StockQuantity);

public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);

// Backs GET /api/wishlist. DefaultVariantId is the variant "Add to Bag"/"Proceed
// to Buy" actually adds to cart with — the wishlist is product-level, but
// POST /api/cart/items needs a variant id, so this picks the default variant
// if it's in stock, otherwise the first in-stock variant, otherwise null
// (out-of-stock product — nothing to add).
public record WishlistItemDto(
    int ProductId, string Name, string Slug, string ImageUrl,
    decimal Price, decimal? CompareAtPrice, bool InStock,
    int? DefaultVariantId, DateTime AddedAt,
    string? VariantColor, string? VariantSize);

// Backs GET /api/reviews/featured — real, verified reviews used as homepage
// testimonials, not admin-curated content. ProductName stands in for what a
// "city" field would show (Review/ApplicationUser have no city of their
// own — only Address does, and that's not tied to a specific review).
public record FeaturedTestimonialDto(int Id, string Quote, string CustomerName, string ProductName, int Rating);
