using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PeachyGlamora.Api.Services;

public interface IInvoicePdfService
{
    // Returns null if no matching order is found for this user (controller
    // turns that into a 404 — the service itself doesn't know about HTTP).
    Task<byte[]?> GenerateInvoicePdfAsync(string orderNumber, string userId);
}

public class InvoicePdfService : IInvoicePdfService
{
    private readonly AppDbContext _db;
    public InvoicePdfService(AppDbContext db) => _db = db;

    // ---------- Business details ----------
    // TODO: replace every value below with the real registered business
    // details before going live — these are placeholders only.
    private const string CompanyName = "Peachy Glamora";
    private const string CompanyAddress = "Saket, New Delhi";
    private const string CompanyGstin = "";
    private const string CompanyEmail = "peachyglamora@gmail.com";
    private const string CompanyPhone = "9021559122";

    private record InvoiceLineItem(string ProductName, decimal UnitPrice, int Quantity);

    private record InvoiceData(
        string OrderNumber,
        DateTime CreatedAt,
        string CustomerName,
        string? CustomerEmail,
        string ShippingFullName,
        string ShippingPhone,
        string ShippingLine1,
        string? ShippingLine2,
        string ShippingCity,
        string ShippingState,
        string ShippingPincode,
        decimal Subtotal,
        decimal DiscountAmount,
        decimal TaxAmount,
        decimal ShippingAmount,
        decimal TotalAmount,
        string? CouponCode,
        List<InvoiceLineItem> Items);

    public async Task<byte[]?> GenerateInvoicePdfAsync(string orderNumber, string userId)
    {
        var data = await _db.Orders
            .Where(o => o.OrderNumber == orderNumber && o.UserId == userId)
            .Select(o => new InvoiceData(
                o.OrderNumber,
                o.CreatedAt,
                o.User.FullName,
                o.User.Email,
                o.ShippingAddress.FullName,
                o.ShippingAddress.Phone,
                o.ShippingAddress.Line1,
                o.ShippingAddress.Line2,
                o.ShippingAddress.City,
                o.ShippingAddress.State,
                o.ShippingAddress.Pincode,
                o.Subtotal,
                o.DiscountAmount,
                o.TaxAmount,
                o.ShippingAmount,
                o.TotalAmount,
                o.CouponCode,
                o.Items
                    .Select(i => new InvoiceLineItem(i.ProductNameSnapshot, i.UnitPriceSnapshot, i.Quantity))
                    .ToList()))
            .FirstOrDefaultAsync();

        return data == null ? null : Render(data);
    }

    private static byte[] Render(InvoiceData d)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(CompanyName).FontSize(18).Bold();
                            c.Item().Text(CompanyAddress).FontSize(9);
                            c.Item().Text($"GSTIN: {CompanyGstin}").FontSize(9);
                            c.Item().Text($"{CompanyEmail} · {CompanyPhone}").FontSize(9);
                        });
                        row.ConstantItem(160).Column(c =>
                        {
                            c.Item().AlignRight().Text("TAX INVOICE").FontSize(14).Bold();
                            c.Item().AlignRight().Text($"Order #{d.OrderNumber}").FontSize(10);
                            c.Item().AlignRight().Text($"Date: {d.CreatedAt:dd MMM yyyy}").FontSize(10);
                        });
                    });
                    col.Item().PaddingTop(10).LineHorizontal(1);
                });

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Billed To").Bold();
                            c.Item().Text(d.CustomerName);
                            if (!string.IsNullOrWhiteSpace(d.CustomerEmail))
                                c.Item().Text(d.CustomerEmail);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Shipping Address").Bold();
                            c.Item().Text(d.ShippingFullName);
                            c.Item().Text(d.ShippingLine1 + (string.IsNullOrWhiteSpace(d.ShippingLine2) ? "" : ", " + d.ShippingLine2));
                            c.Item().Text($"{d.ShippingCity}, {d.ShippingState} - {d.ShippingPincode}");
                            c.Item().Text(d.ShippingPhone);
                        });
                    });

                    col.Item().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Item").Bold();
                            header.Cell().AlignCenter().Text("Qty").Bold();
                            header.Cell().AlignRight().Text("Unit Price").Bold();
                            header.Cell().AlignRight().Text("Amount").Bold();
                            header.Cell().ColumnSpan(4).PaddingTop(4).LineHorizontal(1);
                        });

                        foreach (var item in d.Items)
                        {
                            table.Cell().PaddingVertical(4).Text(item.ProductName);
                            table.Cell().PaddingVertical(4).AlignCenter().Text(item.Quantity.ToString());
                            table.Cell().PaddingVertical(4).AlignRight().Text($"₹{item.UnitPrice:N2}");
                            table.Cell().PaddingVertical(4).AlignRight().Text($"₹{item.UnitPrice * item.Quantity:N2}");
                        }
                    });

                    col.Item().PaddingTop(16).AlignRight().Width(240).Column(c =>
                    {
                        SummaryRow(c, "Subtotal", d.Subtotal);
                        if (d.DiscountAmount > 0)
                            SummaryRow(c, string.IsNullOrWhiteSpace(d.CouponCode) ? "Discount" : $"Discount ({d.CouponCode})", -d.DiscountAmount);
                        SummaryRow(c, "Delivery Charges", d.ShippingAmount);
                        SummaryRow(c, "Tax", d.TaxAmount);
                        c.Item().PaddingTop(6).LineHorizontal(1);
                        c.Item().PaddingTop(6).Row(row =>
                        {
                            row.RelativeItem().Text("Total").Bold().FontSize(12);
                            row.ConstantItem(100).AlignRight().Text($"₹{d.TotalAmount:N2}").Bold().FontSize(12);
                        });
                    });
                });

                page.Footer().AlignCenter().Text("This is a computer-generated invoice and does not require a signature.").FontSize(8);
            });
        });

        return document.GeneratePdf();
    }

    private static void SummaryRow(ColumnDescriptor c, string label, decimal amount)
    {
        c.Item().Row(row =>
        {
            row.RelativeItem().Text(label);
            row.ConstantItem(100).AlignRight().Text($"₹{amount:N2}");
        });
    }
}
