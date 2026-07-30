using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Data;

// Inherits IdentityDbContext so ASP.NET Core Identity's user/role tables are created
// alongside our own tables in the same SQLServer database.
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // HasData requires a fixed, literal value — never DateTime.UtcNow — or
    // EF's migration diff would regenerate a "new" migration every single
    // time you build, since the seed timestamp would differ each run.
    private static readonly DateTime SeedTimestamp = new(2026, 7, 12, 8, 19, 41, DateTimeKind.Utc);

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
    public DbSet<CouponProduct> CouponProducts => Set<CouponProduct>();
    public DbSet<CouponCategory> CouponCategories => Set<CouponCategory>();
    public DbSet<CouponUsage> CouponUsages => Set<CouponUsage>();
    public DbSet<GiftCard> GiftCards => Set<GiftCard>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();

    public DbSet<BlogCategory> BlogCategories => Set<BlogCategory>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<BulkImportJob> BulkImportJobs => Set<BulkImportJob>();
    public DbSet<BulkImportRow> BulkImportRows => Set<BulkImportRow>();
    public DbSet<HsnTaxRate> HsnTaxRates => Set<HsnTaxRate>();
    public DbSet<PincodePost> PincodePosts => Set<PincodePost>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankAccountRevealLog> BankAccountRevealLogs => Set<BankAccountRevealLog>();

    // UPI ID payout method — sits alongside BankAccounts as the second half
    // of "Payout Methods". No reveal-log table here: unlike a bank account
    // number, a UPI VPA is never masked or encrypted, so there's nothing to
    // audit-log a "reveal" of.
    public DbSet<UpiAccount> UpiAccounts => Set<UpiAccount>();

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
        builder.Entity<HsnTaxRate>().HasIndex(h => h.HsnCode).IsUnique();

        // A coupon can only be scoped to a given product/category once.
        builder.Entity<CouponProduct>().HasIndex(cp => new { cp.CouponId, cp.ProductId }).IsUnique();
        builder.Entity<CouponCategory>().HasIndex(cc => new { cc.CouponId, cc.CategoryId }).IsUnique();
        // Non-unique — a user can redeem the same coupon multiple times up to its limit.
        builder.Entity<CouponUsage>().HasIndex(cu => new { cu.CouponId, cu.UserId });

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

        // Deleting a product/category shouldn't silently cascade-delete a
        // coupon's scope rows — restrict instead, same reasoning as the
        // Address/Category restricts above.
        builder.Entity<CouponProduct>()
            .HasOne(cp => cp.Product)
            .WithMany()
            .HasForeignKey(cp => cp.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CouponCategory>()
            .HasOne(cc => cc.Category)
            .WithMany()
            .HasForeignKey(cc => cc.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deliberate: a coupon that has actually been redeemed can't be
        // deleted (only deactivated) — this keeps the usage/audit trail intact.
        builder.Entity<CouponUsage>()
            .HasOne(cu => cu.Coupon)
            .WithMany()
            .HasForeignKey(cu => cu.CouponId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- Seed a couple of top-level categories so the storefront has data on first run ----
        builder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Earrings", Slug = "earrings", DisplayOrder = 1 },
            new Category { Id = 2, Name = "Pendants", Slug = "pendants", DisplayOrder = 2 },
            new Category { Id = 3, Name = "Bangles", Slug = "bangles", DisplayOrder = 3 },
            new Category { Id = 4, Name = "Rings", Slug = "rings", DisplayOrder = 4 },
            new Category { Id = 5, Name = "Bracelets", Slug = "bracelets", DisplayOrder = 5 },
            new Category { Id = 6, Name = "Combo Sets", Slug = "combo-sets", DisplayOrder = 6 }
        );

        // ---- Seed standard GST HSN codes covering imitation/fashion jewellery, so
        // Product.HsnTaxRateId has real rows to point at from day one ----
        builder.Entity<HsnTaxRate>().HasData(
            new HsnTaxRate { Id = 1, HsnCode = "711711", Description = "Imitation jewellery of base metal, whether or not plated", TaxRatePercent = 3.00m, IsActive = true, CreatedAt = SeedTimestamp },
            new HsnTaxRate { Id = 2, HsnCode = "711790", Description = "Other imitation jewellery", TaxRatePercent = 3.00m, IsActive = true, CreatedAt = SeedTimestamp },
            new HsnTaxRate { Id = 3, HsnCode = "391926", Description = "Fashion accessories of plastics", TaxRatePercent = 12.00m, IsActive = true, CreatedAt = SeedTimestamp },
            new HsnTaxRate { Id = 4, HsnCode = "711719", Description = "Imitation jewellery of base metal, gold/silver plated", TaxRatePercent = 3.00m, IsActive = true, CreatedAt = SeedTimestamp }
        );

        builder.Entity<Product>()
            .HasOne(p => p.HsnTaxRate)
            .WithMany()
            .HasForeignKey(p => p.HsnTaxRateId)
            .OnDelete(DeleteBehavior.Restrict);

        // Occasion/Material/Finish are free text (no lookup table) — cap lengths
        // so they don't default to nvarchar(max).
        builder.Entity<Product>().Property(p => p.Occasion).HasMaxLength(100);
        builder.Entity<Product>().Property(p => p.Material).HasMaxLength(150);
        builder.Entity<Product>().Property(p => p.Finish).HasMaxLength(200);

        // Speeds up "does this user already have an active account" checks
        // and the customer-facing list query (both filter on UserId first).
        builder.Entity<BankAccount>().HasIndex(b => new { b.UserId, b.IsActive });
        builder.Entity<BankAccount>().Property(b => b.IfscCode).HasMaxLength(11);
        builder.Entity<BankAccount>().Property(b => b.AccountNumberLast4).HasMaxLength(4);

        // Same reasoning as BankAccount's index above — this is the query
        // shape both UpiAccountsController.GetAll and the cross-table
        // Activate checks run.
        builder.Entity<UpiAccount>().HasIndex(u => new { u.UserId, u.IsActive });
        builder.Entity<UpiAccount>().Property(u => u.UpiId).HasMaxLength(256);

        // Reveal logs are permanent audit history — deleting a bank account
        // (soft-delete, so this barely matters) or even a user should never
        // silently wipe out who viewed sensitive details and when.
        builder.Entity<BankAccountRevealLog>()
            .HasOne(r => r.BankAccount)
            .WithMany()
            .HasForeignKey(r => r.BankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Non-unique — a single pincode has many post offices.
        builder.Entity<PincodePost>().HasIndex(p => p.Pincode);
        builder.Entity<PincodePost>().Property(p => p.Pincode).HasMaxLength(6);
        builder.Entity<PincodePost>().Property(p => p.District).HasMaxLength(150);
        builder.Entity<PincodePost>().Property(p => p.StateName).HasMaxLength(100);
        builder.Entity<PincodePost>().Property(p => p.OfficeName).HasMaxLength(200);
    }
}