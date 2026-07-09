using System.Text.Json;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.ConfirmImportSession;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

public class ConfirmImportSessionHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNowOffset = new(FixedUtcNow, TimeSpan.Zero);

    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ConfirmImportSessionHandler BuildHandler(
        DataImportDbContext db,
        FakeEmployeeImportWriter? employeeWriter = null,
        FakeLeaveImportWriter? leaveWriter = null,
        FakeEmployeeImportLookupReader? lookupReader = null,
        FakeIntegrationEventPublisher? publisher = null) =>
        new(
            db,
            employeeWriter ?? new FakeEmployeeImportWriter(),
            leaveWriter ?? new FakeLeaveImportWriter(),
            lookupReader ?? new FakeEmployeeImportLookupReader(),
            publisher ?? new FakeIntegrationEventPublisher(),
            new FakeClock(FixedUtcNow));

    private static ImportSession SeedSession(
        DataImportDbContext db, Guid companyId, ImportStatus status, int totalRows = 1)
    {
        var session = ImportSession.Create(
            Guid.NewGuid(), companyId, "Employee", "employees.csv", totalRows, Guid.NewGuid(),
            "sessions/abc/employees.csv", "text/csv", FixedNowOffset);
        db.ImportSessions.Add(session);
        db.SaveChanges();

        session.Start(FixedNowOffset);
        session.Validate(successfulRows: totalRows, failedRows: 0, FixedNowOffset);
        db.SaveChanges();

        return session;
    }

    private static string BuildRawData(
        string firstName = "Alice",
        string lastName = "Smith",
        string workEmail = "alice@example.com",
        string startDate = "2026-01-01",
        string? workingDays = null,
        string? hoursPerDay = null,
        string? salaryAmount = null,
        string? leaveTypeCode = null,
        string? leaveBalanceDays = null)
    {
        var fields = new Dictionary<string, string?>
        {
            ["FirstName"] = firstName,
            ["LastName"] = lastName,
            ["WorkEmail"] = workEmail,
            ["StartDate"] = startDate,
        };
        if (workingDays is not null) fields["WorkingDays"] = workingDays;
        if (hoursPerDay is not null) fields["HoursPerDay"] = hoursPerDay;
        if (salaryAmount is not null) fields["SalaryAmount"] = salaryAmount;
        if (leaveTypeCode is not null) fields["LeaveTypeCode"] = leaveTypeCode;
        if (leaveBalanceDays is not null) fields["LeaveBalanceDays"] = leaveBalanceDays;

        return JsonSerializer.Serialize(fields);
    }

    private static ImportStagingEmployee AddValidRow(
        DataImportDbContext db,
        Guid companyId,
        Guid sessionId,
        int rowNumber,
        string workEmail = "alice@example.com",
        string? employeeNumber = null,
        string? managerReference = null,
        string rawData = "")
    {
        var row = ImportStagingEmployee.Create(
            Guid.NewGuid(), companyId, sessionId, rowNumber, employeeNumber, workEmail,
            managerReference, null, null, null, null,
            string.IsNullOrEmpty(rawData) ? BuildRawData(workEmail: workEmail) : rawData,
            isValid: true, FixedNowOffset);
        db.ImportStagingEmployees.Add(row);
        db.SaveChanges();
        return row;
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = Guid.NewGuid(), ImportSessionId = Guid.NewGuid() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Session_Is_Not_In_A_Confirmable_State()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        // Pending is not Validated or CompletedWithErrors.
        var session = ImportSession.Create(
            Guid.NewGuid(), companyId, "Employee", "employees.csv", 1, Guid.NewGuid(),
            "sessions/abc/employees.csv", "text/csv", FixedNowOffset);
        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_There_Are_No_Valid_Rows()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated);

        // No staging rows added at all.
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Employee_For_Each_Valid_Row_And_Marks_Session_Imported()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated);
        AddValidRow(db, companyId, session.Id, 2, workEmail: "alice@example.com");

        var employeeWriter = new FakeEmployeeImportWriter();
        var publisher = new FakeIntegrationEventPublisher();
        var handler = BuildHandler(db, employeeWriter, publisher: publisher);
        var actorUserId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            actorUserId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.FailedCount);
        Assert.Equal(nameof(ImportStatus.Imported), result.Value.Status);

        var createRequest = Assert.Single(employeeWriter.CreateRequests);
        Assert.Equal("Alice", createRequest.FirstName);
        Assert.Equal("alice@example.com", createRequest.WorkEmail);
        Assert.Equal(actorUserId, createRequest.ActorUserId);
        Assert.Equal(session.Id, createRequest.ImportSessionId);

        var events = publisher.Published;
        Assert.Contains(events, e => e is EmployeeCreatedIntegrationEvent created && created.IsImported);
        Assert.Contains(events, e => e is EmployeeImportedIntegrationEvent imported && imported.RowNumber == 2);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Imported, savedSession.Status);
    }

    [Fact]
    public async Task HandleAsync_Sets_WorkingPattern_And_Compensation_When_Columns_Populated()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated);
        AddValidRow(
            db, companyId, session.Id, 2,
            rawData: BuildRawData(workingDays: "Monday,Tuesday", hoursPerDay: "8", salaryAmount: "50000"));

        var employeeWriter = new FakeEmployeeImportWriter();
        var handler = BuildHandler(db, employeeWriter);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(employeeWriter.WorkingPatternCalls);
        Assert.Single(employeeWriter.CompensationCalls);
    }

    [Fact]
    public async Task HandleAsync_Lays_Opening_Leave_Balance_When_Leave_Columns_Populated()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated);
        AddValidRow(
            db, companyId, session.Id, 2,
            rawData: BuildRawData(leaveTypeCode: "ANNUAL", leaveBalanceDays: "20"));

        var leaveWriter = new FakeLeaveImportWriter();
        var handler = BuildHandler(db, leaveWriter: leaveWriter);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(leaveWriter.Calls);
        Assert.Equal("ANNUAL", call.LeaveTypeCode);
        Assert.Equal(20m, call.OpeningBalanceDays);
    }

    [Fact]
    public async Task HandleAsync_Records_Row_Error_And_Continues_When_A_Row_Throws()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated, totalRows: 2);
        AddValidRow(db, companyId, session.Id, 2, workEmail: "fails@example.com");
        AddValidRow(db, companyId, session.Id, 3, workEmail: "succeeds@example.com");

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.FailCreationFor("fails@example.com");
        var handler = BuildHandler(db, employeeWriter);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Equal(nameof(ImportStatus.CompletedWithErrors), result.Value.Status);

        var rowError = await db.ImportRowErrors.SingleAsync(e => e.ImportSessionId == session.Id);
        Assert.Equal(2, rowError.RowNumber);
        Assert.Equal(ImportRowErrorSeverity.Error, rowError.Severity);
        Assert.Contains("Simulated failure", rowError.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Manager_Reference_Matching_Another_Row_Created_In_Same_Session()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated, totalRows: 2);
        AddValidRow(db, companyId, session.Id, 2, workEmail: "manager@example.com", employeeNumber: "MGR1",
            rawData: BuildRawData(workEmail: "manager@example.com"));
        AddValidRow(db, companyId, session.Id, 3, workEmail: "report@example.com", managerReference: "MGR1",
            rawData: BuildRawData(workEmail: "report@example.com"));

        var employeeWriter = new FakeEmployeeImportWriter();
        var handler = BuildHandler(db, employeeWriter);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attempt = Assert.Single(employeeWriter.ManagerAssignmentAttempts);

        var managerRequest = employeeWriter.CreateRequests.Single(r => r.WorkEmail == "manager@example.com");
        var reportRequest = employeeWriter.CreateRequests.Single(r => r.WorkEmail == "report@example.com");
        Assert.Equal(managerRequest.Id, attempt.ManagerId);
        Assert.Equal(reportRequest.Id, attempt.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Manager_Reference_Via_LookupReader_For_Existing_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated);
        AddValidRow(db, companyId, session.Id, 2, workEmail: "report@example.com",
            managerReference: "existing.manager@example.com",
            rawData: BuildRawData(workEmail: "report@example.com"));

        var existingManagerId = Guid.NewGuid();
        var lookupReader = new FakeEmployeeImportLookupReader();
        lookupReader.SeedReference("existing.manager@example.com", existingManagerId);

        var employeeWriter = new FakeEmployeeImportWriter();
        var handler = BuildHandler(db, employeeWriter, lookupReader: lookupReader);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attempt = Assert.Single(employeeWriter.ManagerAssignmentAttempts);
        Assert.Equal(existingManagerId, attempt.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Manager_Assignment_Failure_Records_Warning_Row_Error()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, ImportStatus.Validated, totalRows: 2);
        AddValidRow(db, companyId, session.Id, 2, workEmail: "manager@example.com", employeeNumber: "MGR1",
            rawData: BuildRawData(workEmail: "manager@example.com"));
        AddValidRow(db, companyId, session.Id, 3, workEmail: "report@example.com", managerReference: "MGR1",
            rawData: BuildRawData(workEmail: "report@example.com"));

        // The fake's TryAssignManagerAsync normally fails only for pre-registered employee ids —
        // since the handler generates a fresh Guid per row, force every assignment attempt to
        // fail instead.
        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.FailAllManagerAssignments();
        var handler = BuildHandler(db, employeeWriter);

        var result = await handler.HandleAsync(
            new ConfirmImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.FailedCount);
        Assert.Equal(nameof(ImportStatus.Imported), result.Value.Status);

        var warning = await db.ImportRowErrors.SingleAsync(e => e.ImportSessionId == session.Id);
        Assert.Equal(3, warning.RowNumber);
        Assert.Equal(ImportRowErrorSeverity.Warning, warning.Severity);
        Assert.Contains("could not be assigned", warning.ErrorMessage);
    }
}
