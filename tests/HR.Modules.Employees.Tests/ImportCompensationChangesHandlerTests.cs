using ClosedXML.Excel;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.ImportCompensationChanges;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Employees.Tests;

public class ImportCompensationChangesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 8, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public async Task HandleAsync_Returns_InvalidFile_When_Stream_Is_Not_A_Workbook()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, new FakeAuditPublisher());

        using var stream = new MemoryStream([1, 2, 3, 4, 5]);

        var outcome = await handler.HandleAsync(Guid.NewGuid(), stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.InvalidFile, outcome.Type);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationFailed_When_Employee_Number_Not_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = BuildHandler(context, new FakeAuditPublisher());

        var stream = BuildWorkbook(("EMP-999", "50000", "Annual", "2027-01-01", "NewHire", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Contains(outcome.RowErrors, e => e.Message.Contains("was not found"));
        Assert.Empty(await context.Compensations.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationFailed_When_New_Salary_Is_Not_A_Positive_Number()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, "EMP-001", now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var stream = BuildWorkbook(("EMP-001", "not-a-number", "Annual", "2027-01-01", "NewHire", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Contains(outcome.RowErrors, e => e.Message.Contains("New Salary"));
        Assert.Empty(await context.Compensations.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationFailed_When_Employee_Has_No_Existing_Compensation_Record()
    {
        // Salary Frequency is reference-only and is never validated as user input — instead, the
        // handler requires an existing open compensation record to source the frequency from.
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, "EMP-001", now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        // Whatever text is in the Salary Frequency cell (even nonsense) is irrelevant now.
        var stream = BuildWorkbook(("EMP-001", "50000", "NotAFrequency", "2027-01-01", "NewHire", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Contains(
            outcome.RowErrors,
            e => e.Message.Contains("Employee has no existing compensation record to determine salary frequency from."));
        Assert.Empty(await context.Compensations.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Uses_SalaryType_From_Existing_Open_Compensation_Record_Ignoring_Row_Value()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, "EMP-001", now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Hourly, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        // Row says "Annual" but the employee's existing open record is Hourly — the row value must be ignored.
        var stream = BuildWorkbook(("EMP-001", "50000", "Annual", "2027-01-01", "AnnualReview", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.Success, outcome.Type);
        var newRecord = await context.Compensations.SingleAsync(c => c.EffectiveFrom == new DateOnly(2027, 1, 1));
        Assert.Equal(SalaryType.Hourly, newRecord.SalaryType);
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationFailed_When_Effective_Date_Missing()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, "EMP-001", now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        // No effective date column value → row parses with a Reason set so it's not treated as blank.
        var stream = BuildWorkbookRaw(("EMP-001", "50000", "Annual", null, "NewHire", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Contains(outcome.RowErrors, e => e.Message.Contains("Effective Date"));
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationFailed_When_Reason_Invalid()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, "EMP-001", now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var stream = BuildWorkbook(("EMP-001", "50000", "Annual", "2027-01-01", "NotARealReason", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Contains(outcome.RowErrors, e => e.Message.Contains("Reason"));
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationFailed_For_Duplicate_Employee_And_Date_Pairs_In_File()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, "EMP-001", now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var stream = BuildWorkbook(
            ("EMP-001", "50000", "Annual", "2027-01-01", "NewHire", null),
            ("EMP-001", "51000", "Annual", "2027-01-01", "Correction", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Contains(outcome.RowErrors, e => e.Message.Contains("Duplicate row"));
        // No new rows were written — only the pre-existing seeded record remains.
        Assert.Equal([existing.Id], (await context.Compensations.ToListAsync()).Select(c => c.Id));
    }

    [Fact]
    public async Task HandleAsync_Returns_ValidationFailed_When_Overlap_Conflict_With_Existing_Record()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee = CreateEmployee(companyId, "EMP-001", now);
        context.Employees.Add(employee);

        var existing = Compensation.Create(Guid.NewGuid(), companyId, employee.Id, new DateOnly(2027, 1, 1), SalaryType.Annual, 40000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var stream = BuildWorkbook(("EMP-001", "45000", "Annual", "2027-01-01", "AnnualReview", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Single(outcome.RowErrors);

        // Existing record is untouched — the overlap conflict is caught before any writes occur.
        var unchanged = await context.Compensations.SingleAsync(c => c.Id == existing.Id);
        Assert.Null(unchanged.EffectiveTo);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Write_Any_Row_When_Any_Row_Is_Invalid()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee1 = CreateEmployee(companyId, "EMP-001", now);
        var employee2 = CreateEmployee(companyId, "EMP-002", now);
        context.Employees.AddRange(employee1, employee2);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, new FakeAuditPublisher());
        var stream = BuildWorkbook(
            ("EMP-001", "50000", "Annual", "2027-01-01", "NewHire", null),
            ("EMP-002", "not-a-number", "Annual", "2027-01-01", "NewHire", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.ValidationFailed, outcome.Type);
        Assert.Empty(await context.Compensations.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Imports_Valid_Rows_And_Publishes_Audit_Events_With_Shared_ImportBatchId()
    {
        await using var context = BuildContext();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var companyId = Guid.NewGuid();
        var employee1 = CreateEmployee(companyId, "EMP-001", now);
        var employee2 = CreateEmployee(companyId, "EMP-002", now);
        context.Employees.AddRange(employee1, employee2);

        var existingForEmployee1 = Compensation.Create(Guid.NewGuid(), companyId, employee1.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 40000m, "EUR", 35m, 0.9m, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existingForEmployee1);

        var existingForEmployee2 = Compensation.Create(Guid.NewGuid(), companyId, employee2.Id, new DateOnly(2025, 1, 1), SalaryType.Annual, 30000m, "GBP", null, null, null, CompensationChangeReason.NewHire, Guid.NewGuid(), now);
        context.Compensations.Add(existingForEmployee2);
        await context.SaveChangesAsync();

        var publisher = new FakeAuditPublisher();
        var handler = BuildHandler(context, publisher);
        var stream = BuildWorkbook(
            ("EMP-001", "46000", "Annual", "2027-01-01", "AnnualReview", "Raise"),
            ("EMP-002", "38000", "Annual", "2027-01-01", "NewHire", null));

        var outcome = await handler.HandleAsync(companyId, stream, ActorId, CancellationToken.None);

        Assert.Equal(ImportCompensationOutcomeType.Success, outcome.Type);
        Assert.Equal(2, outcome.Response!.Items.Count);

        var importBatchId = outcome.Response.ImportBatchId;
        Assert.NotEqual(Guid.Empty, importBatchId);

        // Employee1's existing record's currency/hours/fte should be carried over into its new record.
        var newRecordForEmployee1 = await context.Compensations.SingleAsync(c => c.EmployeeId == employee1.Id && c.EffectiveFrom == new DateOnly(2027, 1, 1));
        Assert.Equal("EUR", newRecordForEmployee1.Currency);
        Assert.Equal(35m, newRecordForEmployee1.HoursPerWeek);
        Assert.Equal(0.9m, newRecordForEmployee1.FTE);

        // Employee2's existing record's currency/hours/fte (GBP, null hours/fte) should be carried over.
        var newRecordForEmployee2 = await context.Compensations.SingleAsync(c => c.EmployeeId == employee2.Id && c.EffectiveFrom == new DateOnly(2027, 1, 1));
        Assert.Equal("GBP", newRecordForEmployee2.Currency);
        Assert.Null(newRecordForEmployee2.HoursPerWeek);
        Assert.Null(newRecordForEmployee2.FTE);

        var importedEvents = publisher.Published.OfType<CompensationRecordImportedAuditEvent>().ToList();
        Assert.Equal(2, importedEvents.Count);
        Assert.All(importedEvents, e => Assert.Equal(importBatchId, e.ImportBatchId));

        var closedEvents = publisher.Published.OfType<CompensationRecordClosedAuditEvent>().ToList();
        Assert.Equal(2, closedEvents.Count);
        Assert.Contains(closedEvents, e => e.CompensationRecordId == existingForEmployee1.Id);
        Assert.Contains(closedEvents, e => e.CompensationRecordId == existingForEmployee2.Id);
    }

    private static ImportCompensationChangesHandler BuildHandler(EmployeesDbContext context, FakeAuditPublisher publisher) =>
        new(context, new CompensationRecordWriter(context, new FakeClock(FixedUtcNow)), new FakeClock(FixedUtcNow), publisher);

    private static Employee CreateEmployee(Guid companyId, string employeeNumber, DateTimeOffset now) =>
        Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com", new DateOnly(2024, 1, 1), true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", employeeNumber, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);

    private static MemoryStream BuildWorkbook(params (string EmployeeNumber, string NewSalary, string SalaryFrequency, string EffectiveDate, string Reason, string? Notes)[] rows)
    {
        var raw = rows.Select(r => (r.EmployeeNumber, (string?)r.NewSalary, (string?)r.SalaryFrequency, (string?)r.EffectiveDate, (string?)r.Reason, r.Notes)).ToArray();
        return BuildWorkbookRaw(raw);
    }

    private static MemoryStream BuildWorkbookRaw(params (string EmployeeNumber, string? NewSalary, string? SalaryFrequency, string? EffectiveDate, string? Reason, string? Notes)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");

        sheet.Cell(1, 1).Value = "Employee Number";
        sheet.Cell(1, 2).Value = "New Salary";
        sheet.Cell(1, 3).Value = "Salary Frequency";
        sheet.Cell(1, 4).Value = "Effective Date";
        sheet.Cell(1, 5).Value = "Reason";
        sheet.Cell(1, 6).Value = "Notes";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.EmployeeNumber;
            if (row.NewSalary is not null) sheet.Cell(rowIndex, 2).Value = row.NewSalary;
            if (row.SalaryFrequency is not null) sheet.Cell(rowIndex, 3).Value = row.SalaryFrequency;
            if (row.EffectiveDate is not null) sheet.Cell(rowIndex, 4).Value = row.EffectiveDate;
            if (row.Reason is not null) sheet.Cell(rowIndex, 5).Value = row.Reason;
            if (row.Notes is not null) sheet.Cell(rowIndex, 6).Value = row.Notes;
            rowIndex++;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new EmployeesDbContext(options);
    }
}
