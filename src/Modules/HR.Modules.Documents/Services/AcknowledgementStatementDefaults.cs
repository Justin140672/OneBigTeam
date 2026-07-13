namespace HR.Modules.Documents.Services;

/// <summary>
/// The acknowledgement statement is optional at the document level — this is the sentence shown
/// to employees when HR hasn't written a custom one. Applied at read time only, never persisted,
/// so changing it later affects every document that hasn't overridden it.
/// </summary>
internal static class AcknowledgementStatementDefaults
{
    public const string Default = "I confirm that I have read and understood this document.";

    public static string Resolve(string? statement) =>
        string.IsNullOrWhiteSpace(statement) ? Default : statement;
}
