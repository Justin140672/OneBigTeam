namespace HR.Modules.Documents.Services;

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
