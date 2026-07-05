using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Data;

// Inherits IdentityDbContext so ASP.NET Core Identity's user/role tables are created
// alongside our own tables in the same SQLServer database.
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ProductQuestion> ProductQuestions => Set<ProductQuestion>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<RecentlyViewed> RecentlyViewedItems => Set<RecentlyViewed>();

    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportTicketReply> SupportTicketReplies => Set<SupportTicketReply>();
    public DbSet<AppNotification> Notifications => Set<AppNotification>();

    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();

    public DbSet<BlogCategory> BlogCategories => Set<BlogCategory>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // keep Identity's own configuration

        // ---- Uniqueness constraints ----
        builder.Entity<Category>().HasIndex(c => c.Slug).IsUnique();
        builder.Entity<Product>().HasIndex(p => p.Slug).IsUnique();
        builder.Entity<ProductVariant>().HasIndex(v => v.Sku).IsUnique();
        builder.Entity<Coupon>().HasIndex(c => c.Code).IsUnique();
        builder.Entity<GiftCard>().HasIndex(g => g.Code).IsUnique();
        builder.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        builder.Entity<BlogPost>().HasIndex(b => b.Slug).IsUnique();

        // ---- Decimal precision (avoid silent rounding on money columns) ----
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetColumnType("decimal(12,2)");
        }

        // ---- Prevent cascade-delete cycles (SQL Server/Postgres both reject multiple
        // cascade paths to the same table) by restricting non-primary relationships ----
        builder.Entity<Order>()
            .HasOne(o => o.ShippingAddress)
            .WithMany()
            .HasForeignKey(o => o.ShippingAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Order>()
            .HasOne(o => o.BillingAddress)
            .WithMany()
            .HasForeignKey(o => o.BillingAddressId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Category>()
            .HasOne(c => c.ParentCategory)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Payment>()
            .HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId);

        // ---- Seed a couple of top-level categories so the storefront has data on first run ----
        builder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Earrings", Slug = "earrings", DisplayOrder = 1 },
            new Category { Id = 2, Name = "Necklace Sets", Slug = "necklace-sets", DisplayOrder = 2 },
            new Category { Id = 3, Name = "Bangles", Slug = "bangles", DisplayOrder = 3 },
            new Category { Id = 4, Name = "Rings", Slug = "rings", DisplayOrder = 4 },
            new Category { Id = 5, Name = "Bracelets", Slug = "bracelets", DisplayOrder = 5 },
            new Category { Id = 6, Name = "Combo Sets", Slug = "combo-sets", DisplayOrder = 6 }
        );
    }
}
