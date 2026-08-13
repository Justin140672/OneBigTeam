using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.ImportCompensationChanges;

internal sealed class ImportCompensationChangesHandler(
    EmployeesDbContext dbContext,
    CompensationRecordWriter writer,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    IIntegrationEventPublisher integrationEventPublisher)
{
    private sealed record ValidatedRow(
        int RowNumber,
        Guid EmployeeId,
        string EmployeeNumber,
        decimal Salary,
        SalaryType SalaryType,
        DateOnly EffectiveDate,
        CompensationChangeReason Reason,
        string? Notes,
        string Currency,
        decimal? HoursPerWeek,
        decimal? Fte);

    public async Task<ImportCompensationChangesOutcome> HandleAsync(
        Guid companyId,
        Stream fileContent,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CompensationImportParsedRow> parsedRows;
        try
        {
            parsedRows = CompensationImportFileParser.Parse(fileContent);
        }
        catch (Exception)
        {
            return ImportCompensationChangesOutcome.InvalidFile(
                "The file could not be read as a valid .xlsx workbook.");
        }

        if (parsedRows.Count == 0)
            return ImportCompensationChangesOutcome.ValidationFailed(
                [new CompensationImportRowError(0, "The file contains no data rows.")]);

        // Employee.EmployeeNumber is always normalized to uppercase at write time (mirrors the
        // WorkEmail convention), so the searched values must be uppercased too before the
        // database comparison below — a raw .Contains() translates to a case-sensitive SQL IN,
        // and would silently match nothing for a row whose EmployeeNumber was typed in any other
        // case even though employeesByNumber's own lookup is case-insensitive.
        var employeeNumbers = parsedRows
            .Select(r => r.EmployeeNumber)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var employeesByNumber = await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && employeeNumbers.Contains(e.EmployeeNumber))
            .ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var currentOpenByEmployee = await dbContext.Compensations
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId &&
                        employeesByNumber.Values.Contains(c.EmployeeId) &&
                        c.EffectiveTo == null)
            .ToDictionaryAsync(c => c.EmployeeId, c => c, cancellationToken);

        var rowErrors = new List<CompensationImportRowError>();
        var validatedRows = new List<ValidatedRow>();
        var seenEmployeeDatePairs = new HashSet<(Guid EmployeeId, DateOnly EffectiveDate)>();

        foreach (var row in parsedRows)
        {
            var errors = new List<string>();
            var employeeId = Guid.Empty;

            if (string.IsNullOrWhiteSpace(row.EmployeeNumber))
            {
                errors.Add("Employee Number is required.");
            }
            else if (!employeesByNumber.TryGetValue(row.EmployeeNumber, out employeeId))
            {
                errors.Add($"Employee Number '{row.EmployeeNumber}' was not found.");
            }

            decimal salary = 0;
            if (string.IsNullOrWhiteSpace(row.NewSalary) || !decimal.TryParse(row.NewSalary, out salary) || salary <= 0)
                errors.Add("New Salary must be a number greater than 0.");

            // Salary Frequency is normally inherited from the employee's existing open compensation
            // record rather than taken from the uploaded row — HR can only change the salary amount
            // via bulk import for employees who already have a record, not the pay frequency. For an
            // employee with no existing record (e.g. a brand-new hire), there's nothing to inherit
            // from, so the row's own Salary Frequency column is used instead.
            var salaryType = default(SalaryType);
            if (employeeId != Guid.Empty)
            {
                if (currentOpenByEmployee.TryGetValue(employeeId, out var existingForFrequency))
                {
                    salaryType = existingForFrequency.SalaryType;
                }
                else if (string.IsNullOrWhiteSpace(row.SalaryFrequency) ||
                         !Enum.TryParse(row.SalaryFrequency, ignoreCase: true, out salaryType))
                {
                    errors.Add($"Salary Frequency must be one of: {string.Join(", ", Enum.GetNames<SalaryType>())}.");
                }
            }

            if (row.EffectiveDate is null)
            {
                errors.Add("Effective Date must be a valid date.");
            }
            else if (row.EffectiveDate.Value.Year < 1900 || row.EffectiveDate.Value.Year > 2200)
            {
                errors.Add("Effective Date is out of the acceptable range.");
            }

            var reason = default(CompensationChangeReason);
            var hasReason = !string.IsNullOrWhiteSpace(row.Reason) &&
                             Enum.TryParse(row.Reason, ignoreCase: true, out reason);
            if (!hasReason)
                errors.Add($"Reason must be one of: {string.Join(", ", Enum.GetNames<CompensationChangeReason>())}.");

            if (errors.Count == 0 && row.EffectiveDate is not null)
            {
                var key = (employeeId, row.EffectiveDate.Value);
                if (!seenEmployeeDatePairs.Add(key))
                    errors.Add(
                        "Duplicate row: this employee already has another row in this file with the same Effective Date.");
            }

            if (errors.Count > 0)
            {
                rowErrors.Add(new CompensationImportRowError(row.RowNumber, string.Join(" ", errors)));
                continue;
            }

            currentOpenByEmployee.TryGetValue(employeeId, out var existing);

            validatedRows.Add(new ValidatedRow(
                row.RowNumber,
                employeeId,
                row.EmployeeNumber,
                salary,
                salaryType,
                row.EffectiveDate!.Value,
                reason,
                row.Notes,
                existing?.Currency ?? "GBP",
                existing?.HoursPerWeek,
                existing?.FTE));
        }

        if (rowErrors.Count > 0)
            return ImportCompensationChangesOutcome.ValidationFailed(rowErrors);

        var importBatchId = Guid.NewGuid();
        var items = new List<ImportedCompensationItem>();
        var pendingAuditEvents = new List<(ValidatedRow Row, Compensation Record, Compensation? Previous)>();

        // Entire import is written in a single transaction: every row has already been validated
        // above (including intra-file duplicate checks), and CompensationRecordWriter re-validates
        // the overlap-with-existing-records rule as each row is written. Any write failure rolls
        // back the whole import — nothing is left partially applied.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var row in validatedRows)
        {
            var writeResult = await writer.WriteAsync(
                companyId,
                row.EmployeeId,
                row.EffectiveDate,
                row.SalaryType,
                row.Salary,
                row.Currency,
                row.HoursPerWeek,
                row.Fte,
                row.Notes,
                row.Reason,
                actorEmployeeId,
                cancellationToken);

            if (writeResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ImportCompensationChangesOutcome.ValidationFailed(
                    [new CompensationImportRowError(row.RowNumber, writeResult.Error.Message)]);
            }

            var (record, previous) = writeResult.Value!;

            items.Add(new ImportedCompensationItem(row.EmployeeId, row.EmployeeNumber, record.Id, record.Salary, record.EffectiveFrom));
            pendingAuditEvents.Add((row, record, previous));
        }

        await transaction.CommitAsync(cancellationToken);

        var now = clock.UtcNowOffset();

        foreach (var (row, record, previous) in pendingAuditEvents)
        {
            if (previous is not null)
            {
                await auditEventPublisher.PublishAsync(
                    new CompensationRecordClosedAuditEvent(
                        companyId, row.EmployeeId, previous.Id, actorEmployeeId,
                        previous.EffectiveFrom, previous.EffectiveTo!.Value, now),
                    cancellationToken);
            }

            await auditEventPublisher.PublishAsync(
                new CompensationRecordImportedAuditEvent(
                    companyId, row.EmployeeId, record.Id, actorEmployeeId, record.EffectiveFrom,
                    record.SalaryType.ToString(), record.Salary, record.Currency, record.Reason.ToString(),
                    importBatchId, now),
                cancellationToken);

            await integrationEventPublisher.PublishAsync(
                new CompensationChangedIntegrationEvent(
                    companyId, row.EmployeeId, record.Id, record.EffectiveFrom,
                    record.SalaryType.ToString(), record.Reason.ToString(), now),
                cancellationToken);
        }

        return ImportCompensationChangesOutcome.Success(new ImportCompensationChangesResponse(importBatchId, items));
    }
}
