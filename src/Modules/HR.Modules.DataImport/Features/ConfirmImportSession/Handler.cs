using System.Globalization;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.Modules.Employees.Contracts;
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
    IImportLookupResolver lookupResolver,
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

        var now = clock.UtcNowOffset();

        // OBT-REM-06: a session is confirmable when validated, or when a previous confirm attempt
        // finished with errors (a legitimate "fix the data / transient failure — retry" flow: the
        // retry only reprocesses rows that were not already turned into employees), or when a prior
        // run claimed it but appears to have crashed (Processing, but stale).
        const int staleClaimMinutes = 15;
        var claimIsStale = session.Status == ImportStatus.Processing
            && session.StartedAt is { } startedAt
            && now - startedAt > TimeSpan.FromMinutes(staleClaimMinutes);

        var confirmable = session.Status is ImportStatus.Validated or ImportStatus.CompletedWithErrors
            || claimIsStale;

        if (!confirmable)
        {
            return Result.Failure<ConfirmImportSessionResponse>(
                Error.Conflict($"Import session '{request.ImportSessionId}' is not in a confirmable state (status: {session.Status})."));
        }

        // Atomically claim the session before doing any work. The xmin concurrency token means a
        // second simultaneous confirmation loses this save and is rejected with a conflict.
        session.ClaimForConfirmation(now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<ConfirmImportSessionResponse>(
                Error.Conflict($"Import session '{request.ImportSessionId}' is already being confirmed."));
        }

        var allValidRows = await db.ImportStagingEmployees
            .Where(s => s.ImportSessionId == session.Id && s.CompanyId == request.CompanyId && s.IsValid)
            .OrderBy(s => s.RowNumber)
            .ToListAsync(cancellationToken);

        if (allValidRows.Count == 0)
        {
            return Result.Failure<ConfirmImportSessionResponse>(
                Error.Conflict($"Import session '{request.ImportSessionId}' has no valid rows to confirm."));
        }

        // Only rows that have not already been turned into an employee are (re)processed.
        var stagingRows = allValidRows.Where(s => s.CreatedEmployeeId is null).ToList();

        // Clear any confirm-phase error rows from a previous attempt for the rows we are about to
        // retry, so cumulative counts and the error list do not double-count across retries.
        var rowNumbersToProcess = stagingRows.Select(s => s.RowNumber).ToHashSet();
        if (rowNumbersToProcess.Count > 0)
        {
            var staleErrors = await db.ImportRowErrors
                .Where(e => e.ImportSessionId == session.Id && rowNumbersToProcess.Contains(e.RowNumber))
                .ToListAsync(cancellationToken);
            db.ImportRowErrors.RemoveRange(staleErrors);
        }

        // Rows that failed the earlier Validate step never enter the loop below (only IsValid
        // staging rows do), so they'd otherwise vanish from this step's own failedCount entirely
        // — session.Confirm below overwrites FailedRows rather than adding to it, so without this
        // a file with some already-invalid rows would incorrectly report ImportStatus.Imported
        // (0 failures) once every row that WAS valid is confirmed successfully.
        var alreadyInvalidRowCount = await db.ImportStagingEmployees
            .CountAsync(s => s.ImportSessionId == session.Id && s.CompanyId == request.CompanyId && !s.IsValid, cancellationToken);

        var createdByRow = new Dictionary<int, (Guid EmployeeId, string? ManagerReference)>();
        var createdRowResults = new List<ConfirmImportSessionRowResult>();

        // Seed with rows already confirmed by an earlier (partial) run so this run's manager
        // resolution can still point at them, and the response reflects the full picture.
        foreach (var confirmed in allValidRows.Where(r => r.CreatedEmployeeId is not null))
        {
            createdByRow[confirmed.RowNumber] = (confirmed.CreatedEmployeeId!.Value, confirmed.ManagerReference);
            createdRowResults.Add(new ConfirmImportSessionRowResult(
                confirmed.RowNumber, confirmed.CreatedEmployeeId.Value, confirmed.EmployeeNumber));
        }

        foreach (var row in stagingRows)
        {
            try
            {
                var fields = ParseRawData(row.RawData);

                // Reference data (new departments/locations/employment types/position profiles) is
                // deliberately NOT created during preview/validation — only here, at confirm time,
                // once the user has reviewed the preview and clicked Confirm. The staging row's
                // DepartmentId/LocationId/EmploymentTypeId/PositionProfileId may therefore be null
                // even for an otherwise-valid row; resolve (get-or-create) them by name now.
                var departmentId = row.DepartmentId ?? (await lookupResolver.GetOrCreateDepartmentAsync(
                    request.CompanyId, GetRequired(fields, "DepartmentName"), cancellationToken)).Id;

                var locationId = row.LocationId ?? (await lookupResolver.GetOrCreateLocationAsync(
                    request.CompanyId, GetRequired(fields, "LocationName"), cancellationToken)).Id;

                var employmentTypeId = row.EmploymentTypeId ?? (await lookupResolver.GetOrCreateEmploymentTypeAsync(
                    request.CompanyId, GetRequired(fields, "EmploymentTypeName"), cancellationToken)).Id;

                Guid positionProfileId;
                if (row.PositionProfileId is not null)
                {
                    positionProfileId = row.PositionProfileId.Value;
                }
                else
                {
                    var positionProfileResult = await lookupResolver.GetOrCreatePositionProfileAsync(
                        request.CompanyId, GetRequired(fields, "PositionProfileTitle"), departmentId, locationId, cancellationToken);

                    if (positionProfileResult.Skipped || positionProfileResult.Id is null)
                        throw new InvalidOperationException($"Position Profile '{GetRequired(fields, "PositionProfileTitle")}' could not be created.");

                    positionProfileId = positionProfileResult.Id.Value;
                }

                var createRequest = new EmployeeImportCreateRequest(
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
                    departmentId,
                    locationId,
                    employmentTypeId,
                    positionProfileId,
                    row.EmployeeNumber,
                    session.Id,
                    actorUserId,
                    fields.GetValueOrDefault("Address"),
                    ParseDate(fields.GetValueOrDefault("ProbationEndDate")));

                // Rows whose Work Email matched the company's seed admin employee (see
                // Employee.IsInitialCompanyAdmin) update that existing employee rather than
                // creating a duplicate — this is the ONLY case where import ever updates an
                // existing employee (see EmployeeStagingRowValidator's remarks).
                var createResult = row.ExistingEmployeeIdToUpdate is not null
                    ? await employeeWriter.UpdateEmployeeAsync(row.ExistingEmployeeIdToUpdate.Value, createRequest, cancellationToken)
                    : await employeeWriter.CreateEmployeeAsync(createRequest, cancellationToken);

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
                            fields.GetValueOrDefault("Currency") ?? "GBP"),
                        cancellationToken);
                }

                // OBT-REM-06: record the durable per-row confirmation state (and clear the tracked
                // employee-writer work) BEFORE publishing integration events. A crash between here
                // and the publish leaves the row marked confirmed, so a retry will not create the
                // employee a second time — the far more damaging outcome than a re-runnable
                // downstream projection gap.
                row.MarkConfirmed(createResult.EmployeeId, clock.UtcNowOffset());
                await db.SaveChangesAsync(cancellationToken);

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

                // Leave Type Code was removed from the import template — Annual Leave is the only
                // leave type an import ever sets an opening balance for, so it's hardcoded here
                // rather than asking the user to specify a type.
                var leaveBalanceDaysRaw = fields.GetValueOrDefault("LeaveBalanceDays");
                if (!string.IsNullOrWhiteSpace(leaveBalanceDaysRaw))
                {
                    await leaveWriter.TryLayOpeningBalanceAsync(
                        request.CompanyId,
                        createResult.EmployeeId,
                        "Annual Leave",
                        decimal.Parse(leaveBalanceDaysRaw, CultureInfo.InvariantCulture),
                        actorUserId,
                        cancellationToken);
                }

                createdByRow[row.RowNumber] = (createResult.EmployeeId, row.ManagerReference);
                createdRowResults.Add(new ConfirmImportSessionRowResult(
                    row.RowNumber, createResult.EmployeeId, createResult.EmployeeNumber));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The DataImport DbContext only tracks the session + staging rows + error rows; a
                // failure inside the (separately-scoped) employee writer does not dirty it, so the
                // error row can be persisted immediately for durability.
                db.ImportRowErrors.Add(ImportRowError.Create(
                    Guid.NewGuid(),
                    request.CompanyId,
                    session.Id,
                    row.RowNumber,
                    ImportRowErrorSeverity.Error,
                    $"Failed to create employee: {ex.Message}",
                    row.RawData,
                    clock.UtcNowOffset()));
                await db.SaveChangesAsync(cancellationToken);
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

            var matchInFile = allValidRows.FirstOrDefault(r =>
                r.RowNumber != rowNumber &&
                createdByRow.ContainsKey(r.RowNumber) &&
                (string.Equals(r.EmployeeNumber?.Trim(), normalizedReference, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(r.WorkEmail?.Trim(), normalizedReference, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     $"{ParseRawData(r.RawData).GetValueOrDefault("FirstName")?.Trim()} {ParseRawData(r.RawData).GetValueOrDefault("LastName")?.Trim()}",
                     normalizedReference,
                     StringComparison.OrdinalIgnoreCase)));

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

        // Recompute cumulative counts from the durable per-row state rather than this run's deltas,
        // so a resumed/retried confirmation reports the whole session's outcome.
        var confirmedTotal = await db.ImportStagingEmployees
            .CountAsync(s => s.ImportSessionId == session.Id && s.CreatedEmployeeId != null, cancellationToken);
        var validTotal = allValidRows.Count;
        var failedTotal = (validTotal - confirmedTotal) + alreadyInvalidRowCount;

        session.Confirm(confirmedTotal, failedTotal, clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ConfirmImportSessionResponse(
            session.Id, session.Status.ToString(), confirmedTotal, failedTotal,
            createdRowResults.OrderBy(r => r.RowNumber).ToList()));
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
