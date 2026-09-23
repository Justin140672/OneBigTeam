namespace HR.Web.Components.Controls;

/// <summary>
/// Maps invitation exclusion / skip reason codes (from the bulk-invite confirmation, the
/// QueueInvitationBatch response <c>Excluded</c> list, and InvitationBatchRecipient.FailureReason)
/// to user-facing text. Unknown codes fall back to the raw value. The server is authoritative for
/// which emails are rejected — this only formats the codes it returns.
/// </summary>
public static class InvitationReasonDisplay
{
    public const string PublicEmailDomain = "PublicEmailDomain";

    /// <summary>Short label, suitable for a table cell.</summary>
    public static string Label(string? reason) => reason switch
    {
        null or ""          => "—",
        "NotEligible"       => "Not eligible",
        "MissingEmail"      => "No work email on file",
        "AlreadyInvited"    => "Already invited",
        "AlreadyHasAccount" => "Already has an account",
        "DuplicateEmail"    => "Duplicate email address",
        PublicEmailDomain   => "Organisation email required",
        _                   => reason,
    };

    /// <summary>Longer explanation, suitable for a sentence in an alert.</summary>
    public static string Explanation(string? reason) => reason switch
    {
        PublicEmailDomain => "Organisation email required. An organisation email address is required to create an account.",
        _                 => Label(reason),
    };
}
