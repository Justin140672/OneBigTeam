namespace HR.Modules.DataImport.Features.ValidateImportSession;

internal sealed class ValidateImportSessionRequest
{
    public Guid CompanyId { get; init; }
    public Guid ImportSessionId { get; init; }

    /// <summary>
    /// Optional user-adjusted column mapping (target field name -> source file header name).
    /// Entries here override the standard employee column mapping; target fields not present
    /// keep their standard header. Null or empty uses the standard mapping unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ColumnMapping { get; init; }
}
