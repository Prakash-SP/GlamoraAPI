using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PeachyGlamora.Api.Services;

// A separate document from the tax invoice — this is the "SHIP TO" address
// slip meant to be printed and physically pasted onto the parcel, the way a
// courier label works. Deliberately NOT the invoice: a tax invoice has line
// items and prices, which don't need to be legible from across a warehouse
// and shouldn't be the first thing a delivery agent squints at to find the
// address. Kept as its own small PDF, own service, own endpoint.
public interface IShippingLabelPdfService
{
    // Returns null if no matching order is found (controller turns that
    // into a 404, same convention as IInvoicePdfService).
    Task<byte[]?> GenerateShippingLabelPdfAsync(int orderId);

    // Batch printing for the Orders list "select + print labels" flow.
    // Lays out labelsPerPage labels per A4 page (2 = one A5 half each, more
    // legible; 4 = four quarter-height strips, denser but tighter fit) with
    // a cut line between each, so admin can print a stack of selected
    // orders' labels in one PDF and physically cut the sheet apart.
    // Returns null only if NONE of the requested order IDs matched anything
    // in the database — if some (not all) are missing, those are silently
    // skipped and the rest are still rendered, since a batch print run
    // shouldn't fail entirely because one order was deleted/renumbered
    // between page-load and print-click.
    Task<byte[]?> GenerateBatchShippingLabelsPdfAsync(List<int> orderIds, int labelsPerPage = 2);
}

