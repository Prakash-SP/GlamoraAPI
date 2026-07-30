using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeachyGlamora.Api.Data;
using PeachyGlamora.Api.Models;

namespace PeachyGlamora.Api.Controllers;

[ApiController]
[Route("api/upi-accounts")]
[Authorize]
public class UpiAccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    public UpiAccountsController(AppDbContext db) => _db = db;

    private string UserId => User.FindFirst("sub")!.Value;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var accounts = await _db.UpiAccounts
            .Where(u => u.UserId == UserId && !u.IsDeleted)
            .OrderByDescending(u => u.IsActive).ThenByDescending(u => u.CreatedAt)
            .Select(u => new { u.Id, u.UpiId, u.IsActive, u.CreatedAt })
            .ToListAsync();

        return Ok(accounts);
    }

    public record CreateUpiAccountRequest(string UpiId);

    // handle@psp — 2-256 chars before the @ (letters/digits/dot/hyphen/
    // underscore, per NPCI's VPA format), 2-64 letters after it (the
    // bank/PSP handle, e.g. oksbi, ybl, paytm). Format-only validation —
    // there's no payment gateway yet to actually verify the VPA resolves
    // to a real account (see UpiAccount.UpiId comment).
    private static readonly Regex UpiPattern = new(@"^[a-zA-Z0-9.\-_]{2,256}@[a-zA-Z]{2,64}$");

    [HttpPost]
    public async Task<IActionResult> Create(CreateUpiAccountRequest req)
    {
        var upiId = (req.UpiId ?? "").Trim();
        if (!UpiPattern.IsMatch(upiId))
            return BadRequest(new { error = "Please enter a valid UPI ID (e.g. name@oksbi)." });

        // First payout method a user ever adds — bank account OR UPI ID —
        // becomes Active automatically, same convenience as Address.IsDefault
        // and BankAccount's own first-one-is-Active rule.
        var hasAnyActive = await _db.BankAccounts.AnyAsync(b => b.UserId == UserId && !b.IsDeleted && b.IsActive)
            || await _db.UpiAccounts.AnyAsync(u => u.UserId == UserId && !u.IsDeleted && u.IsActive);

        var account = new UpiAccount
        {
            UserId = UserId,
            UpiId = upiId,
            IsActive = !hasAnyActive,
        };

        _db.UpiAccounts.Add(account);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            account.Id,
            account.UpiId,
            account.IsActive,
            account.CreatedAt,
            message = "UPI ID added."
        });
    }

    [HttpPut("{id:int}/activate")]
    public async Task<IActionResult> Activate(int id)
    {
        var target = await _db.UpiAccounts.FirstOrDefaultAsync(u => u.Id == id && u.UserId == UserId && !u.IsDeleted);
        if (target == null) return NotFound();

        // Only one payout method total can be Active per user — a bank
        // account OR a UPI ID, never both — since a refund only goes to
        // one place. Unset every active bank account AND every other UPI
        // account for this user, then set this one. This is the piece that
        // makes Bank Accounts and UPI IDs share a single "Active" slot on
        // the combined Payout Methods page.
        var activeBankAccounts = await _db.BankAccounts
            .Where(b => b.UserId == UserId && !b.IsDeleted && b.IsActive).ToListAsync();
        foreach (var b in activeBankAccounts) b.IsActive = false;

        var otherUpiAccounts = await _db.UpiAccounts
            .Where(u => u.UserId == UserId && !u.IsDeleted && u.Id != id).ToListAsync();
        foreach (var u in otherUpiAccounts) u.IsActive = false;

        target.IsActive = true;
        await _db.SaveChangesAsync();
        return Ok(new { message = "UPI ID activated." });
    }

    [HttpPut("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var account = await _db.UpiAccounts.FirstOrDefaultAsync(u => u.Id == id && u.UserId == UserId && !u.IsDeleted);
        if (account == null) return NotFound();

        // Doesn't auto-activate a replacement — same reasoning as
        // BankAccountsController.Deactivate. Zero active payout methods is
        // a valid state; nothing should guess a replacement.
        account.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { message = "UPI ID deactivated." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await _db.UpiAccounts.FirstOrDefaultAsync(u => u.Id == id && u.UserId == UserId && !u.IsDeleted);
        if (account == null) return NotFound();

        // Cannot delete the active UPI ID directly — forces an explicit
        // "activate something else first" step, same as bank accounts.
        if (account.IsActive)
            return BadRequest(new { error = "You can't delete your active UPI ID. Activate a different payout method first (or add a new one), then delete this one." });

        account.IsDeleted = true;
        account.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { message = "UPI ID removed." });
    }
}
