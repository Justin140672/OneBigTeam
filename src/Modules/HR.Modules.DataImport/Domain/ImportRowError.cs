namespace HR.Modules.DataImport.Domain;

internal sealed class ImportRowError
{
    private ImportRowError() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ImportSessionId { get; private set; }
    public int RowNumber { get; private set; }
    public ImportRowErrorSeverity Severity { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public string? RawRowData { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ImportRowError Create(
        Guid id,
        Guid companyId,
        Guid importSessionId,
        int rowNumber,
        ImportRowErrorSeverity severity,
        string errorMessage,
        string? rawRowData,
        DateTimeOffset now)
    {
        return new ImportRowError
        {
            Id = id,
            CompanyId = companyId,
            ImportSessionId = importSessionId,
            RowNumber = rowNumber,
            Severity = severity,
            ErrorMessage = errorMessage,
            RawRowData = rawRowData,
            CreatedAt = now,
        };
    }
}
