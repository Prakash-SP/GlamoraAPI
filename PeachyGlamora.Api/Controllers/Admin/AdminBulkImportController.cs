using ClosedXML.Excel;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;
using PeachyGlamora.Api.Services;

namespace PeachyGlamora.Api.Controllers.Admin;

// NOTE: requires the ClosedXML NuGet package (free, MIT-licensed .xlsx reader/writer):
//   dotnet add package ClosedXML
[ApiController]
[Route("api/admin/products/bulk-import")]
[Authorize(Roles = "Admin")]
public class AdminBulkImportController : ControllerBase
{
    private readonly AppDbContext _db;

    // Occasion/Material are now free text — admins can type or import any value,
    // no fixed allowlist. (Previously hardcoded here; removed per product decision
    // to make these fully dynamic.)

    // Exact column order the frontend's downloadable template uses — kept as
    // a single source of truth here since parsing depends on it by index.
    private static readonly string[] Columns =
    {
        "Product Name", "Slug", "Short Description", "Description", "Category",
        "Occasion", "Material", "Finish", "Base Price", "Compare At Price", "HSN Code",
        "SKU", "Color", "Size", "Stock Quantity", "Image URL",
        "Is New Arrival", "Is Best Seller", "Is Trending", "Is Featured", "Is Active",
    };

    public AdminBulkImportController(AppDbContext db) => _db = db;

