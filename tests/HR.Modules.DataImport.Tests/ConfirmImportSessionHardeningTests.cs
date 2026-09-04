using System.Text.Json;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.ConfirmImportSession;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
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
        FakeIntegrationEventPublisher? publisher = null,
        FakeLeaveImportWriter? leaveWriter = null,
        DateTime? now = null) =>
        new(
            db,
            employeeWriter ?? new FakeEmployeeImportWriter(),
            leaveWriter ?? new FakeLeaveImportWriter(),
            new FakeEmployeeImportLookupReader(),
            lookupResolver ?? new FakeImportLookupResolver(),
            publisher ?? new FakeIntegrationEventPublisher(),
            new FakeClock(now ?? FixedUtcNow));

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

    // --- OBT-REM-08: granular per-row resume, without ever recreating an already-created employee ---

    [Fact]
    public async Task Row_With_Employee_Already_Created_But_Events_Not_Published_Republishes_Without_Recreating_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        var employeeId = Guid.NewGuid();

        // Simulates a crash immediately after MarkEmployeeCreated was persisted, before any of the
        // downstream steps ran.
        var row = AddRow(db, companyId, session.Id, 2, workEmail: "resume@example.com");
        row.MarkEmployeeCreated(employeeId, FixedNowOffset);
        db.SaveChanges();

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.SeedSnapshot(employeeId, new EmployeeImportCreateResult(
            employeeId, "EMP-0001", new DateOnly(2026, 1, 1), null, null, new DateOnly(2026, 7, 1), null));
        var publisher = new FakeIntegrationEventPublisher();
        var handler = BuildHandler(db, employeeWriter, publisher: publisher);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(employeeWriter.CreateRequests); // never recreated
        Assert.Equal(1, employeeWriter.GetImportSnapshotCalls);
        Assert.Single(publisher.Published.OfType<EmployeeCreatedIntegrationEvent>());
        Assert.Single(publisher.Published.OfType<EmployeeImportedIntegrationEvent>());

        var saved = await db.ImportStagingEmployees.SingleAsync(s => s.Id == row.Id);
        Assert.Equal(employeeId, saved.CreatedEmployeeId);
        Assert.NotNull(saved.EmployeeCreatedEventPublishedAt);
        Assert.NotNull(saved.EmployeeImportedEventPublishedAt);
        Assert.True(saved.IsFullyConfirmed);
        Assert.Equal(nameof(ImportStatus.Imported), result.Value!.Status);
    }

    [Fact]
    public async Task Row_Missing_OpeningLeaveBalanceProcessedAt_Gets_Balance_Applied_On_Retry()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        var employeeId = Guid.NewGuid();

        var fields = new Dictionary<string, string?>
        {
            ["FirstName"] = "Alice", ["LastName"] = "Smith", ["WorkEmail"] = "leave@example.com",
            ["StartDate"] = "2026-01-01", ["DateOfBirth"] = "1990-01-01",
            ["Nationality"] = "British", ["Gender"] = "Female", ["LeaveBalanceDays"] = "5",
        };
        var row = AddRow(db, companyId, session.Id, 2, workEmail: "leave@example.com", rawData: JsonSerializer.Serialize(fields));
        row.MarkEmployeeCreated(employeeId, FixedNowOffset);
        row.MarkEmployeeCreatedEventPublished(FixedNowOffset);
        row.MarkEmployeeImportedEventPublished(FixedNowOffset);
        db.SaveChanges();

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.SeedSnapshot(employeeId, new EmployeeImportCreateResult(
            employeeId, "EMP-0001", new DateOnly(2026, 1, 1), null, null, new DateOnly(2026, 7, 1), null));
        var leaveWriter = new FakeLeaveImportWriter();
        var handler = BuildHandler(db, employeeWriter, leaveWriter: leaveWriter);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(leaveWriter.Calls);
        Assert.Equal(employeeId, call.EmployeeId);
        Assert.Equal(5m, call.OpeningBalanceDays);

        var saved = await db.ImportStagingEmployees.SingleAsync(s => s.Id == row.Id);
        Assert.NotNull(saved.OpeningLeaveBalanceProcessedAt);
        Assert.True(saved.IsFullyConfirmed);
    }

    [Fact]
    public async Task Row_With_OpeningLeaveBalance_Already_Processed_Is_Not_Reapplied_On_Retry()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        var employeeId = Guid.NewGuid();

        var fields = new Dictionary<string, string?>
        {
            ["FirstName"] = "Alice", ["LastName"] = "Smith", ["WorkEmail"] = "already@example.com",
            ["StartDate"] = "2026-01-01", ["DateOfBirth"] = "1990-01-01",
            ["Nationality"] = "British", ["Gender"] = "Female", ["LeaveBalanceDays"] = "5",
        };
        var row = AddRow(db, companyId, session.Id, 2, workEmail: "already@example.com", rawData: JsonSerializer.Serialize(fields));
        row.MarkEmployeeCreated(employeeId, FixedNowOffset);
        row.MarkEmployeeCreatedEventPublished(FixedNowOffset);
        row.MarkEmployeeImportedEventPublished(FixedNowOffset);
        // The step under test is already marked complete from a previous attempt; only manager
        // assignment (which resolves to "no manager reference" here) is still outstanding.
        row.MarkOpeningLeaveBalanceProcessed(FixedNowOffset);
        db.SaveChanges();

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.SeedSnapshot(employeeId, new EmployeeImportCreateResult(
            employeeId, "EMP-0001", new DateOnly(2026, 1, 1), null, null, new DateOnly(2026, 7, 1), null));
        var leaveWriter = new FakeLeaveImportWriter();
        var handler = BuildHandler(db, employeeWriter, leaveWriter: leaveWriter);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(leaveWriter.Calls); // must not double-apply the opening balance

        var saved = await db.ImportStagingEmployees.SingleAsync(s => s.Id == row.Id);
        Assert.True(saved.IsFullyConfirmed);
    }

    [Fact]
    public async Task Session_Completes_As_Imported_Once_The_Last_Outstanding_Step_Finishes_For_Every_Row()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1, target: ImportStatus.CompletedWithErrors);
        var employeeId = Guid.NewGuid();

        // Every step is already done except manager assignment (no manager reference here, so this
        // finalizes with no external calls at all) - proves a retry that only needed to finish the
        // very last step still transitions the session out of CompletedWithErrors.
        var row = AddRow(db, companyId, session.Id, 2, workEmail: "last-step@example.com");
        row.MarkEmployeeCreated(employeeId, FixedNowOffset);
        row.MarkEmployeeCreatedEventPublished(FixedNowOffset);
        row.MarkEmployeeImportedEventPublished(FixedNowOffset);
        row.MarkOpeningLeaveBalanceProcessed(FixedNowOffset);
        db.SaveChanges();

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.SeedSnapshot(employeeId, new EmployeeImportCreateResult(
            employeeId, "EMP-0001", new DateOnly(2026, 1, 1), null, null, new DateOnly(2026, 7, 1), null));
        var leaveWriter = new FakeLeaveImportWriter();
        var handler = BuildHandler(db, employeeWriter, leaveWriter: leaveWriter);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(ImportStatus.Imported), result.Value!.Status);
        Assert.Empty(employeeWriter.CreateRequests);
        Assert.Empty(leaveWriter.Calls);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Imported, savedSession.Status);
        var savedRow = await db.ImportStagingEmployees.SingleAsync(s => s.Id == row.Id);
        Assert.True(savedRow.IsFullyConfirmed);
        Assert.NotNull(savedRow.ManagerAssignmentProcessedAt);
    }

    [Fact]
    public async Task Retry_Reports_Cumulative_Totals_Including_Rows_Confirmed_On_An_Earlier_Attempt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 2, target: ImportStatus.CompletedWithErrors);

        // Row 2 was fully confirmed by an earlier attempt; row 3 has never been touched at all.
        var alreadyDoneEmployeeId = Guid.NewGuid();
        var rowAlreadyDone = AddRow(db, companyId, session.Id, 2, workEmail: "done@example.com", employeeNumber: "EMP-0002");
        rowAlreadyDone.MarkEmployeeCreated(alreadyDoneEmployeeId, FixedNowOffset);
        rowAlreadyDone.MarkEmployeeCreatedEventPublished(FixedNowOffset);
        rowAlreadyDone.MarkEmployeeImportedEventPublished(FixedNowOffset);
        rowAlreadyDone.MarkOpeningLeaveBalanceProcessed(FixedNowOffset);
        rowAlreadyDone.MarkManagerAssignmentProcessed(FixedNowOffset);
        rowAlreadyDone.MarkFullyConfirmed(FixedNowOffset);

        AddRow(db, companyId, session.Id, 3, workEmail: "fresh@example.com", employeeNumber: "EMP-0003");
        db.SaveChanges();

        var employeeWriter = new FakeEmployeeImportWriter();
        employeeWriter.SeedSnapshot(alreadyDoneEmployeeId, new EmployeeImportCreateResult(
            alreadyDoneEmployeeId, "EMP-0002", new DateOnly(2026, 1, 1), null, null, new DateOnly(2026, 7, 1), null));
        var handler = BuildHandler(db, employeeWriter);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.FailedCount);
        Assert.Equal(nameof(ImportStatus.Imported), result.Value.Status);
        Assert.Equal(2, result.Value.CreatedRows.Count);
        // The already-confirmed row is fully skipped (no writer interaction at all) - only the
        // still-outstanding row goes through CreateEmployeeAsync.
        Assert.Single(employeeWriter.CreateRequests);
    }

    // --- OBT-REM-08: an actively-running claim is judged for staleness from when THIS attempt
    // started, not from the original Validate timestamp. ---

    [Fact]
    public async Task Stale_Processing_Claim_Older_Than_15_Minutes_Is_Reclaimable_And_StartedAt_Is_Refreshed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        AddRow(db, companyId, session.Id, 2, workEmail: "stale@example.com");

        // Simulates a prior run that claimed the session (Processing) 20 minutes ago and then
        // crashed without ever completing - older than the 15-minute stale-claim window.
        session.Start(FixedNowOffset.AddMinutes(-20));
        db.SaveChanges();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        // The bug this guards against: StartedAt used to only ever be set once (??=), so a stale
        // claim's timestamp would stay frozen at the original crash time forever. It must now
        // reflect this attempt's own claim time.
        Assert.Equal(FixedNowOffset, saved.StartedAt);
        Assert.NotEqual(FixedNowOffset.AddMinutes(-20), saved.StartedAt);
    }

    [Fact]
    public async Task Recently_Claimed_Processing_Session_Is_Not_Confirmable_Even_If_Validated_Long_Ago()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);
        AddRow(db, companyId, session.Id, 2);

        // Actively running: claimed 5 minutes ago, well inside the 15-minute stale window, even
        // though the original Validate call (inside SeedSession) happened at the same FixedNowOffset
        // - i.e. this must NOT be judged stale using the original validation timestamp.
        session.Start(FixedNowOffset.AddMinutes(-5));
        db.SaveChanges();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(Request(companyId, session.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        var saved = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Processing, saved.Status);
    }
}
