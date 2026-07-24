namespace HR.Modules.Documents.Services;

/// <summary>
/// Single source of truth for classifying a document's acknowledgement status from a given
/// employee's perspective, into exactly the four states callers need: a document that never
/// required acknowledgement is "Not Required"; one they've acknowledged (of its current version)
/// is "Completed"; one still outstanding past its due date is "Overdue"; anything else
/// outstanding is "Pending".
/// </summary>
internal static class SharedCompanyDocumentAcknowledgementStatusCalculator
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Overdue = "Overdue";
    public const string NotRequired = "Not Required";

    public static string Calculate(
        bool requiresAcknowledgement,
        DateTimeOffset? acknowledgedAt,
        DateOnly? dueDate,
        DateOnly today)
    {
        if (!requiresAcknowledgement)
            return NotRequired;

        if (acknowledgedAt is not null)
            return Completed;

        if (dueDate is not null && dueDate < today)
            return Overdue;

        return Pending;
    }
}
