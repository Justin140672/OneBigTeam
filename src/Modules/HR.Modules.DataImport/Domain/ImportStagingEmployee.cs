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

    // OBT-REM-06/OBT-REM-08: durable per-row confirmation progress. Each step is recorded
    // independently so a retry after a crash resumes exactly the steps that did not complete,
    // without ever creating a second employee for the row. A row is only counted as successfully
    // confirmed (see ConfirmImportSessionHandler) once FullyConfirmedAt is set.
    public Guid? CreatedEmployeeId { get; private set; }
    public DateTimeOffset? EmployeeCreatedAt { get; private set; }
    public DateTimeOffset? EmployeeCreatedEventPublishedAt { get; private set; }
    public DateTimeOffset? EmployeeImportedEventPublishedAt { get; private set; }
    public DateTimeOffset? OpeningLeaveBalanceProcessedAt { get; private set; }
    public DateTimeOffset? ManagerAssignmentProcessedAt { get; private set; }
    // Mapped to the pre-existing "confirmed_at" column (was previously set right after employee
    // creation; now only set once every mandatory step below has completed).
    public DateTimeOffset? FullyConfirmedAt { get; private set; }

    public bool IsFullyConfirmed => FullyConfirmedAt is not null;

    public void MarkEmployeeCreated(Guid createdEmployeeId, DateTimeOffset now)
    {
        CreatedEmployeeId = createdEmployeeId;
        EmployeeCreatedAt = now;
    }

    public void MarkEmployeeCreatedEventPublished(DateTimeOffset now) => EmployeeCreatedEventPublishedAt = now;

    public void MarkEmployeeImportedEventPublished(DateTimeOffset now) => EmployeeImportedEventPublishedAt = now;

    public void MarkOpeningLeaveBalanceProcessed(DateTimeOffset now) => OpeningLeaveBalanceProcessedAt = now;

    public void MarkManagerAssignmentProcessed(DateTimeOffset now) => ManagerAssignmentProcessedAt = now;

    /// <summary>
    /// Marks the row as fully confirmed. Only valid once every mandatory downstream step
    /// (employee creation, both integration events, and the leave-balance / manager-assignment
    /// passes) has completed for this row.
    /// </summary>
    public void MarkFullyConfirmed(DateTimeOffset now) => FullyConfirmedAt = now;

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
