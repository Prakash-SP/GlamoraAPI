using Microsoft.AspNetCore.DataProtection;

namespace PeachyGlamora.Api.Models;

// Bank account for refunds. Deliberately NO update endpoint anywhere — once
// saved, an account's details are immutable. Only Add / Activate / Deactivate
// / (soft) Delete exist. This is intentional: editing a saved bank account
// is exactly the kind of action that should never be silent — if the details
// are wrong, add a new one instead.
public class BankAccount
{
    public int Id { get; set; }
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    public string AccountHolderName { get; set; } = default!;

    // Never stored or returned in plaintext. Encrypted via BankAccountCrypto
    // (ASP.NET Core Data Protection) before being written here.
    public string AccountNumberEncrypted { get; set; } = default!;

    // Last 4 digits ARE stored in plain text — on their own they're not
    // sensitive, and this is what powers the "XXXXXX1234" masked display
    // everywhere without ever touching the encrypted value.
    public string AccountNumberLast4 { get; set; } = default!;

    public string IfscCode { get; set; } = default!;
    public string BankName { get; set; } = default!;
    public string? BranchName { get; set; }

    // Exactly one account per user should be true at a time — enforced in
    // BankAccountsController.Activate (unset all others, then set this one),
    // not by a DB constraint. See README for the filtered-unique-index note
    // if you want that hardened further.
    public bool IsActive { get; set; }

    // Soft delete — deleted accounts are hidden from every customer-facing
    // query (always filtered WHERE IsDeleted == false) but kept in the DB
    // permanently, since a past refund may have gone to this account and
    // that history shouldn't disappear.
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}

// Every time an admin views the full (decrypted) account number to actually
// process a refund, a row goes here. Nothing about this table is ever shown
// to the customer — it's purely an internal audit trail of who saw sensitive
// bank details and when/why.
public class BankAccountRevealLog
{
    public int Id { get; set; }
    public int BankAccountId { get; set; }
    public BankAccount BankAccount { get; set; } = default!;
    public string RevealedByAdminUserId { get; set; } = default!;
    public string? Reason { get; set; } // e.g. the order number the refund is being processed for
    public DateTime RevealedAt { get; set; } = DateTime.UtcNow;
}

// Thin wrapper around IDataProtector so encryption/decryption isn't
// hand-rolled inline in a controller. Uses ASP.NET Core's built-in Data
// Protection API — already active in this project because Identity depends
// on it — so this needs no new external dependency, key vault, or config.
// The purpose string below is a permanent identifier: changing it would make
// every previously-encrypted account number undecryptable, so treat it as
// fixed once deployed (bump to .v2 and handle migration explicitly if the
// encryption scheme ever needs to change).
public class BankAccountCrypto
{
    private readonly IDataProtector _protector;

    public BankAccountCrypto(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("PeachyGlamora.BankAccountNumber.v1");
    }

    public string Encrypt(string plainAccountNumber) => _protector.Protect(plainAccountNumber);
    public string Decrypt(string encryptedAccountNumber) => _protector.Unprotect(encryptedAccountNumber);
}