    // ---------- 1. Upload + validate (does NOT create any products yet) ----------
    [HttpPost("validate")]
    [RequestSizeLimit(10_000_000)] // 10 MB, matches the frontend's stated limit
    public async Task<IActionResult> Validate(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file was uploaded." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .xlsx files are supported." });

        var userId = User.FindFirst("sub")!.Value;

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        List<Dictionary<string, string>> rawRows;
        try
        {
            rawRows = ParseWorkbook(stream);
        }
        catch (Exception)
        {
            return BadRequest(new { error = "Could not read this file — make sure it matches the template format." });
        }

        if (rawRows.Count == 0) return BadRequest(new { error = "This file has no data rows to import." });

        var categories = await _db.Categories.ToListAsync();
        var hsnRates = await _db.HsnTaxRates.Where(h => h.IsActive).ToListAsync();
        var existingSlugs = new HashSet<string>(await _db.Products.Select(p => p.Slug.ToLower()).ToListAsync());
        var existingNames = new HashSet<string>(await _db.Products.Select(p => p.Name.ToLower()).ToListAsync());
        var existingSkus = new HashSet<string>(await _db.ProductVariants.Select(v => v.Sku.ToLower()).ToListAsync());

        var job = new BulkImportJob
        {
            FileName = file.FileName,
            UploadedByUserId = userId,
            TotalRecords = rawRows.Count,
        };

        var seenSlugs = new HashSet<string>();
        var seenSkus = new HashSet<string>();
        var seenNames = new HashSet<string>();

        foreach (var (raw, index) in rawRows.Select((r, i) => (r, i)))
        {
            var row = ValidateRow(raw, index + 2, categories, hsnRates, existingSlugs, existingNames, existingSkus, seenSlugs, seenSkus, seenNames);
            job.Rows.Add(row);
        }

        job.ValidRecords = job.Rows.Count(r => r.Status != BulkImportRowStatus.Error);
        job.InvalidRecords = job.Rows.Count(r => r.Status == BulkImportRowStatus.Error);
        job.DuplicateRecords = job.Rows.Count(r => r.IsDuplicate);
        job.WarningRecords = job.Rows.Count(r => r.Status == BulkImportRowStatus.Warning);

        _db.BulkImportJobs.Add(job);
        await _db.SaveChangesAsync();

        return Ok(await ProjectJobDetail(job.Id));
    }

    // ---------- 2. Kick off the actual import (Hangfire background job — durable,
    //              survives the browser tab closing since jobs persist in SQL Server
    //              storage, same Hangfire setup already used for cart/reminder jobs) ----------
    [HttpPost("{jobId:int}/execute")]
    public async Task<IActionResult> Execute(int jobId)
    {
        var job = await _db.BulkImportJobs.FindAsync(jobId);
        if (job == null) return NotFound();
        if (job.Status != BulkImportJobStatus.Validated)
            return BadRequest(new { error = $"This job is already {job.Status} and cannot be re-executed." });

        job.Status = BulkImportJobStatus.Importing;
        await _db.SaveChangesAsync();

        BackgroundJob.Enqueue<BulkImportJobRunner>(runner => runner.RunAsync(jobId));

        return Ok(new { message = "Import started.", jobId });
    }

    // ---------- 3. Poll job status/progress ----------
    [HttpGet("{jobId:int}")]
    public async Task<IActionResult> GetJob(int jobId)
    {
        var job = await ProjectJobDetail(jobId);
        return job == null ? NotFound() : Ok(job);
    }

    // ---------- 4. Row-level detail (optionally filtered) ----------
    [HttpGet("{jobId:int}/rows")]
    public async Task<IActionResult> GetRows(int jobId, [FromQuery] string? status)
    {
        if (!await _db.BulkImportJobs.AnyAsync(j => j.Id == jobId)) return NotFound();

        var query = _db.BulkImportRows.Where(r => r.JobId == jobId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BulkImportRowStatus>(status, true, out var parsedStatus))
            query = query.Where(r => r.Status == parsedStatus);

        // .Split() can't be translated into SQL, so project the raw scalar
        // columns first (this runs as SQL), then split Errors/Warnings into
        // arrays afterward, in memory, once the rows are already materialized.
        var raw = await query.OrderBy(r => r.RowNumber).Select(r => new RowRawDto(
            r.Id, r.RowNumber, r.Status, r.IsDuplicate, r.Errors, r.Warnings,
            r.ProductName, r.Slug, r.CategoryName, r.Occasion, r.Material, r.Finish, r.Sku,
            r.BasePrice, r.HsnCode, r.StockQuantity, r.ImportStatus, r.ImportError, r.CreatedProductId
        )).ToListAsync();

        return Ok(raw.Select(ToRowDto));
    }

    // ---------- 5. Paginated history ----------
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var query = _db.BulkImportJobs.OrderByDescending(j => j.UploadedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new
            {
                j.Id,
                j.FileName,
                UploadedByName = j.UploadedBy.FullName,
                j.UploadedAt,
                j.TotalRecords,
                j.ImportedRecords,
                j.FailedRecords,
                j.Status,
                j.CompletedAt,
            }).ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    // ---------- 6. Downloadable error report ----------
    [HttpGet("{jobId:int}/error-report")]
    public async Task<IActionResult> DownloadErrorReport(int jobId)
    {
        var rows = await _db.BulkImportRows
            .Where(r => r.JobId == jobId && (r.Status == BulkImportRowStatus.Error || r.ImportStatus == BulkImportRowImportStatus.Failed))
            .OrderBy(r => r.RowNumber)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Errors");
        string[] headers = { "Row", "Status", "Import Status", "Errors", "Warnings", "Product Name", "Slug", "SKU", "Price", "Stock" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var rowIndex = i + 2;
            sheet.Cell(rowIndex, 1).Value = r.RowNumber;
            sheet.Cell(rowIndex, 2).Value = r.Status.ToString();
            sheet.Cell(rowIndex, 3).Value = r.ImportError ?? r.ImportStatus.ToString();
            sheet.Cell(rowIndex, 4).Value = r.Errors ?? "";
            sheet.Cell(rowIndex, 5).Value = r.Warnings ?? "";
            sheet.Cell(rowIndex, 6).Value = r.ProductName;
            sheet.Cell(rowIndex, 7).Value = r.Slug;
            sheet.Cell(rowIndex, 8).Value = r.Sku ?? "";
            sheet.Cell(rowIndex, 9).Value = r.BasePrice?.ToString() ?? "";
            sheet.Cell(rowIndex, 10).Value = r.StockQuantity?.ToString() ?? "";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"import-errors-job-{jobId}.xlsx");
    }

    // ================== helpers ==================

    private List<Dictionary<string, string>> ParseWorkbook(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        var rows = new List<Dictionary<string, string>>();

        var usedRange = sheet.RangeUsed();
        if (usedRange == null) return rows;

        var lastRow = usedRange.LastRow().RowNumber();
        for (var r = 2; r <= lastRow; r++) // row 1 = headers
        {
            var dict = new Dictionary<string, string>();
            for (var c = 0; c < Columns.Length; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                dict[Columns[c]] = cell.IsEmpty() ? "" : cell.GetString().Trim();
            }
            // Skip fully blank rows (e.g. trailing empty rows Excel sometimes includes).
            if (dict.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(dict);
        }
        return rows;
    }

    private static BulkImportRow ValidateRow(
        Dictionary<string, string> raw, int rowNumber, List<Category> categories, List<HsnTaxRate> hsnRates,
        HashSet<string> existingSlugs, HashSet<string> existingNames, HashSet<string> existingSkus,
        HashSet<string> seenSlugs, HashSet<string> seenSkus, HashSet<string> seenNames)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        string Get(string col) => raw.TryGetValue(col, out var v) ? v : "";
        bool GetBool(string col) => string.Equals(Get(col), "TRUE", StringComparison.OrdinalIgnoreCase);
        decimal? GetDecimal(string col) => decimal.TryParse(Get(col), out var d) ? d : null;
        int? GetInt(string col) => int.TryParse(Get(col), out var i) ? i : null;

        var name = Get("Product Name");
        var slug = Get("Slug");
        var shortDesc = Get("Short Description");
        var description = Get("Description");
        var categoryName = Get("Category");
        var occasion = Get("Occasion");
        var material = Get("Material");
        var finish = Get("Finish");
        var basePrice = GetDecimal("Base Price");
        var compareAtPrice = string.IsNullOrWhiteSpace(Get("Compare At Price")) ? (decimal?)null : GetDecimal("Compare At Price");
        var hsnCode = Get("HSN Code");
        var sku = Get("SKU");
        var color = Get("Color");
        var size = Get("Size");
        var stock = GetInt("Stock Quantity");
        var imageUrl = Get("Image URL");

        if (string.IsNullOrWhiteSpace(name)) errors.Add("Product Name is required.");
        if (name.Length > 200) errors.Add("Product Name exceeds 200 characters.");
        if (string.IsNullOrWhiteSpace(slug)) errors.Add("Slug is required.");
        else if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            errors.Add("Slug must be lowercase, alphanumeric, hyphen-separated.");
        if (string.IsNullOrWhiteSpace(shortDesc)) errors.Add("Short Description is required.");
        if (string.IsNullOrWhiteSpace(description)) errors.Add("Description is required.");
        if (string.IsNullOrWhiteSpace(sku)) errors.Add("SKU is required.");
        if (stock == null) errors.Add("Stock Quantity is required and must be a whole number.");
        else if (stock < 0) errors.Add("Stock Quantity cannot be negative.");

        var matchedCategory = categories.FirstOrDefault(c => string.Equals(c.Name.Trim(), categoryName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(categoryName)) errors.Add("Category is required.");
        else if (matchedCategory == null) errors.Add($"Category \"{categoryName}\" does not match any existing category.");

        // Free text now — any non-empty value is accepted, no allowlist.
        if (string.IsNullOrWhiteSpace(occasion)) errors.Add("Occasion is required.");
        if (string.IsNullOrWhiteSpace(material)) errors.Add("Material is required.");
        if (string.IsNullOrWhiteSpace(finish))
            warnings.Add("No Finish provided — product will be created without a finish description.");

        if (basePrice == null) errors.Add("Base Price is required and must be a number.");
        else if (basePrice <= 0) errors.Add("Base Price must be greater than 0.");
        if (compareAtPrice != null && basePrice != null && compareAtPrice <= basePrice)
            warnings.Add("Compare At Price is not greater than Base Price — discount will show as 0 or negative.");

        var matchedHsnRate = hsnRates.FirstOrDefault(h => string.Equals(h.HsnCode.Trim(), hsnCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(hsnCode)) errors.Add("HSN Code is required.");
        else if (matchedHsnRate == null) errors.Add($"HSN Code \"{hsnCode}\" does not match any existing HSN tax rate entry.");

        if (!string.IsNullOrWhiteSpace(imageUrl) && !System.Text.RegularExpressions.Regex.IsMatch(imageUrl, "^https?://.+"))
            errors.Add("Image URL must start with http:// or https://.");
        if (string.IsNullOrWhiteSpace(imageUrl))
            warnings.Add("No Image URL provided — product will be created without an image.");

        var isDuplicate = false;
        var slugKey = slug.ToLower();
        var skuKey = sku.ToLower();
        var nameKey = name.ToLower();

        if (!string.IsNullOrWhiteSpace(slug))
        {
            if (!seenSlugs.Add(slugKey)) { errors.Add("Duplicate Slug within this file."); isDuplicate = true; }
            if (existingSlugs.Contains(slugKey)) { errors.Add("A product with this Slug already exists in the catalog."); isDuplicate = true; }
        }
        if (!string.IsNullOrWhiteSpace(sku))
        {
            if (!seenSkus.Add(skuKey)) { errors.Add("Duplicate SKU within this file."); isDuplicate = true; }
            if (existingSkus.Contains(skuKey)) { errors.Add("A variant with this SKU already exists in the catalog."); isDuplicate = true; }
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (!seenNames.Add(nameKey)) { errors.Add("Duplicate Product Name within this file."); isDuplicate = true; }
            if (existingNames.Contains(nameKey)) { errors.Add("A product with this Name already exists in the catalog."); isDuplicate = true; }
        }

        var status = errors.Count > 0 ? BulkImportRowStatus.Error
            : warnings.Count > 0 ? BulkImportRowStatus.Warning
            : BulkImportRowStatus.Valid;

        return new BulkImportRow
        {
            RowNumber = rowNumber,
            Status = status,
            IsDuplicate = isDuplicate,
            Errors = errors.Count > 0 ? string.Join(" | ", errors) : null,
            Warnings = warnings.Count > 0 ? string.Join(" | ", warnings) : null,
            ProductName = name,
            Slug = slug,
            ShortDescription = shortDesc,
            Description = description,
            CategoryName = categoryName,
            CategoryId = matchedCategory?.Id,
            Occasion = occasion,
            Material = material,
            Finish = string.IsNullOrWhiteSpace(finish) ? null : finish,
            BasePrice = basePrice,
            CompareAtPrice = compareAtPrice,
            HsnCode = hsnCode,
            HsnTaxRateId = matchedHsnRate?.Id,
            Sku = sku,
            Color = string.IsNullOrWhiteSpace(color) ? null : color,
            Size = string.IsNullOrWhiteSpace(size) ? null : size,
            StockQuantity = stock,
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
            IsNewArrival = GetBool("Is New Arrival"),
            IsBestSeller = GetBool("Is Best Seller"),
            IsTrending = GetBool("Is Trending"),
            IsFeatured = GetBool("Is Featured"),
            IsActive = string.IsNullOrWhiteSpace(Get("Is Active")) || GetBool("Is Active"),
        };
    }

    private async Task<object?> ProjectJobDetail(int jobId)
    {
        var raw = await _db.BulkImportJobs.Where(j => j.Id == jobId).Select(j => new
        {
            j.Id,
            j.FileName,
            UploadedByName = j.UploadedBy.FullName,
            j.UploadedAt,
            j.TotalRecords,
            j.ValidRecords,
            j.InvalidRecords,
            j.DuplicateRecords,
            j.WarningRecords,
            j.ImportedRecords,
            j.FailedRecords,
            j.Status,
            j.CompletedAt,
            Rows = j.Rows.OrderBy(r => r.RowNumber).Select(r => new RowRawDto(
                r.Id, r.RowNumber, r.Status, r.IsDuplicate, r.Errors, r.Warnings,
                r.ProductName, r.Slug, r.CategoryName, r.Occasion, r.Material, r.Finish, r.Sku,
                r.BasePrice, r.HsnCode, r.StockQuantity, r.ImportStatus, r.ImportError, r.CreatedProductId
            )),
        }).FirstOrDefaultAsync();

        if (raw == null) return null;

        return new
        {
            raw.Id,
            raw.FileName,
            raw.UploadedByName,
            raw.UploadedAt,
            raw.TotalRecords,
            raw.ValidRecords,
            raw.InvalidRecords,
            raw.DuplicateRecords,
            raw.WarningRecords,
            raw.ImportedRecords,
            raw.FailedRecords,
            raw.Status,
            raw.CompletedAt,
            Rows = raw.Rows.Select(ToRowDto),
        };
    }

    private record RowRawDto(
        int Id, int RowNumber, BulkImportRowStatus Status, bool IsDuplicate, string? Errors, string? Warnings,
        string ProductName, string Slug, string? CategoryName, string? Occasion, string? Material, string? Finish, string? Sku,
        decimal? BasePrice, string? HsnCode, int? StockQuantity, BulkImportRowImportStatus ImportStatus, string? ImportError, int? CreatedProductId);

    private static object ToRowDto(RowRawDto r) => new
    {
        r.Id,
        r.RowNumber,
        r.Status,
        r.IsDuplicate,
        Errors = r.Errors?.Split(" | ", StringSplitOptions.None) ?? Array.Empty<string>(),
        Warnings = r.Warnings?.Split(" | ", StringSplitOptions.None) ?? Array.Empty<string>(),
        r.ProductName,
        r.Slug,
        r.CategoryName,
        r.Occasion,
        r.Material,
        r.Finish,
        r.Sku,
        r.BasePrice,
        r.HsnCode,
        r.StockQuantity,
        r.ImportStatus,
        r.ImportError,
        r.CreatedProductId,
    };
}
