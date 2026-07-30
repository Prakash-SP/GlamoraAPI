using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Services;

public record CouponResult(bool Valid, string? Error, decimal Discount, bool FreeShipping, HashSet<int> EligibleProductIds);

public interface ICartService
{
    Task<CartSummaryDto> GetCartAsync(string? userId, string? guestCartId, string? couponCode = null);
    Task AddItemAsync(string? userId, string? guestCartId, AddToCartRequest req);
    Task UpdateItemAsync(string? userId, string? guestCartId, int cartItemId, int quantity);
    Task RemoveItemAsync(string? userId, string? guestCartId, int cartItemId);

    // Takes the actual cart items (not just a subtotal number) because
    // scope-aware coupons need to know WHICH items qualify, not just the cart
    // total. Also returns whether the coupon grants free shipping and which
    // products are eligible, so callers can prorate discount correctly.
    Task<CouponResult> ValidateCouponAsync(string code, string? userId, List<CartItem> items);

    // Proration happens only across items eligible for the coupon's scope —
    // needs the eligible-only subtotal and whether THIS item is eligible.
    decimal CalculateItemTax(ProductVariant variant, int quantity, decimal eligibleSubtotal, decimal cartDiscount, bool itemIsEligible);
}

public class CartService : ICartService
{
    private readonly AppDbContext _db;
    private readonly decimal _freeShippingThreshold;
    private readonly decimal _standardShippingFee;

