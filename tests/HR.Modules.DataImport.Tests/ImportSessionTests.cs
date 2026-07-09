using HR.Modules.DataImport.Domain;

namespace HR.Modules.DataImport.Tests;

public class ImportSessionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static ImportSession CreateSession(DateTimeOffset now, int totalRows = 100)
    {
        return ImportSession.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Employee",
            "employees.csv",
            totalRows,
            Guid.NewGuid(),
            "company/session/employees.csv",
            "text/csv",
            now);
    }

    [Fact]
    public void Create_Sets_All_Fields_Correctly()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var initiatedByUserId = Guid.NewGuid();

        var session = ImportSession.Create(
            id,
            companyId,
            "Employee",
            "employees.csv",
            100,
            initiatedByUserId,
            "company/session/employees.csv",
            "text/csv",
            FixedNow);

        Assert.Equal(id, session.Id);
        Assert.Equal(companyId, session.CompanyId);
        Assert.Equal("Employee", session.EntityType);
        Assert.Equal("employees.csv", session.FileName);
        Assert.Equal(100, session.TotalRows);
        Assert.Equal(initiatedByUserId, session.InitiatedByUserId);
        Assert.Equal("company/session/employees.csv", session.StorageKey);
        Assert.Equal("text/csv", session.ContentType);
        Assert.Equal(FixedNow, session.CreatedAt);
        Assert.Equal(FixedNow, session.UpdatedAt);
    }

    [Fact]
    public void Create_Defaults_Status_To_Pending()
    {
        var session = CreateSession(FixedNow);

        Assert.Equal(ImportStatus.Pending, session.Status);
    }

    [Fact]
    public void Create_Defaults_RowCounts_To_Zero()
    {
        var session = CreateSession(FixedNow);

        Assert.Equal(0, session.ProcessedRows);
        Assert.Equal(0, session.SuccessfulRows);
        Assert.Equal(0, session.FailedRows);
    }

    [Fact]
    public void Create_Leaves_Optional_Fields_Null()
    {
        var session = CreateSession(FixedNow);

        Assert.Null(session.StartedAt);
        Assert.Null(session.CompletedAt);
        Assert.Null(session.ErrorSummary);
    }

    [Fact]
    public void Start_Sets_Status_To_Processing()
    {
        var session = CreateSession(FixedNow);
        var startedAt = FixedNow.AddMinutes(5);

        session.Start(startedAt);

        Assert.Equal(ImportStatus.Processing, session.Status);
    }

    [Fact]
    public void Start_Sets_StartedAt_And_UpdatedAt()
    {
        var session = CreateSession(FixedNow);
        var startedAt = FixedNow.AddMinutes(5);

        session.Start(startedAt);

        Assert.Equal(startedAt, session.StartedAt);
        Assert.Equal(startedAt, session.UpdatedAt);
    }

    [Fact]
    public void Complete_With_No_Failed_Rows_Sets_Status_To_Completed()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        var completedAt = FixedNow.AddMinutes(10);

        session.Complete(successfulRows: 100, failedRows: 0, completedAt);

        Assert.Equal(ImportStatus.Completed, session.Status);
        Assert.Equal(100, session.SuccessfulRows);
        Assert.Equal(0, session.FailedRows);
        Assert.Equal(100, session.ProcessedRows);
        Assert.Equal(completedAt, session.CompletedAt);
        Assert.Equal(completedAt, session.UpdatedAt);
    }

    [Fact]
    public void Complete_With_Failed_Rows_Sets_Status_To_CompletedWithErrors()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        var completedAt = FixedNow.AddMinutes(10);

        session.Complete(successfulRows: 90, failedRows: 10, completedAt);

        Assert.Equal(ImportStatus.CompletedWithErrors, session.Status);
        Assert.Equal(90, session.SuccessfulRows);
        Assert.Equal(10, session.FailedRows);
        Assert.Equal(completedAt, session.CompletedAt);
        Assert.Equal(completedAt, session.UpdatedAt);
    }

    [Fact]
    public void Complete_Sets_ProcessedRows_To_Sum_Of_Successful_And_Failed()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));

        session.Complete(successfulRows: 90, failedRows: 10, FixedNow.AddMinutes(10));

        Assert.Equal(session.SuccessfulRows + session.FailedRows, session.ProcessedRows);
        Assert.Equal(100, session.ProcessedRows);
    }

    [Fact]
    public void Fail_Sets_Status_To_Failed()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        var failedAt = FixedNow.AddMinutes(2);

        session.Fail("Unrecoverable parse error", failedAt);

        Assert.Equal(ImportStatus.Failed, session.Status);
    }

    [Fact]
    public void Fail_Sets_ErrorSummary_And_CompletedAt()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        var failedAt = FixedNow.AddMinutes(2);

        session.Fail("Unrecoverable parse error", failedAt);

        Assert.Equal("Unrecoverable parse error", session.ErrorSummary);
        Assert.Equal(failedAt, session.CompletedAt);
        Assert.Equal(failedAt, session.UpdatedAt);
    }

    [Fact]
    public void Cancel_Sets_Status_To_Cancelled()
    {
        var session = CreateSession(FixedNow);
        var cancelledAt = FixedNow.AddMinutes(3);

        session.Cancel(cancelledAt);

        Assert.Equal(ImportStatus.Cancelled, session.Status);
    }

    [Fact]
    public void Cancel_Sets_CompletedAt_And_UpdatedAt()
    {
        var session = CreateSession(FixedNow);
        var cancelledAt = FixedNow.AddMinutes(3);

        session.Cancel(cancelledAt);

        Assert.Equal(cancelledAt, session.CompletedAt);
        Assert.Equal(cancelledAt, session.UpdatedAt);
    }

    [Fact]
    public void Validate_With_At_Least_One_Successful_Row_Sets_Status_To_Validated()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        var validatedAt = FixedNow.AddMinutes(5);

        session.Validate(successfulRows: 1, failedRows: 1, validatedAt);

        Assert.Equal(ImportStatus.Validated, session.Status);
        Assert.Equal(1, session.SuccessfulRows);
        Assert.Equal(1, session.FailedRows);
        Assert.Equal(2, session.ProcessedRows);
        Assert.Equal(validatedAt, session.CompletedAt);
        Assert.Equal(validatedAt, session.UpdatedAt);
    }

    [Fact]
    public void Validate_With_All_Rows_Successful_Sets_Status_To_Validated()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        var validatedAt = FixedNow.AddMinutes(5);

        session.Validate(successfulRows: 2, failedRows: 0, validatedAt);

        Assert.Equal(ImportStatus.Validated, session.Status);
        Assert.Equal(0, session.FailedRows);
    }

    [Fact]
    public void Validate_With_Zero_Successful_Rows_Sets_Status_To_CompletedWithErrors()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        var validatedAt = FixedNow.AddMinutes(5);

        session.Validate(successfulRows: 0, failedRows: 3, validatedAt);

        Assert.Equal(ImportStatus.CompletedWithErrors, session.Status);
        Assert.Equal(0, session.SuccessfulRows);
        Assert.Equal(3, session.FailedRows);
        Assert.Equal(3, session.ProcessedRows);
    }

    [Fact]
    public void Confirm_With_Zero_Failed_Rows_Sets_Status_To_Imported()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        session.Validate(successfulRows: 2, failedRows: 0, FixedNow.AddMinutes(2));
        var confirmedAt = FixedNow.AddMinutes(10);

        session.Confirm(createdCount: 2, failedCount: 0, confirmedAt);

        Assert.Equal(ImportStatus.Imported, session.Status);
        Assert.Equal(2, session.SuccessfulRows);
        Assert.Equal(0, session.FailedRows);
        Assert.Equal(2, session.ProcessedRows);
        Assert.Equal(confirmedAt, session.CompletedAt);
        Assert.Equal(confirmedAt, session.UpdatedAt);
    }

    [Fact]
    public void Confirm_With_Some_Failed_Rows_Sets_Status_To_CompletedWithErrors()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        session.Validate(successfulRows: 3, failedRows: 0, FixedNow.AddMinutes(2));
        var confirmedAt = FixedNow.AddMinutes(10);

        session.Confirm(createdCount: 2, failedCount: 1, confirmedAt);

        Assert.Equal(ImportStatus.CompletedWithErrors, session.Status);
        Assert.Equal(2, session.SuccessfulRows);
        Assert.Equal(1, session.FailedRows);
        Assert.Equal(3, session.ProcessedRows);
    }

    [Fact]
    public void Confirm_With_All_Rows_Failed_Sets_Status_To_CompletedWithErrors()
    {
        var session = CreateSession(FixedNow);
        session.Start(FixedNow.AddMinutes(1));
        session.Validate(successfulRows: 2, failedRows: 0, FixedNow.AddMinutes(2));
        var confirmedAt = FixedNow.AddMinutes(10);

        session.Confirm(createdCount: 0, failedCount: 2, confirmedAt);

        Assert.Equal(ImportStatus.CompletedWithErrors, session.Status);
        Assert.Equal(0, session.SuccessfulRows);
        Assert.Equal(2, session.FailedRows);
    }
}
