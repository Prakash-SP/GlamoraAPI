using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;

namespace PeachyGlamora.Api.Services;

public interface IRestockNotificationService
{
    /// <summary>Emails every user who has this product in their wishlist. Called via
    /// Hangfire, fire-and-forget, whenever AdminProductsController detects a product
    /// going from zero total stock (across all variants) to having stock again.</summary>
    Task NotifyWishlistersAsync(int productId);
}

public class RestockNotificationService : IRestockNotificationService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;

    public RestockNotificationService(AppDbContext db, IEmailService email, IConfiguration config)
    {
        _db = db;
        _email = email;
        _config = config;
    }

    public async Task NotifyWishlistersAsync(int productId)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return;

        // Re-check here too (not just at the call site) — by the time this Hangfire
        // job actually runs, a later admin edit could have already sold the restock
        // back down to zero. No point emailing "back in stock" for something that
        // isn't, right now, actually in stock.
        var stillInStock = await _db.ProductVariants
            .Where(v => v.ProductId == productId)
            .AnyAsync(v => v.StockQuantity > 0);
        if (!stillInStock) return;

        var wishlisters = await _db.WishlistItems
            .Include(w => w.User)
            .Where(w => w.ProductId == productId)
            .Select(w => new { w.User.Email, w.User.FullName })
            .Distinct()
            .ToListAsync();

        if (wishlisters.Count == 0) return;

        // TODO: set "Frontend:BaseUrl" in appsettings (falls back to production
        // domain below) — same reasoning as the Smtp:* config keys in
        // NotificationServices.cs, just not wired up in appsettings yet.
        var baseUrl = _config["Frontend:BaseUrl"] ?? "https://peachyglamora.com";
        var productUrl = $"{baseUrl}/product/{product.Slug}";

        foreach (var user in wishlisters)
        {
            if (string.IsNullOrWhiteSpace(user.Email)) continue;

            var html = $@"
                <p>Hi {user.FullName},</p>
                <p><strong>{product.Name}</strong> is back in stock — it's still sitting in your wishlist,
                so grab it before it sells out again.</p>
                <p><a href=""{productUrl}"">View {product.Name}</a></p>";

            // Same reasoning as SmtpEmailService's own try/catch: a failed send here
            // should never stop the rest of the wishlist from being notified.
            await _email.SendAsync(user.Email, $"{product.Name} is back in stock!", html);
        }
    }
}