    // Read straight from config ("Shipping:FreeShippingThreshold" /
    // "Shipping:StandardShippingFee" in appsettings.json) — just two flat
    // values, so a dedicated IOptions<T> class would be more ceremony than
    // it's worth. Falls back to the previous hardcoded numbers if the section
    // is missing, so this can't silently break existing environments.
    public CartService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _freeShippingThreshold = config.GetValue("Shipping:FreeShippingThreshold", 999m);
        _standardShippingFee = config.GetValue("Shipping:StandardShippingFee", 79m);
    }

    private IQueryable<CartItem> ScopedItems(string? userId, string? guestCartId) =>
        _db.CartItems
            .Include(c => c.ProductVariant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .Include(c => c.ProductVariant).ThenInclude(v => v.Product).ThenInclude(p => p.HsnTaxRate)
            .Include(c => c.ProductVariant).ThenInclude(v => v.Product).ThenInclude(p => p.Category) // needed for category-scoped coupons
            .Where(c => userId != null ? c.UserId == userId : c.GuestCartId == guestCartId);

    public async Task<CartSummaryDto> GetCartAsync(string? userId, string? guestCartId, string? couponCode = null)
    {
        var items = await ScopedItems(userId, guestCartId).ToListAsync();

        var dtoItems = items.Select(c => new CartItemDto(
            c.Id, c.ProductVariantId, c.ProductVariant.Product.Name,
            c.ProductVariant.Product.Images.FirstOrDefault(i => i.IsPrimary)?.Url
                ?? c.ProductVariant.Product.Images.FirstOrDefault()?.Url ?? "",
            c.ProductVariant.Color, c.ProductVariant.Size,
            c.ProductVariant.PriceOverride, c.Quantity, c.ProductVariant.StockQuantity
        )).ToList();

        var subtotal = dtoItems.Sum(i => i.UnitPrice * i.Quantity);

        decimal discount = 0;
        bool freeShipping = false;
        string? couponError = null;
        var eligibleProductIds = new HashSet<int>();

        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var result = await ValidateCouponAsync(couponCode, userId, items);
            if (result.Valid)
            {
                discount = result.Discount;
                freeShipping = result.FreeShipping;
                eligibleProductIds = result.EligibleProductIds;
            }
            else
            {
                // Surfaced (not silently dropped) so the cart can show "this
                // coupon no longer applies" instead of just losing the discount.
                couponError = result.Error;
            }
        }

        var eligibleSubtotal = items
            .Where(c => eligibleProductIds.Contains(c.ProductVariant.ProductId))
            .Sum(c => c.ProductVariant.PriceOverride * c.Quantity);

        // Per-item tax, summed — each item's tax comes from its own product's
        // HSN code, and discount proration only spreads across items that are
        // actually eligible for the applied coupon's scope.
        var tax = items.Sum(c => CalculateItemTax(
            c.ProductVariant, c.Quantity, eligibleSubtotal, discount,
            eligibleProductIds.Contains(c.ProductVariant.ProductId)));

        var taxableAmount = subtotal - discount;
        var shipping = freeShipping || (subtotal - discount) >= _freeShippingThreshold || subtotal == 0
            ? 0 : _standardShippingFee;
        var total = taxableAmount + tax + shipping;

        // NOTE: CartSummaryDto needs a new `string? CouponError` field added
        // (after CouponCode) to accept the 8th argument here — see DTOs.cs.
        return new CartSummaryDto(dtoItems, subtotal, discount, tax, shipping, total,
            couponError == null ? couponCode : null, couponError);
    }

    // Discount is prorated only across items ELIGIBLE for the coupon's scope,
    // weighted by their share of the eligible subtotal — not the whole cart.
    // A category-scoped coupon must not shave tax off items outside that
    // category just because they happen to share a cart with eligible ones.
    public decimal CalculateItemTax(ProductVariant variant, int quantity, decimal eligibleSubtotal, decimal cartDiscount, bool itemIsEligible)
    {
        var itemGross = variant.PriceOverride * quantity;
        decimal itemDiscountShare = 0m;
        if (itemIsEligible && eligibleSubtotal > 0)
            itemDiscountShare = cartDiscount * (itemGross / eligibleSubtotal);

        var itemTaxable = itemGross - itemDiscountShare;
        return Math.Round(itemTaxable * (variant.Product.HsnTaxRate.TaxRatePercent / 100m), 2);
    }

    public async Task AddItemAsync(string? userId, string? guestCartId, AddToCartRequest req)
    {
        var variant = await _db.ProductVariants.FindAsync(req.ProductVariantId)
            ?? throw new InvalidOperationException("Product variant not found.");

        if (variant.StockQuantity < req.Quantity)
            throw new InvalidOperationException("Not enough stock available.");

        var existing = await ScopedItems(userId, guestCartId)
            .FirstOrDefaultAsync(c => c.ProductVariantId == req.ProductVariantId);

        if (existing != null)
        {
            existing.Quantity += req.Quantity;
        }
        else
        {
            _db.CartItems.Add(new CartItem
            {
                UserId = userId,
                GuestCartId = userId == null ? guestCartId : null,
                ProductVariantId = req.ProductVariantId,
                Quantity = req.Quantity
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task UpdateItemAsync(string? userId, string? guestCartId, int cartItemId, int quantity)
    {
        var item = await ScopedItems(userId, guestCartId).FirstOrDefaultAsync(c => c.Id == cartItemId)
            ?? throw new InvalidOperationException("Cart item not found.");

        if (quantity <= 0) { _db.CartItems.Remove(item); }
        else { item.Quantity = quantity; }
        await _db.SaveChangesAsync();
    }

    public async Task RemoveItemAsync(string? userId, string? guestCartId, int cartItemId)
    {
        var item = await ScopedItems(userId, guestCartId).FirstOrDefaultAsync(c => c.Id == cartItemId);
        if (item != null) { _db.CartItems.Remove(item); await _db.SaveChangesAsync(); }
    }

    public async Task<CouponResult> ValidateCouponAsync(string code, string? userId, List<CartItem> items)
    {
        var coupon = await _db.Coupons
            .Include(c => c.CouponProducts)
            .Include(c => c.CouponCategories)
            .FirstOrDefaultAsync(c => c.Code == code.ToUpper() && c.IsActive);

        if (coupon == null) return new CouponResult(false, "Invalid coupon code.", 0, false, new());
        if (DateTime.UtcNow < coupon.ValidFrom || DateTime.UtcNow > coupon.ValidTo)
            return new CouponResult(false, "This coupon has expired.", 0, false, new());
        if (coupon.TotalUsageLimit.HasValue && coupon.TimesUsed >= coupon.TotalUsageLimit)
            return new CouponResult(false, "This coupon has reached its usage limit.", 0, false, new());

        if (userId != null && coupon.UsageLimitPerUser.HasValue)
        {
            var usedByUser = await _db.CouponUsages.CountAsync(u => u.CouponId == coupon.Id && u.UserId == userId);
            if (usedByUser >= coupon.UsageLimitPerUser)
                return new CouponResult(false, "You've already used this coupon the maximum number of times.", 0, false, new());
        }

        // Resolve which products in THIS cart actually qualify for the coupon's scope.
        var eligibleProductIds = coupon.ScopeType switch
        {
            CouponScopeType.WholeCart => items.Select(c => c.ProductVariant.ProductId).ToHashSet(),
            CouponScopeType.SpecificProducts => items
                .Where(c => coupon.CouponProducts.Any(cp => cp.ProductId == c.ProductVariant.ProductId))
                .Select(c => c.ProductVariant.ProductId).ToHashSet(),
            CouponScopeType.SpecificCategories => items
                .Where(c => coupon.CouponCategories.Any(cc => cc.CategoryId == c.ProductVariant.Product.CategoryId))
                .Select(c => c.ProductVariant.ProductId).ToHashSet(),
            _ => new HashSet<int>()
        };

        var eligibleSubtotal = items
            .Where(c => eligibleProductIds.Contains(c.ProductVariant.ProductId))
            .Sum(c => c.ProductVariant.PriceOverride * c.Quantity);

        if (eligibleSubtotal <= 0)
            return new CouponResult(false, "This coupon doesn't apply to any items currently in your cart.", 0, false, new());

        // NOTE: MinOrderValue is checked against the ELIGIBLE subtotal, not the
        // whole cart — correct for a scoped coupon (e.g. "₹999+ on Rings"
        // should mean 999+ of Rings, not 999+ of anything in the cart).
        if (coupon.MinOrderValue.HasValue && eligibleSubtotal < coupon.MinOrderValue)
            return new CouponResult(false, $"Minimum order value of ₹{coupon.MinOrderValue} required on eligible items.", 0, false, new());

        var discount = coupon.Type switch
        {
            CouponType.PercentOff => Math.Round(eligibleSubtotal * (coupon.Value / 100m), 2),
            CouponType.FlatOff => Math.Min(coupon.Value, eligibleSubtotal), // never discount below zero
            CouponType.FreeShipping => 0,
            _ => 0
        };

        if (coupon.MaxDiscountAmount.HasValue)
            discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);

        return new CouponResult(true, null, discount, coupon.Type == CouponType.FreeShipping, eligibleProductIds);
    }
}
