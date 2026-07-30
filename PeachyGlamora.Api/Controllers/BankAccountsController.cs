using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/bank-accounts")]
[Authorize]
public class BankAccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BankAccountCrypto _crypto;
    private readonly IHttpClientFactory _httpClientFactory;
    public BankAccountsController(AppDbContext db, BankAccountCrypto crypto, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _crypto = crypto;
        _httpClientFactory = httpClientFactory;
    }

    private string UserId => User.FindFirst("sub")!.Value;

    private static string Mask(string last4) => $"XXXXXX{last4}";

    // .Select() projection per the project convention — also doubles here as
    // the mechanism that guarantees the encrypted number and its decryption
    // key never even enter the response shape, not just "we didn't map it."
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Project raw scalars first (Mask() can't run inside a SQL-translated
        // .Select() — same rule as the recurring EF Core bug pattern noted
        // elsewhere in this project), then mask in-memory after ToListAsync().
        var raw = await _db.BankAccounts
            .Where(b => b.UserId == UserId && !b.IsDeleted)
            .OrderByDescending(b => b.IsActive).ThenByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.Id,
                b.AccountHolderName,
                b.AccountNumberLast4,
                b.IfscCode,
                b.BankName,
                b.BranchName,
                b.IsActive,
                b.CreatedAt
            })
            .ToListAsync();

        var accounts = raw.Select(b => new
        {
            b.Id,
            b.AccountHolderName,
            MaskedAccountNumber = Mask(b.AccountNumberLast4),
            b.IfscCode,
            b.BankName,
            b.BranchName,
            b.IsActive,
            b.CreatedAt
        });

        return Ok(accounts);
    }

    public record CreateBankAccountRequest(
        string AccountHolderName, string AccountNumber, string IfscCode, string BankName, string? BranchName);

    private static readonly Regex IfscPattern = new(@"^[A-Z]{4}0[A-Z0-9]{6}$");
    private static readonly Regex AccountNumberPattern = new(@"^\d{9,18}$");

    private class RazorpayIfscResponse
    {
        public string? BANK { get; set; }
        public string? BRANCH { get; set; }
    }

    // Server-side proxy for Razorpay's free public IFSC lookup — the
    // browser can't call ifsc.razorpay.com directly because that endpoint
    // doesn't send back CORS headers, but a server-to-server call from
    // here isn't subject to CORS at all. Frontend calls THIS endpoint
    // instead; this endpoint calls Razorpay and relays the result. Purely
    // a convenience for auto-filling Bank Name / Branch Name — failure
    // here is never an error the customer needs to see, they can just
    // type both fields by hand.
    [HttpGet("ifsc-lookup/{code}")]
    public async Task<IActionResult> IfscLookup(string code)
    {
        code = (code ?? "").ToUpper();
        if (!IfscPattern.IsMatch(code))
            return BadRequest(new { error = "Not a valid IFSC code format." });

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5); // this is a "nice to have" lookup, never worth a long hang
            var response = await client.GetAsync($"https://ifsc.razorpay.com/{code}");
            if (!response.IsSuccessStatusCode)
                return NotFound(new { error = "No bank found for this IFSC code." });

            var result = await response.Content.ReadFromJsonAsync<RazorpayIfscResponse>();
            return Ok(new { bankName = result?.BANK, branchName = result?.BRANCH });
        }
        catch
        {
            // Network hiccup / Razorpay down / timeout — same as "not found"
            // from the frontend's point of view: it just falls back to
            // manual typing, no error banner needed.
            return NotFound(new { error = "Could not look up this IFSC code right now." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateBankAccountRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.AccountHolderName))
            return BadRequest(new { error = "Please enter the account holder's name." });
        if (!AccountNumberPattern.IsMatch(req.AccountNumber ?? ""))
            return BadRequest(new { error = "Please enter a valid account number (9–18 digits)." });
        if (!IfscPattern.IsMatch((req.IfscCode ?? "").ToUpper()))
            return BadRequest(new { error = "Please enter a valid IFSC code (e.g. HDFC0001234)." });
        if (string.IsNullOrWhiteSpace(req.BankName))
            return BadRequest(new { error = "Please enter the bank name." });

        // First payout method a user ever adds — bank account OR UPI ID —
        // becomes Active automatically — same "first one is the default"
        // convenience as Address.IsDefault.
        var hasAnyActive = await _db.BankAccounts.AnyAsync(b => b.UserId == UserId && !b.IsDeleted && b.IsActive)
            || await _db.UpiAccounts.AnyAsync(u => u.UserId == UserId && !u.IsDeleted && u.IsActive);

        var account = new BankAccount
        {
            UserId = UserId,
            AccountHolderName = req.AccountHolderName.Trim(),
            AccountNumberEncrypted = _crypto.Encrypt(req.AccountNumber),
            AccountNumberLast4 = req.AccountNumber[^4..],
            IfscCode = req.IfscCode.ToUpper(),
            BankName = req.BankName.Trim(),
            BranchName = req.BranchName?.Trim(),
            IsActive = !hasAnyActive,
        };

        _db.BankAccounts.Add(account);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            account.Id,
            account.AccountHolderName,
            MaskedAccountNumber = Mask(account.AccountNumberLast4),
            account.IfscCode,
            account.BankName,
            account.BranchName,
            account.IsActive,
            account.CreatedAt,
            message = "Bank account added."
        });
    }

    [HttpPut("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id)
    {
        var accounts = await _db.BankAccounts.Where(b => b.UserId == UserId && !b.IsDeleted).ToListAsync();
        var target = accounts.FirstOrDefault(b => b.Id == id);
        if (target == null) return NotFound();

        foreach (var a in accounts) a.IsActive = a.Id == id;

        // Only one payout method total can be Active per user — a bank
        // account OR a UPI ID, never both. Deactivate any active UPI ID
        // too, since Bank Accounts and UPI IDs now share one "Active" slot
        // on the combined Payout Methods page (see UpiAccountsController.Activate
        // for the mirror image of this).
        var activeUpiAccounts = await _db.UpiAccounts
            .Where(u => u.UserId == UserId && !u.IsDeleted && u.IsActive).ToListAsync();
        foreach (var u in activeUpiAccounts) u.IsActive = false;

        await _db.SaveChangesAsync();
        return Ok(new { message = "Bank account activated." });
    }

    [HttpPut("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var account = await _db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId && !b.IsDeleted);
        if (account == null) return NotFound();

        // Deliberately does NOT auto-activate another account — leaving zero
        // active accounts is a valid state the customer can choose. Nothing
        // in the refund flow should guess a replacement.
        account.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Bank account deactivated." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await _db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId && !b.IsDeleted);
        if (account == null) return NotFound();

        // Cannot delete the active account directly — forces an explicit
        // "activate something else first" step rather than the system
        // guessing which remaining account should take over for refunds.
        if (account.IsActive)
            return BadRequest(new { error = "You can't delete your active bank account. Activate a different payout method first (or add a new one), then delete this one." });

        account.IsDeleted = true;
        account.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Bank account removed." });
    }
}