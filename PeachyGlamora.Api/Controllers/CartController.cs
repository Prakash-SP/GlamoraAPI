using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeachyGlamora.Api.DTOs;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/cart")]
public class CartController : ControllerBase
{
    private readonly ICartService _cart;
    public CartController(ICartService cart) => _cart = cart;

    // Guests are identified by an X-Guest-Cart-Id header the frontend generates and
    // persists in a cookie; logged-in users are identified by their JWT's sub claim instead.
    private string? UserId => User.Identity?.IsAuthenticated == true
        ? User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        : null;
    private string? GuestCartId => Request.Headers["X-Guest-Cart-Id"].FirstOrDefault();

    [HttpGet]
    public async Task<IActionResult> GetCart([FromQuery] string? coupon)
        => Ok(await _cart.GetCartAsync(UserId, GuestCartId, coupon));

    [HttpPost("items")]
    public async Task<IActionResult> AddItem(AddToCartRequest req)
    {
        try { await _cart.AddItemAsync(UserId, GuestCartId, req); return Ok(await _cart.GetCartAsync(UserId, GuestCartId)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> UpdateItem(int id, UpdateCartItemRequest req)
    {
        try { await _cart.UpdateItemAsync(UserId, GuestCartId, id, req.Quantity); return Ok(await _cart.GetCartAsync(UserId, GuestCartId)); }
        catch (InvalidOperationException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> RemoveItem(int id)
    {
        await _cart.RemoveItemAsync(UserId, GuestCartId, id);
        return Ok(await _cart.GetCartAsync(UserId, GuestCartId));
    }

    [HttpPost("coupon")]
    public async Task<IActionResult> ApplyCoupon(ApplyCouponRequest req)
    {
        // GetCartAsync already runs ValidateCouponAsync internally (it needs the
        // actual CartItem entities with ProductVariant/Category included, which
        // aren't available here) and surfaces the result as CouponError on the
        // summary — so just read that instead of re-validating in the controller.
        var cartSummary = await _cart.GetCartAsync(UserId, GuestCartId, req.Code);
        if (cartSummary.CouponError != null) return BadRequest(new { error = cartSummary.CouponError });
        return Ok(cartSummary);
    }

    // Simple pincode serviceability check for the delivery-estimate box on the PDP/cart.
    [HttpGet("~/api/shipping/check-pincode/{pincode}")]
    [AllowAnonymous]
    public IActionResult CheckPincode(string pincode)
    {
        var isValid = pincode.Length == 6 && pincode.All(char.IsDigit);
        return Ok(new PincodeCheckResponse(isValid, isValid ? 4 : 0, isValid));
    }
}
