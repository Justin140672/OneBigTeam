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
    public Guid? DepartmentId { get; private set; }
    public Guid? LocationId { get; private set; }
    public Guid? EmploymentTypeId { get; private set; }
    public Guid? PositionProfileId { get; private set; }
    // Set only when this row's Work Email matches the company's initial (seed) admin employee —
    // see Employee.IsInitialCompanyAdmin. ConfirmImportSessionHandler updates that existing
    // employee instead of creating a new one when this is non-null.
    public Guid? ExistingEmployeeIdToUpdate { get; private set; }
    public string RawData { get; private set; } = string.Empty;
    public bool IsValid { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // OBT-REM-06: durable per-row confirmation state. Once set, a retry of the confirm step skips
    // this row instead of creating the employee a second time.
    public Guid? CreatedEmployeeId { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public void MarkConfirmed(Guid createdEmployeeId, DateTimeOffset now)
    {
        CreatedEmployeeId = createdEmployeeId;
        ConfirmedAt = now;
    }

    public static ImportStagingEmployee Create(
        Guid id,
        Guid companyId,
        Guid importSessionId,
        int rowNumber,
        string? employeeNumber,
        string? workEmail,
        string? managerReference,
        Guid? departmentId,
        Guid? locationId,
        Guid? employmentTypeId,
        Guid? positionProfileId,
        string rawData,
        bool isValid,
        DateTimeOffset now,
        Guid? existingEmployeeIdToUpdate = null)
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
            DepartmentId = departmentId,
            LocationId = locationId,
            EmploymentTypeId = employmentTypeId,
            PositionProfileId = positionProfileId,
            ExistingEmployeeIdToUpdate = existingEmployeeIdToUpdate,
            RawData = rawData,
            IsValid = isValid,
            CreatedAt = now,
        };
    }
}
