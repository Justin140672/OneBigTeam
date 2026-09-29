namespace HR.Modules.Documents.Services;

internal static class AcknowledgementStatementDefaults
{
    public const string Default = "I confirm that I have read and understood this document.";

    public static string Resolve(string? statement) =>
        string.IsNullOrWhiteSpace(statement) ? Default : statement;
}
