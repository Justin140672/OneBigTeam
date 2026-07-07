namespace HR.Modules.DataImport.Domain;

internal sealed class ImportStagingEmployee
{
    private ImportStagingEmployee() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ImportSessionId { get; private set; }
    public int RowNumber { get; private set; }
    public string? EmployeeNumber { get; private set; }
    public string? WorkEmail { get; private set; }
    public string? ManagerReference { get; private set; }
    public string RawData { get; private set; } = string.Empty;
    public bool IsValid { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ImportStagingEmployee Create(
        Guid id,
        Guid companyId,
        Guid importSessionId,
        int rowNumber,
        string? employeeNumber,
        string? workEmail,
        string? managerReference,
        string rawData,
        bool isValid,
        DateTimeOffset now)
    {
        return new ImportStagingEmployee
        {
            Id = id,
            CompanyId = companyId,
            ImportSessionId = importSessionId,
            RowNumber = rowNumber,
            EmployeeNumber = employeeNumber,
            WorkEmail = workEmail,
            ManagerReference = managerReference,
            RawData = rawData,
            IsValid = isValid,
            CreatedAt = now,
        };
    }
}
