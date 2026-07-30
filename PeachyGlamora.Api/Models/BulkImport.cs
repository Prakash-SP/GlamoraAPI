namespace PeachyGlamora.Api.Models;

public enum BulkImportJobStatus { Validated, Importing, Completed, PartiallyCompleted, Failed, Cancelled }
public enum BulkImportRowStatus { Valid, Warning, Error }
public enum BulkImportRowImportStatus { Pending, Imported, Failed }

public class BulkImportJob
{
    public int Id { get; set; }
    public string FileName { get; set; } = default!;

    public string UploadedByUserId { get; set; } = default!;
    public ApplicationUser UploadedBy { get; set; } = default!;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public int TotalRecords { get; set; }
    public int ValidRecords { get; set; }
    public int InvalidRecords { get; set; }
    public int DuplicateRecords { get; set; }
    public int WarningRecords { get; set; }

    public int ImportedRecords { get; set; }
    public int FailedRecords { get; set; }

    public BulkImportJobStatus Status { get; set; } = BulkImportJobStatus.Validated;
    public DateTime? CompletedAt { get; set; }

    public ICollection<BulkImportRow> Rows { get; set; } = new List<BulkImportRow>();
}

public class BulkImportRow
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public BulkImportJob Job { get; set; } = default!;

    public int RowNumber { get; set; }
    public BulkImportRowStatus Status { get; set; }
    public bool IsDuplicate { get; set; }

    // Stored as "|"-joined strings rather than a separate messages table —
    // simple, and these are only ever displayed, never queried individually.
    public string? Errors { get; set; }
    public string? Warnings { get; set; }

    public string ProductName { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public int? CategoryId { get; set; }
    public string? Occasion { get; set; }
    public string? Material { get; set; }
    public string? Finish { get; set; }
    public decimal? BasePrice { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public string? HsnCode { get; set; }
    public int? HsnTaxRateId { get; set; }
    public string? Sku { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public int? StockQuantity { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsNewArrival { get; set; }
    public bool IsBestSeller { get; set; }
    public bool IsTrending { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;

    public BulkImportRowImportStatus ImportStatus { get; set; } = BulkImportRowImportStatus.Pending;
    public string? ImportError { get; set; }
    public int? CreatedProductId { get; set; }
}
