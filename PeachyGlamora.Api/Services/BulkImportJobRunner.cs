using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Services;

// Registered as a scoped service in Program.cs (builder.Services.AddScoped<BulkImportJobRunner>())
// — Hangfire's ASP.NET Core integration creates a fresh DI scope per job
// execution automatically, so a normal scoped AppDbContext injection here
// works exactly like it does in a controller.
public class BulkImportJobRunner
{
    private readonly AppDbContext _db;
    public BulkImportJobRunner(AppDbContext db) => _db = db;

    public async Task RunAsync(int jobId)
    {
        var job = await _db.BulkImportJobs.Include(j => j.Rows).FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null) return; // job was deleted mid-flight — nothing to do

        // Only rows that passed validation get imported — Error rows are
        // skipped entirely and stay at their original ImportStatus (Pending),
        // which the frontend treats as "not attempted."
        var importable = job.Rows.Where(r => r.Status != BulkImportRowStatus.Error).OrderBy(r => r.RowNumber).ToList();

        foreach (var row in importable)
        {
            try
            {
                var product = new Product
                {
                    Name = row.ProductName,
                    Slug = row.Slug,
                    Description = row.Description ?? "",
                    ShortDescription = row.ShortDescription ?? "",
                    CategoryId = row.CategoryId!.Value,
                    Occasion = row.Occasion!,
                    Material = row.Material!,
                    Finish = row.Finish,
                    BasePrice = row.BasePrice!.Value,
                    CompareAtPrice = row.CompareAtPrice,
                    HsnTaxRateId = row.HsnTaxRateId!.Value,
                    IsNewArrival = row.IsNewArrival,
                    IsBestSeller = row.IsBestSeller,
                    IsTrending = row.IsTrending,
                    IsFeatured = row.IsFeatured,
                    IsActive = row.IsActive,
                };
                _db.Products.Add(product);
                await _db.SaveChangesAsync(); // need product.Id before adding variant/image below

                if (!string.IsNullOrWhiteSpace(row.Sku))
                {
                    _db.ProductVariants.Add(new ProductVariant
                    {
                        ProductId = product.Id,
                        Sku = row.Sku,
                        Color = row.Color,
                        Size = row.Size,
                        PriceOverride = row.BasePrice!.Value,
                        StockQuantity = row.StockQuantity ?? 0,
                        IsDefault = true,
                    });
                }

                if (!string.IsNullOrWhiteSpace(row.ImageUrl))
                {
                    _db.ProductImages.Add(new ProductImage
                    {
                        ProductId = product.Id,
                        Url = row.ImageUrl,
                        AltText = row.ProductName,
                        DisplayOrder = 0,
                        IsPrimary = true,
                    });
                }

                await _db.SaveChangesAsync();

                row.ImportStatus = BulkImportRowImportStatus.Imported;
                row.CreatedProductId = product.Id;
                job.ImportedRecords++;
            }
            catch (Exception ex)
            {
                row.ImportStatus = BulkImportRowImportStatus.Failed;
                row.ImportError = ex.Message;
                job.FailedRecords++;
            }

            // Save progress after every row (not just at the end) so the
            // frontend's polling GET /bulk-import/{jobId} reflects real-time
            // progress instead of jumping from 0% to 100% at the very end.
            await _db.SaveChangesAsync();
        }

        job.Status = job.FailedRecords == 0
            ? BulkImportJobStatus.Completed
            : job.ImportedRecords == 0
                ? BulkImportJobStatus.Failed
                : BulkImportJobStatus.PartiallyCompleted;
        job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