public class ShippingLabelPdfService : IShippingLabelPdfService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public ShippingLabelPdfService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ---------- Return address ("FROM / RETURN TO") ----------
    // Read from appsettings.json (section "ShippingLabel:ReturnAddress")
    // instead of hardcoded constants, since this is the address a courier
    // physically sends an undelivered parcel back to. Deliberately does NOT
    // fall back to a placeholder if this is missing/incomplete — a label
    // with a fake return address could actually get printed and pasted on a
    // real parcel, and nobody would notice until a delivery failed. Better
    // to refuse to generate the PDF at all and surface a clear error.
    //
    // Add this to appsettings.json:
    // "ShippingLabel": {
    //   "ReturnAddress": {
    //     "Name": "Peachy Glamora",
    //     "AddressLine1": "Flat 202, A96/97, Lane No. 4",
    //     "AddressLine2": "Paryavaran Complex, Saket, New Delhi",
    //     "PinCode": "110030",
    //     "Phone": "9021559122"
    //   }
    // }
    private record ReturnAddress(string Name, string Line1, string Line2, string PinCode, string Phone);

    // Thrown when the return address is missing/incomplete — caught by the
    // controller and turned into a clear 400/500, never silently swallowed
    // into a placeholder-filled PDF.
    public class ShippingLabelConfigException : Exception
    {
        public ShippingLabelConfigException(string message) : base(message) { }
    }

    private ReturnAddress GetReturnAddress()
    {
        var section = _config.GetSection("ShippingLabel:ReturnAddress");
        if (!section.Exists())
            throw new ShippingLabelConfigException(
                "Shipping label return address is not configured. Add a 'ShippingLabel:ReturnAddress' section to appsettings.json before printing labels.");

        var missing = new List<string>();
        string Require(string key)
        {
            var value = section[key];
            if (string.IsNullOrWhiteSpace(value)) missing.Add(key);
            return value ?? "";
        }

        var name = Require("Name");
        var line1 = Require("AddressLine1");
        var line2 = Require("AddressLine2");
        // Accept either key so an existing "Pin Code" (with the space)
        // config still works, but prefer the no-space "PinCode" going
        // forward — see the appsettings.json example above.
        var pinCode = section["PinCode"];
        if (string.IsNullOrWhiteSpace(pinCode)) pinCode = section["Pin Code"];
        if (string.IsNullOrWhiteSpace(pinCode)) missing.Add("PinCode");
        var phone = Require("Phone");

        if (missing.Count > 0)
            throw new ShippingLabelConfigException(
                $"Shipping label return address is missing required field(s): {string.Join(", ", missing)}. " +
                "Fill in 'ShippingLabel:ReturnAddress' in appsettings.json before printing labels.");

        return new ReturnAddress(name, line1, line2, pinCode!, phone);
    }

    private record LabelData(
        int OrderId,
        string OrderNumber,
        string? TrackingId,
        string RecipientName,
        string RecipientPhone,
        string Line1,
        string? Line2,
        string City,
        string State,
        string Pincode,
        int ItemCount,
        bool IsCodPending,
        decimal CodAmount);

    public async Task<byte[]?> GenerateShippingLabelPdfAsync(int orderId)
    {
        // Fail fast: check the return address is properly configured BEFORE
        // touching the database at all — no point querying the order just to
        // throw once we get to rendering. GetReturnAddress() throws
        // ShippingLabelConfigException if anything's missing; the controller
        // catches that specifically and returns a clear error, never a PDF.
        var returnAddress = GetReturnAddress();

        var data = await _db.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new LabelData(
                o.Id,
                o.OrderNumber,
                o.TrackingId,
                o.ShippingAddress.FullName,
                o.ShippingAddress.Phone,
                o.ShippingAddress.Line1,
                o.ShippingAddress.Line2,
                o.ShippingAddress.City,
                o.ShippingAddress.State,
                o.ShippingAddress.Pincode,
                o.Items.Where(i => !i.IsCancelled).Sum(i => (int?)i.Quantity) ?? 0,
                // COD amount is only worth printing on the label while it's
                // actually still owed — once Payment.Status is Paid (COD
                // collected at delivery, per OrderService/PaymentsController
                // conventions) there's nothing left to collect.
                o.Payment != null && o.Payment.Method == PaymentMethod.CashOnDelivery && o.Payment.Status != PaymentStatus.Paid,
                o.TotalAmount))
            .FirstOrDefaultAsync();

        return data == null ? null : Render(data, returnAddress);
    }

    public async Task<byte[]?> GenerateBatchShippingLabelsPdfAsync(List<int> orderIds, int labelsPerPage = 2)
    {
        if (orderIds == null || orderIds.Count == 0) return null;
        if (labelsPerPage != 2 && labelsPerPage != 4)
            throw new ArgumentOutOfRangeException(nameof(labelsPerPage), "Only 2 or 4 labels per page are supported.");

        // Same fail-fast rule as the single-label path: don't touch the DB
        // for a batch of orders just to throw once rendering starts.
        var returnAddress = GetReturnAddress();

        var found = await _db.Orders
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new LabelData(
                o.Id,
                o.OrderNumber,
                o.TrackingId,
                o.ShippingAddress.FullName,
                o.ShippingAddress.Phone,
                o.ShippingAddress.Line1,
                o.ShippingAddress.Line2,
                o.ShippingAddress.City,
                o.ShippingAddress.State,
                o.ShippingAddress.Pincode,
                o.Items.Where(i => !i.IsCancelled).Sum(i => (int?)i.Quantity) ?? 0,
                o.Payment != null && o.Payment.Method == PaymentMethod.CashOnDelivery && o.Payment.Status != PaymentStatus.Paid,
                o.TotalAmount))
            .ToListAsync();

        if (found.Count == 0) return null;

        // Preserve the order the admin selected them in (e.g. top-to-bottom
        // on the Orders list) rather than whatever order EF/SQL returns them
        // in — otherwise which order ends up paired on which sheet half is
        // effectively random from the admin's point of view.
        var byId = found.ToDictionary(d => d.OrderId);
        var ordered = orderIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        return RenderBatch(ordered, returnAddress, labelsPerPage);
    }

    // 2-up: A4 page split into 2 vertically-stacked A5-height halves.
    // 4-up: a 2x2 grid instead of 4 stacked strips — stacking 4 would need
    // ~200pt per label (vs ~408pt for 2-up), which isn't enough room for
    // full-size address text no matter how much other content shrinks. A
    // grid keeps both rows at the same height as the 2-up layout (so SHIP TO
    // / RETURN TO never need smaller fonts) and only narrows the width,
    // which the address block absorbs by wrapping an extra line here and
    // there. Only the order#/items/AWB/prepaid block gets denser — that's
    // the part with slack to spare, not the addresses.
    private const float A4WidthPt = 595f;   // ~210mm
    private const float A4HeightPt = 842f;  // ~297mm
    private const float CutLineAreaPt = 26f;      // horizontal cut line height
    private const float VerticalCutGapPt = 16f;   // vertical cut line width, 4-up grid only

    private static byte[] RenderBatch(List<LabelData> orders, ReturnAddress returnAddress, int labelsPerPage)
    {
        var rowHeight = (A4HeightPt - CutLineAreaPt) / 2f; // same height either way

        var document = Document.Create(container =>
        {
            if (labelsPerPage == 4)
            {
                for (var i = 0; i < orders.Count; i += 4)
                {
                    var group = orders.Skip(i).Take(4).ToList();
                    container.Page(page =>
                    {
                        page.Size(A4WidthPt, A4HeightPt);
                        page.Margin(0);
                        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));
                        page.Background(Colors.White);

                        page.Content().Column(col =>
                        {
                            col.Item().Height(rowHeight).Element(e => RenderGridRow(e, group, 0, returnAddress));
                            col.Item().Height(CutLineAreaPt).Element(RenderHorizontalCutLine);
                            col.Item().Height(rowHeight).Element(e => RenderGridRow(e, group, 2, returnAddress));
                        });
                    });
                }
            }
            else // labelsPerPage == 2
            {
                for (var i = 0; i < orders.Count; i += 2)
                {
                    var top = orders[i];
                    var bottom = i + 1 < orders.Count ? orders[i + 1] : null;

                    container.Page(page =>
                    {
                        page.Size(A4WidthPt, A4HeightPt);
                        page.Margin(0);
                        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Black));
                        page.Background(Colors.White);

                        page.Content().Column(col =>
                        {
                            col.Item().Height(rowHeight).Padding(18)
                                .Element(e => RenderLabelBody(e, top, returnAddress));

                            col.Item().Height(CutLineAreaPt).Element(RenderHorizontalCutLine);

                            if (bottom != null)
                                col.Item().Height(rowHeight).Padding(18)
                                    .Element(e => RenderLabelBody(e, bottom, returnAddress));
                            else
                                col.Item().Height(rowHeight); // odd count — leave the last half blank
                        });
                    });
                }
            }
        });

        return document.GeneratePdf();
    }

    // One row of the 4-up grid: two labels side by side with a vertical cut
    // line between them. startIndex is 0 for the top row, 2 for the bottom
    // row of a 4-order group; either slot renders blank if the group ran
    // short (last page of an odd-sized batch).
    private static void RenderGridRow(IContainer container, List<LabelData> group, int startIndex, ReturnAddress returnAddress)
    {
        container.Row(row =>
        {
            row.RelativeItem().Padding(14).Element(e =>
            {
                if (startIndex < group.Count)
                    RenderLabelBody(e, group[startIndex], returnAddress, compactInfo: true);
            });

            row.ConstantItem(VerticalCutGapPt).AlignMiddle()
                .LineVertical(1).LineColor(Colors.Grey.Medium);

            row.RelativeItem().Padding(14).Element(e =>
            {
                if (startIndex + 1 < group.Count)
                    RenderLabelBody(e, group[startIndex + 1], returnAddress, compactInfo: true);
            });
        });
    }

    private static void RenderHorizontalCutLine(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignMiddle().LineHorizontal(1).LineColor(Colors.Grey.Medium);
            row.ConstantItem(70).AlignCenter().AlignMiddle()
                .Text("CUT HERE").FontSize(8).FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
            row.RelativeItem().AlignMiddle().LineHorizontal(1).LineColor(Colors.Grey.Medium);
        });
    }

    // compactInfo controls ONLY the order#/items/AWB/prepaid section — the
    // SHIP TO and RETURN TO address blocks always render at full size,
    // whether this is a single-label print, 2-up, or the 4-up grid. That's
    // deliberate: shrinking the order-info row buys back enough room that
    // addresses never have to.
    private static void RenderLabelBody(IContainer container, LabelData d, ReturnAddress returnAddress, bool compactInfo = false)
    {
        const float headerFont = 13, headerPad = 8;
        const float addressBoxPad = 10;
        const float shipLabelFont = 9, shipNameFont = 15, shipLineFont = 10;
        float infoBoxPad = compactInfo ? 6 : 10;
        float infoLabelFont = compactInfo ? 6 : 8, infoValueFont = compactInfo ? 9 : 10;

        container.Column(col =>
        {
            col.Spacing(8);

            col.Item().Background(Colors.Black).Padding(headerPad)
                .Text("Peachy Glamora").FontSize(headerFont).Bold().FontColor(Colors.White);

            col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(addressBoxPad).Column(c =>
            {
                c.Item().Text("SHIP TO").FontSize(shipLabelFont).FontColor(Colors.Grey.Darken1);
                c.Item().PaddingTop(3).Text(d.RecipientName).FontSize(shipNameFont).Bold();
                c.Item().Text(d.Line1 + (string.IsNullOrWhiteSpace(d.Line2) ? "" : ", " + d.Line2))
                    .FontSize(shipLineFont).FontColor(Colors.Grey.Darken2);
                c.Item().Text($"{d.City}, {d.State} - {d.Pincode}").FontSize(shipLineFont).Bold();
                c.Item().PaddingTop(3).Text($"Phone: {d.RecipientPhone}").FontSize(shipLineFont);
            });

            col.Item().Row(row =>
            {
                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(infoBoxPad).Column(c =>
                {
                    c.Item().Text("ORDER #").FontSize(infoLabelFont).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(d.OrderNumber).FontSize(infoValueFont).Bold();
                });
                row.ConstantItem(compactInfo ? 4 : 6);
                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(infoBoxPad).Column(c =>
                {
                    c.Item().Text("ITEMS").FontSize(infoLabelFont).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(d.ItemCount.ToString()).FontSize(infoValueFont).Bold();
                });
                row.ConstantItem(compactInfo ? 4 : 6);
                row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(infoBoxPad).AlignMiddle()
                    .Element(e =>
                    {
                        // Orders are prepaid by default — a COD box only
                        // shows if this particular order genuinely has
                        // money still owed at delivery. Otherwise, a plain
                        // "Prepaid" tag so delivery staff never mistake a
                        // prepaid order for one needing collection.
                        if (d.IsCodPending)
                            e.Text($"COD ₹{d.CodAmount:N0}").FontSize(infoValueFont - 1).Bold().FontColor(Colors.Red.Darken2);
                        else
                            e.Text("Prepaid").FontSize(infoValueFont - 1).Bold().FontColor(Colors.Green.Darken2);
                    });
            });

            if (!string.IsNullOrWhiteSpace(d.TrackingId))
            {
                col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(infoBoxPad).Column(c =>
                {
                    c.Item().Text("AWB / TRACKING ID").FontSize(infoLabelFont).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(d.TrackingId).FontSize(infoValueFont).Bold().LetterSpacing(0.03f);
                });
            }

            col.Item().Border(1).BorderColor(Colors.Grey.Darken1).Padding(addressBoxPad).Column(c =>
            {
                c.Item().Text("IF UNDELIVERED, RETURN TO").FontSize(shipLabelFont - 1).FontColor(Colors.Grey.Darken1);
                c.Item().Text(returnAddress.Name).FontSize(shipLineFont - 1).Bold();
                c.Item().Text(returnAddress.Line1).FontSize(shipLineFont).FontColor(Colors.Grey.Darken2);
                c.Item().Text($"{returnAddress.Line2} - {returnAddress.PinCode}").FontSize(shipLineFont).FontColor(Colors.Grey.Darken2);
                c.Item().Text($"Phone: {returnAddress.Phone}").FontSize(shipLineFont).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static byte[] Render(LabelData d, ReturnAddress returnAddress)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                // Fixed width, UNLIMITED height that shrinks/grows to fit
                // whatever content actually renders — a two-line address
                // takes more vertical space than a one-liner, and a fixed
                // page height (e.g. 420x620) would throw a "content too
                // large" layout exception the moment it didn't fit. This is
                // the right choice for a label/receipt-style document where
                // width is fixed (print/paste size) but content length varies.
                page.ContinuousSize(420);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Black));
                page.Background(Colors.White);

                // Same RenderLabelBody used by the batch (2-up/4-up) print
                // path, non-compact mode — so a single order's label looks
                // identical whether it's printed alone from the order detail
                // page or as part of a multi-order batch. Previously this
                // method had its own separate hand-built layout (bordered
                // "FROM" box, no "Prepaid" tag, different header) that had
                // drifted from the batch design; sharing one render function
                // means the two can no longer go out of sync again.
                page.Content().Element(e => RenderLabelBody(e, d, returnAddress, compactInfo: false));

                page.Footer().AlignCenter().PaddingTop(8)
                    .Text("Please handle with care.").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });

        return document.GeneratePdf();
    }
}