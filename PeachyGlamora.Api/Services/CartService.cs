using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Services;

public interface ICartService
{
    Task<CartSummaryDto> GetCartAsync(string? userId, string? guestCartId, string? couponCode = null);
    Task AddItemAsync(string? userId, string? guestCartId, AddToCartRequest req);
    Task UpdateItemAsync(string? userId, string? guestCartId, int cartItemId, int quantity);
    Task RemoveItemAsync(string? userId, string? guestCartId, int cartItemId);
    Task<(bool valid, string? error, decimal discount)> ValidateCouponAsync(string code, string? userId, decimal subtotal);
}

public class CartService : ICartService
{
    private readonly AppDbContext _db;
    private const decimal FreeShippingThreshold = 999m;
    private const decimal StandardShippingFee = 79m;

    public CartService(AppDbContext db) => _db = db;

    private IQueryable<CartItem> ScopedItems(string? userId, string? guestCartId) =>
        _db.CartItems
            .Include(c => c.ProductVariant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
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
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var (valid, _, calcDiscount) = await ValidateCouponAsync(couponCode, userId, subtotal);
            if (valid) discount = calcDiscount;
        }

        var taxableAmount = subtotal - discount;
        // Simplified: assumes a blended 3% GST slab typical for artificial/imitation jewellery in India.
        var tax = Math.Round(taxableAmount * 0.03m, 2);
        var shipping = (subtotal - discount) >= FreeShippingThreshold || subtotal == 0 ? 0 : StandardShippingFee;
        var total = taxableAmount + tax + shipping;

        return new CartSummaryDto(dtoItems, subtotal, discount, tax, shipping, total, couponCode);
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

    public async Task<(bool valid, string? error, decimal discount)> ValidateCouponAsync(string code, string? userId, decimal subtotal)
    {
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code == code.ToUpper() && c.IsActive);
        if (coupon == null) return (false, "Invalid coupon code.", 0);
        if (DateTime.UtcNow < coupon.ValidFrom || DateTime.UtcNow > coupon.ValidTo)
            return (false, "This coupon has expired.", 0);
        if (coupon.MinOrderValue.HasValue && subtotal < coupon.MinOrderValue)
            return (false, $"Minimum order value of ₹{coupon.MinOrderValue} required.", 0);
        if (coupon.TotalUsageLimit.HasValue && coupon.TimesUsed >= coupon.TotalUsageLimit)
            return (false, "This coupon has reached its usage limit.", 0);

        var discount = coupon.Type switch
        {
            CouponType.PercentOff => Math.Round(subtotal * (coupon.Value / 100m), 2),
            CouponType.FlatOff => coupon.Value,
            CouponType.FreeShipping => 0, // shipping waived separately at checkout
            _ => 0
        };

        if (coupon.MaxDiscountAmount.HasValue)
            discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);

        return (true, null, discount);
    }
}
