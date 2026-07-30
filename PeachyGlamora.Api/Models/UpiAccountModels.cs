namespace PeachyGlamora.Api.Models;

// UPI ID for refunds — mirrors BankAccount's Add / Activate / Deactivate /
// (soft) Delete pattern, but deliberately has none of the encryption /
// masking / reveal-log machinery BankAccount has. A UPI VPA (e.g.
// "name@oksbi") can only be used to SEND money to it, not pull money out,
// so it isn't treated as sensitive the way a bank account number is.
// Stored and displayed in plain text everywhere — to the customer and to
// admins alike.
public class UpiAccount
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    // e.g. "rahul@oksbi", "9876543210@ybl" — format-validated only via
    // regex (see UpiAccountsController) since there's no payment gateway
    // wired up yet to verify a VPA actually resolves to a real account.
    // Once a payout gateway (Razorpay/Cashfree/etc.) is integrated, call
    // its VPA-verify endpoint at save-time in addition to this regex.
    public string UpiId { get; set; } = default!;

    // Only ONE payout method — a bank account OR a UPI ID — should be
    // Active per user at a time, since a refund can only go to one place.
    // Enforced across BOTH BankAccounts and UpiAccounts tables in
    // BankAccountsController.Activate / UpiAccountsController.Activate,
    // not by a DB constraint (same approach as BankAccount.IsActive).
    public bool IsActive { get; set; }

    // Soft delete — same reasoning as BankAccount.IsDeleted. Hidden from
    // every customer-facing query but kept in the DB permanently, since a
    // past refund may have gone to this UPI ID and that history shouldn't
    // disappear.
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
