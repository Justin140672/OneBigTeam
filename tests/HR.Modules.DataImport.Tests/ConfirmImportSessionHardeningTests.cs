using System.Text.Json;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.ConfirmImportSession;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

/// <summary>
/// TEST-007 hardening coverage for ConfirmImportSession: cross-tenant isolation, partial-failure
/// bookkeeping, retry/double-confirm idempotency, invalid cross-references, cancellation, and the
/// imported-employees-skip-onboarding contract (asserted via the published integration event).
/// </summary>
public class ConfirmImportSessionHardeningTests
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
        FakeImportLookupResolver? lookupResolver = null,
        FakeIntegrationEventPublisher? publisher = null) =>
        new(
            db,
            employeeWriter ?? new FakeEmployeeImportWriter(),
            new FakeLeaveImportWriter(),
            new FakeEmployeeImportLookupReader(),
            lookupResolver ?? new FakeImportLookupResolver(),
            publisher ?? new FakeIntegrationEventPublisher(),
            new FakeClock(FixedUtcNow));

    private static ImportSession SeedSession(
        DataImportDbContext db, Guid companyId, int totalRows = 1, ImportStatus target = ImportStatus.Validated)
    {
        var session = ImportSession.Create(
            Guid.NewGuid(), companyId, "Employee", "employees.csv", totalRows, Guid.NewGuid(),
            "sessions/abc/employees.csv", "text/csv", FixedNowOffset);
        db.ImportSessions.Add(session);
        db.SaveChanges();

        session.Start(FixedNowOffset);
        if (target == ImportStatus.CompletedWithErrors)
            session.Validate(successfulRows: totalRows, failedRows: 1, FixedNowOffset);
        else
            session.Validate(successfulRows: totalRows, failedRows: 0, FixedNowOffset);
        db.SaveChanges();

        // Force the exact requested status (Validate lands on Validated whenever successful > 0).
        if (target == ImportStatus.CompletedWithErrors && session.Status != ImportStatus.CompletedWithErrors)
        {
            session.Confirm(createdCount: 0, failedCount: 1, FixedNowOffset);
            db.SaveChanges();
        }

        return session;
    }

    private static string RawData(
        string firstName = "Alice", string lastName = "Smith", string workEmail = "alice@example.com",
        string? departmentName = null, string? locationName = null,
        string? employmentTypeName = null, string? positionProfileTitle = null)
    {
        var fields = new Dictionary<string, string?>
        {
            ["FirstName"] = firstName,
            ["LastName"] = lastName,
            ["WorkEmail"] = workEmail,
            ["StartDate"] = "2026-01-01",
            ["DateOfBirth"] = "1990-01-01",
            ["Nationality"] = "British",
            ["Gender"] = "Female",
        };
        if (departmentName is not null) fields["DepartmentName"] = departmentName;
        if (locationName is not null) fields["LocationName"] = locationName;
        if (employmentTypeName is not null) fields["EmploymentTypeName"] = employmentTypeName;
        if (positionProfileTitle is not null) fields["PositionProfileTitle"] = positionProfileTitle;
        return JsonSerializer.Serialize(fields);
    }

    private static ImportStagingEmployee AddRow(
        DataImportDbContext db, Guid companyId, Guid sessionId, int rowNumber,
        string workEmail = "alice@example.com", string? employeeNumber = "EMP-0001",
        string? managerReference = null, string? rawData = null,
        Guid? departmentId = null, Guid? locationId = null,
        Guid? employmentTypeId = null, Guid? positionProfileId = null, bool isValid = true)
    {
        var row = ImportStagingEmployee.Create(
            Guid.NewGuid(), companyId, sessionId, rowNumber, employeeNumber, workEmail, managerReference,
            departmentId ?? Guid.NewGuid(), locationId ?? Guid.NewGuid(),
            employmentTypeId ?? Guid.NewGuid(), positionProfileId ?? Guid.NewGuid(),
            rawData ?? RawData(workEmail: workEmail), isValid, FixedNowOffset);
        db.ImportStagingEmployees.Add(row);
        db.SaveChanges();
        return row;
    }

    private static ConfirmImportSessionRequest Request(Guid companyId, Guid sessionId) =>
        new() { CompanyId = companyId, ImportSessionId = sessionId };

    // --- Cross-tenant isolation ---

    [Fact]
    public async Task Another_Tenant_Cannot_Confirm_A_Session_It_Does_Not_Own()
    {
        await using var db = BuildContext();
        var ownerCompanyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var session = SeedSession(db, ownerCompanyId);
        AddRow(db, ownerCompanyId, session.Id, 2);

        var employeeWriter = new FakeEmployeeImportWriter();
        var handler = BuildHandler(db, employeeWriter);

        var result = await handler.HandleAsync(Request(otherCompanyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(employeeWriter.CreateRequests);

        var saved = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Validated, saved.Status);
    }

    // --- Partial failure bookkeeping / no undocumented partial state ---

    [Fact]
    public async Task Partial_Failure_Records_Exact_Counts_And_Persists_A_RowError_Per_Failed_Row()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 3);
        AddRow(db, companyId, session.Id, 2, workEmail: "ok1@example.com");
        AddRow(db, companyId, session.Id, 3, workEmail: "boom@example.com");
        AddRow(db, companyId, session.Id, 4, workEmail: "ok2@example.com");

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.FailCreationFor("boom@example.com");
        var handler = BuildHandler(db, employeeWriter);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Equal(nameof(ImportStatus.CompletedWithErrors), result.Value.Status);

        var saved = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.CompletedWithErrors, saved.Status);
        Assert.Equal(2, saved.SuccessfulRows);
        Assert.Equal(1, saved.FailedRows);
        Assert.Equal(3, saved.ProcessedRows);
        Assert.Equal(FixedNowOffset, saved.CompletedAt);

        var rowError = Assert.Single(await db.ImportRowErrors.Where(e => e.ImportSessionId == session.Id).ToListAsync());
        Assert.Equal(3, rowError.RowNumber);
        Assert.Equal(ImportRowErrorSeverity.Error, rowError.Severity);
    }

    // --- Invalid cross-reference: unresolvable required lookup name ---

    [Fact]
    public async Task Row_With_Unresolvable_Required_Lookup_Reference_Fails_That_Row_Only()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 2);

        // Row 2 has NO resolved lookup ids and its raw data omits DepartmentName/LocationName/
        // EmploymentTypeName/PositionProfileTitle, so the confirm handler cannot resolve the
        // reference and GetRequired throws -> the row is recorded as an error, not imported.
        var badRow = ImportStagingEmployee.Create(
            Guid.NewGuid(), companyId, session.Id, 2, "EMP-BAD", "nodept@example.com", null,
            departmentId: null, locationId: null, employmentTypeId: null, positionProfileId: null,
            RawData(workEmail: "nodept@example.com"), isValid: true, FixedNowOffset);
        db.ImportStagingEmployees.Add(badRow);
        db.SaveChanges();
        AddRow(db, companyId, session.Id, 3, workEmail: "fine@example.com");

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Equal(nameof(ImportStatus.CompletedWithErrors), result.Value.Status);

        var rowError = Assert.Single(await db.ImportRowErrors.Where(e => e.ImportSessionId == session.Id).ToListAsync());
        Assert.Equal(2, rowError.RowNumber);
        Assert.Equal(ImportRowErrorSeverity.Error, rowError.Severity);
    }

    // --- Double confirm / retry idempotency ---

    [Fact]
    public async Task Confirming_An_Already_Imported_Session_Is_Rejected_With_No_Duplicate_Employee_Creation()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        AddRow(db, companyId, session.Id, 2, workEmail: "once@example.com");

        var employeeWriter = new FakeEmployeeImportWriter();
        var handler = BuildHandler(db, employeeWriter);

        var first = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(nameof(ImportStatus.Imported), first.Value!.Status);
        Assert.Single(employeeWriter.CreateRequests);

        var second = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("conflict", second.Error.Code);
        Assert.Single(employeeWriter.CreateRequests); // still exactly one - no duplicate business record
    }

    [Fact]
    public async Task Retrying_Confirmation_After_Partial_Failure_Reprocesses_Only_Still_Valid_Rows()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 2);
        AddRow(db, companyId, session.Id, 2, workEmail: "retry@example.com");

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.FailCreationFor("retry@example.com");
        var handler = BuildHandler(db, employeeWriter);

        var first = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(nameof(ImportStatus.CompletedWithErrors), first.Value!.Status);
        Assert.Equal(0, first.Value.CreatedCount);

        // CompletedWithErrors is still a confirmable state, so a retry is allowed once the
        // underlying issue is fixed.
        var fixedWriter = new FakeEmployeeImportWriter();
        var retryHandler = BuildHandler(db, fixedWriter);

        var retry = await retryHandler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(retry.IsSuccess);
        Assert.Equal(1, retry.Value!.CreatedCount);
        Assert.Equal(0, retry.Value.FailedCount);
        Assert.Equal(nameof(ImportStatus.Imported), retry.Value.Status);
        Assert.Single(fixedWriter.CreateRequests);
    }

    // --- Concurrent / duplicate confirmation requests ---

    [Fact]
    public async Task Concurrent_Confirmation_Requests_Do_Not_Both_Create_Employees()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        AddRow(db, companyId, session.Id, 2, workEmail: "race@example.com");

        var employeeWriter = new FakeEmployeeImportWriter();

        // Same DbContext is not thread-safe; model the race as two sequential handler instances
        // sharing one store, which is what the status guard must defend against.
        var handlerA = BuildHandler(db, employeeWriter);
        var handlerB = BuildHandler(db, employeeWriter);

        var resultA = await handlerA.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);
        var resultB = await handlerB.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(resultA.IsSuccess);
        Assert.True(resultB.IsFailure);
        Assert.Equal("conflict", resultB.Error.Code);
        Assert.Single(employeeWriter.CreateRequests);
    }

    // --- Cancellation during confirmation ---

    [Fact]
    public async Task Cancellation_Before_Confirmation_Throws_And_Leaves_Session_Unconfirmed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        AddRow(db, companyId, session.Id, 2);

        var handler = BuildHandler(db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), cts.Token));

        var saved = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Validated, saved.Status);
    }

    // --- Imported employees skip onboarding (event contract) ---

    [Fact]
    public async Task Every_Imported_Row_Publishes_EmployeeCreated_With_IsImported_True()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 2);
        AddRow(db, companyId, session.Id, 2, workEmail: "a@example.com");
        AddRow(db, companyId, session.Id, 3, workEmail: "b@example.com");

        var publisher = new FakeIntegrationEventPublisher();
        var handler = BuildHandler(db, publisher: publisher);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = publisher.Published.OfType<EmployeeCreatedIntegrationEvent>().ToList();
        Assert.Equal(2, created.Count);
        Assert.All(created, e => Assert.True(e.IsImported));
    }
}
