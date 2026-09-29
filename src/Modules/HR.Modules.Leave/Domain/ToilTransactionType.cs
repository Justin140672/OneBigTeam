namespace HR.Modules.Leave.Domain;

internal enum ToilTransactionType
{
    Earned,

    Used,

    Expired,

    /// <summary>
    /// A manual correction, or the reversal of a prior Used transaction (e.g. on leave
    /// cancellation). Reversals set <see cref="ToilTransaction.ReversesTransactionId"/>.
    /// </summary>
    Adjusted
}
