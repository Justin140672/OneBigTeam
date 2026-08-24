namespace HR.Modules.Leave.Domain;

/// <summary>
/// Typed classification for every TOIL ledger entry (LEAVE-06). The ledger is the source of truth
/// for TOIL balances - every change to a TOIL balance must be represented by exactly one of these
/// transaction types, each carrying an actor, a date, a signed/unsigned amount (see
/// <see cref="ToilTransaction.Days"/>) and a traceable source.
/// </summary>
internal enum ToilTransactionType
{
    /// <summary>TOIL earned via <c>AwardToil</c>. Each Earned transaction is its own FIFO bucket.</summary>
    Earned,

    /// <summary>TOIL consumed by an approved leave request, drawn from one or more Earned buckets.</summary>
    Used,

    /// <summary>An Earned bucket's remaining, unused TOIL expiring under the company's TOIL policy.</summary>
    Expired,

    /// <summary>
    /// A manual correction, or the reversal of a prior Used transaction (e.g. on leave
    /// cancellation). Reversals set <see cref="ToilTransaction.ReversesTransactionId"/>.
    /// </summary>
    Adjusted
}
