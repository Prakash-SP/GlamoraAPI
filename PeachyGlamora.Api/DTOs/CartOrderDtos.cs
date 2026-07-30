namespace PeachyGlamora.Api.DTOs;

public record AddToCartRequest(int ProductVariantId, int Quantity);
public record UpdateCartItemRequest(int Quantity);

public record CartItemDto(int Id, int ProductVariantId, string ProductName, string ImageUrl,
    string? Color, string? Size, decimal UnitPrice, int Quantity, int AvailableStock);

public record CartSummaryDto(List<CartItemDto> Items, decimal Subtotal, decimal DiscountAmount,
    decimal EstimatedTax, decimal ShippingEstimate, decimal Total, string? AppliedCouponCode,
    string? CouponError);

public record ApplyCouponRequest(string Code);

public record CheckoutRequest(
    int ShippingAddressId,
    int BillingAddressId,
    string PaymentMethod,      // "UPI" | "Card" | "NetBanking" | "Wallet" | "CashOnDelivery" | "GiftCard"
    string? CouponCode,
    string? GiftCardCode);

public record OrderConfirmationDto(string OrderNumber, decimal TotalAmount, string Status,
    DateTime EstimatedDeliveryDate, string? UpiIntentUri, string? UpiQrCodeBase64Png);

public record PincodeCheckResponse(bool IsServiceable, int EstimatedDeliveryDays, bool CodAvailable);
