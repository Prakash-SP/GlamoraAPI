namespace PeachyGlamora.Api.DTOs;

// Query params the collection page's filter sidebar maps directly onto.
public class ProductQueryParams
{
    public string? CategorySlug { get; set; }
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
    List<string> ImageUrls, List<ProductVariantDto> Variants,
    double AverageRating, int ReviewCount, int StockQuantity);

public record ProductVariantDto(int Id, string? Color, string? ColorHex, string? Size, decimal Price, int StockQuantity);

public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);
