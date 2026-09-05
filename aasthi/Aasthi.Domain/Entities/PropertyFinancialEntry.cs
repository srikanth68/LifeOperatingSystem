namespace Aasthi.Domain.Entities;

// Money that actually moved for a property.
//
// One table, deliberately. The obvious alternative was a second "ledger" table holding
// bank-matched rows next to this one, and that would have left two places to look for
// the same expense -- confusing for the user and worse for San, which would have to
// know which to search. Everything below extends this record rather than replacing it,
// so entries created before any of this existed stay valid: they are simply manual,
// confirmed, and unclassified for tax.
//
// Expectations are NOT stored here. What a property is due to receive or pay lives in
// RecurringCharge, and "did the rent arrive?" is answered by looking for an entry that
// satisfies the charge. Materialising twelve future rent rows per property per year
// would mean thousands of records that exist only to be absent.
public class PropertyFinancialEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public string Type { get; set; } = "expense";      // income | expense | mortgage
    public string Category { get; set; } = "other";    // rent | tax | insurance | repair | mortgage_payment | hoa | utility | other
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Bank reconciliation ---

    // The Vault transaction this entry came from, when it came from one. Kept as the
    // Vault id rather than a foreign key because Vault is a separate module with its
    // own database, and its transactions are a Plaid mirror that re-syncs.
    //
    // The link lives here rather than as a PropertyId column on Vault's Transaction
    // for two reasons: a Plaid resync must never be able to drop it, and one $340
    // hardware-store charge can be split across two properties -- which is possible
    // with several entries pointing at one transaction, and impossible with one
    // property field on the transaction.
    public string? VaultTransactionId { get; set; }

    // Who created this. Manual entries are the user's own and are trusted on sight;
    // "san" means the daily pass proposed it and it is waiting to be confirmed.
    public string Origin { get; set; } = "manual";     // manual | san | recurring | import

    // Where it is. Only "pending" is ever shown for confirmation -- a manual assignment
    // is confirmed the moment it is made, because the user made it.
    //
    // "rejected" rows are kept rather than deleted: they are the tombstone that stops
    // the daily pass proposing the same transaction again tomorrow, which is exactly
    // how the reminder notifications became something to ignore.
    public string Status { get; set; } = "confirmed";  // confirmed | pending | rejected

    // Which recurring obligation this satisfies, when it satisfies one. Null for
    // one-off repairs, which is most of what matters at tax time.
    public Guid? RecurringChargeId { get; set; }

    // 0-100, only meaningful on rows the matcher created. Low-confidence matches are
    // written as pending rather than confirmed.
    public int? MatchConfidence { get; set; }

    // --- Tax ---

    // The distinction with real money attached: patching a roof is deductible this
    // year, replacing it is a capital improvement depreciated over 27.5. Nothing in
    // the bank feed can tell these apart -- only the user or San reading the receipt
    // can -- so it starts unclassified rather than guessing a default that would
    // silently be wrong on every improvement.
    public string TaxTreatment { get; set; } = "unclassified";  // unclassified | deductible | capital_improvement | non_deductible

    // The receipt for THIS expense. PropertyDocument already exists but hangs off the
    // property, which is fine for a deed and useless for an audit -- "show me the
    // receipt for that $340" needs the document attached to the entry.
    public Guid? ReceiptDocumentId { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public Property Property { get; set; } = null!;
}
