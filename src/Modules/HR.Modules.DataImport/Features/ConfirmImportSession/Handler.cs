using System.Globalization;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Features.ConfirmImportSession;

/// <summary>
/// Creates employees from a validated import session's staging rows. Per-row failures are
/// caught and recorded as new ImportRowError rows so a partial success (some rows created,
/// some failed) is possible — mirroring ValidateImportSession's tolerance model. Manager
/// resolution happens in a second pass after all rows have been created, so a row can reference
/// a manager created earlier in the same file.
/// </summary>
internal sealed class ConfirmImportSessionHandler(
    DataImportDbContext db,
    IEmployeeImportWriter employeeWriter,
    ILeaveImportWriter leaveWriter,
    IEmployeeImportLookupReader lookupReader,
    IIntegrationEventPublisher integrationEventPublisher,
    IClock clock)
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy"];

    public async Task<Result<ConfirmImportSessionResponse>> HandleAsync(
        ConfirmImportSessionRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions
            .SingleOrDefaultAsync(
                s => s.Id == request.ImportSessionId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (session is null)
        {
            return Result.Failure<ConfirmImportSessionResponse>(
                Error.NotFound($"Import session '{request.ImportSessionId}' was not found."));
        }

        if (session.Status is not (ImportStatus.Validated or ImportStatus.CompletedWithErrors))
        {
            return Result.Failure<ConfirmImportSessionResponse>(
                Error.Conflict($"Import session '{request.ImportSessionId}' is not in a confirmable state (status: {session.Status})."));
        }

        var stagingRows = await db.ImportStagingEmployees
            .Where(s => s.ImportSessionId == session.Id && s.CompanyId == request.CompanyId && s.IsValid)
            .OrderBy(s => s.RowNumber)
            .ToListAsync(cancellationToken);

        if (stagingRows.Count == 0)
        {
            return Result.Failure<ConfirmImportSessionResponse>(
                Error.Conflict($"Import session '{request.ImportSessionId}' has no valid rows to confirm."));
        }

        var createdCount = 0;
        var failedCount = 0;
        var createdByRow = new Dictionary<int, (Guid EmployeeId, string? ManagerReference)>();

        foreach (var row in stagingRows)
        {
            try
            {
                var fields = ParseRawData(row.RawData);

                var createResult = await employeeWriter.CreateEmployeeAsync(
                    new EmployeeImportCreateRequest(
                        Guid.NewGuid(),
                        request.CompanyId,
                        GetRequired(fields, "FirstName"),
                        GetRequired(fields, "LastName"),
                        fields.GetValueOrDefault("PreferredName"),
                        GetRequired(fields, "WorkEmail"),
                        fields.GetValueOrDefault("PersonalEmail"),
                        ParseDate(fields.GetValueOrDefault("StartDate"))!.Value,
                        ParseDate(fields.GetValueOrDefault("DateOfBirth"))!.Value,
                        GetRequired(fields, "Nationality"),
                        GetRequired(fields, "Gender"),
                        row.DepartmentId!.Value,
                        row.LocationId!.Value,
                        row.EmploymentTypeId!.Value,
                        row.PositionProfileId!.Value,
                        row.EmployeeNumber!,
                        session.Id,
                        actorUserId),
                    cancellationToken);

                var workingDaysRaw = fields.GetValueOrDefault("WorkingDays");
                var hoursPerDayRaw = fields.GetValueOrDefault("HoursPerDay");
                if (!string.IsNullOrWhiteSpace(workingDaysRaw) || !string.IsNullOrWhiteSpace(hoursPerDayRaw))
                {
                    await employeeWriter.SetWorkingPatternAsync(
                        request.CompanyId,
                        createResult.EmployeeId,
                        new EmployeeImportWorkingPattern(
                            ParseWorkingDays(workingDaysRaw),
                            ParseDecimal(hoursPerDayRaw)),
                        cancellationToken);
                }

                var salaryAmountRaw = fields.GetValueOrDefault("SalaryAmount");
                if (!string.IsNullOrWhiteSpace(salaryAmountRaw))
                {
                    await employeeWriter.CreateOpeningCompensationAsync(
                        request.CompanyId,
                        createResult.EmployeeId,
                        createResult.StartDate,
                        new EmployeeImportCompensation(
                            decimal.Parse(salaryAmountRaw, CultureInfo.InvariantCulture),
                            fields.GetValueOrDefault("SalaryType") ?? "Annual",
                            fields.GetValueOrDefault("Currency") ?? "GBP",
                            ParseDecimal(fields.GetValueOrDefault("HoursPerWeek")),
                            ParseDecimal(fields.GetValueOrDefault("FTE"))),
                        cancellationToken);
                }

                // Publishes synchronously in-process; InitialiseEmployeeLeave's handler will
                // have created the baseline leave balances by the time PublishAsync returns.
                await integrationEventPublisher.PublishAsync(new EmployeeCreatedIntegrationEvent(
                    request.CompanyId,
                    createResult.EmployeeId,
                    createResult.StartDate,
                    createResult.ManagerId,
                    createResult.ProbationEndDate,
                    createResult.PositionProfileId,
                    createResult.DefaultLeavePolicyId,
                    IsImported: true), cancellationToken);

                await integrationEventPublisher.PublishAsync(new EmployeeImportedIntegrationEvent(
                    request.CompanyId, createResult.EmployeeId, session.Id, row.RowNumber), cancellationToken);

                var leaveTypeCode = fields.GetValueOrDefault("LeaveTypeCode");
                var leaveBalanceDaysRaw = fields.GetValueOrDefault("LeaveBalanceDays");
                if (!string.IsNullOrWhiteSpace(leaveTypeCode) && !string.IsNullOrWhiteSpace(leaveBalanceDaysRaw))
                {
                    await leaveWriter.TryLayOpeningBalanceAsync(
                        request.CompanyId,
                        createResult.EmployeeId,
                        leaveTypeCode,
                        decimal.Parse(leaveBalanceDaysRaw, CultureInfo.InvariantCulture),
                        actorUserId,
                        cancellationToken);
                }

                createdByRow[row.RowNumber] = (createResult.EmployeeId, row.ManagerReference);
                createdCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                db.ImportRowErrors.Add(ImportRowError.Create(
                    Guid.NewGuid(),
                    request.CompanyId,
                    session.Id,
                    row.RowNumber,
                    ImportRowErrorSeverity.Error,
                    $"Failed to create employee: {ex.Message}",
                    row.RawData,
                    clock.UtcNowOffset()));
            }
        }

        // Second pass: manager resolution, now that every row's employee (if created
        // successfully) exists. A manager reference can point at another row in this same file
        // or at a pre-existing employee.
        foreach (var (rowNumber, (employeeId, managerReference)) in createdByRow)
        {
            if (string.IsNullOrWhiteSpace(managerReference))
                continue;

            var normalizedReference = managerReference.Trim();

            Guid? managerId = null;

            var matchInFile = stagingRows.FirstOrDefault(r =>
                r.RowNumber != rowNumber &&
                createdByRow.ContainsKey(r.RowNumber) &&
                (string.Equals(r.EmployeeNumber?.Trim(), normalizedReference, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(r.WorkEmail?.Trim(), normalizedReference, StringComparison.OrdinalIgnoreCase)));

            if (matchInFile is not null)
                managerId = createdByRow[matchInFile.RowNumber].EmployeeId;

            managerId ??= await lookupReader.FindEmployeeIdByReferenceAsync(
                request.CompanyId, normalizedReference, cancellationToken);

            if (managerId is null)
                continue;

            var assigned = await employeeWriter.TryAssignManagerAsync(
                request.CompanyId, employeeId, managerId.Value, cancellationToken);

            if (!assigned)
            {
                db.ImportRowErrors.Add(ImportRowError.Create(
                    Guid.NewGuid(),
                    request.CompanyId,
                    session.Id,
                    rowNumber,
                    ImportRowErrorSeverity.Warning,
                    $"Manager reference '{managerReference}' could not be assigned (manager not found, terminated, or would create a circular hierarchy). The employee was created without a manager.",
                    null,
                    clock.UtcNowOffset()));
            }
        }

        session.Confirm(createdCount, failedCount, clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ConfirmImportSessionResponse(
            session.Id, session.Status.ToString(), createdCount, failedCount));
    }

    private static Dictionary<string, string?> ParseRawData(string rawData)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(rawData) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string GetRequired(Dictionary<string, string?> fields, string key) =>
        fields.GetValueOrDefault(key) ?? throw new InvalidOperationException($"'{key}' is missing from staged row data.");

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (DateOnly.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;

        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseDecimal(string? value) =>
        !string.IsNullOrWhiteSpace(value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static WorkingDays? ParseWorkingDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var days = WorkingDays.None;
        foreach (var name in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<WorkingDays>(name, ignoreCase: true, out var day))
                days |= day;
        }

        return days == WorkingDays.None ? null : days;
    }
}
