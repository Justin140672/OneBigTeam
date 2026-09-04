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

        // Re-confirming a session that already finished with errors is only meaningful as a retry of
        // valid rows that a previous attempt did not turn into an employee (a transient failure, or
        // the "fix the data and retry" flow). If every valid row was already created, a second
        // confirm would just re-run manager resolution and re-stamp the session — reject it as a
        // conflict so a double-submit cannot be mistaken for fresh progress. (This is done before
        // ClaimForConfirmation so the early return does not strand the session in Processing.)
        if (session.Status == ImportStatus.CompletedWithErrors)
        {
            var hasRetryableRows = await db.ImportStagingEmployees.AnyAsync(
                s => s.ImportSessionId == session.Id
                    && s.CompanyId == request.CompanyId
                    && s.IsValid
                    && s.FullyConfirmedAt == null,
                cancellationToken);

            if (!hasRetryableRows)
            {
                return Result.Failure<ConfirmImportSessionResponse>(
                    Error.Conflict($"Import session '{request.ImportSessionId}' has already been confirmed; there are no remaining rows to retry."));
            }
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

        // OBT-REM-08: rows that are not YET fully confirmed are (re)processed — this includes rows
        // that never got an employee created at all, and rows whose employee exists but one or
        // more downstream steps (integration events, opening leave balance, manager assignment)
        // did not complete on a previous attempt. A row is never re-created once CreatedEmployeeId
        // is set; only its remaining incomplete steps are resumed.
        var stagingRows = allValidRows.Where(s => !s.IsFullyConfirmed).ToList();

        var rowsByNumber = allValidRows.ToDictionary(r => r.RowNumber);

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

        // Seed with every row that already has an employee (from this run or an earlier partial
        // run) so this run's manager resolution can still point at them, and the response reflects
        // the full picture.
        foreach (var confirmed in allValidRows.Where(r => r.CreatedEmployeeId is not null))
        {
            createdByRow[confirmed.RowNumber] = (confirmed.CreatedEmployeeId!.Value, confirmed.ManagerReference);
            createdRowResults.Add(new ConfirmImportSessionRowResult(
                confirmed.RowNumber, confirmed.CreatedEmployeeId.Value, confirmed.EmployeeNumber ?? string.Empty));
        }

        foreach (var row in stagingRows)
        {
            try
            {
                var fields = ParseRawData(row.RawData);

                EmployeeImportCreateResult createResult;

                if (row.CreatedEmployeeId is null)
                {
                    // Reference data (new departments/locations/employment types/position
                    // profiles) is deliberately NOT created during preview/validation — only
                    // here, at confirm time, once the user has reviewed the preview and clicked
                    // Confirm. The staging row's DepartmentId/LocationId/EmploymentTypeId/
                    // PositionProfileId may therefore be null even for an otherwise-valid row;
                    // resolve (get-or-create) them by name now.
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
                    createResult = row.ExistingEmployeeIdToUpdate is not null
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

                    // OBT-REM-08: record the durable per-row "employee created" step BEFORE
                    // publishing integration events or laying the opening leave balance. A crash
                    // between here and those later steps leaves the row's remaining steps
                    // incomplete (not the whole row "confirmed"), so a retry resumes exactly the
                    // steps that did not finish instead of creating the employee a second time OR
                    // silently losing the events/balance.
                    row.MarkEmployeeCreated(createResult.EmployeeId, clock.UtcNowOffset());
                    await db.SaveChangesAsync(cancellationToken);

                    createdByRow[row.RowNumber] = (createResult.EmployeeId, row.ManagerReference);
                    createdRowResults.Add(new ConfirmImportSessionRowResult(
                        row.RowNumber, createResult.EmployeeId, createResult.EmployeeNumber));
                }
                else
                {
                    // Employee already created by an earlier (partial) attempt — resume without
                    // creating it again. Read back the data needed to (re)publish events from the
                    // Employees module rather than re-deriving it from the raw staging row.
                    createResult = await employeeWriter.GetImportSnapshotAsync(
                        request.CompanyId, row.CreatedEmployeeId.Value, cancellationToken)
                        ?? throw new InvalidOperationException(
                            $"Employee '{row.CreatedEmployeeId}' recorded against row {row.RowNumber} could not be found.");
                }

                // Each downstream step is independently resumable: only run (and only persist) a
                // step that did not already complete for this row.
                if (row.EmployeeCreatedEventPublishedAt is null)
                {
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

                    row.MarkEmployeeCreatedEventPublished(clock.UtcNowOffset());
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (row.EmployeeImportedEventPublishedAt is null)
                {
                    await integrationEventPublisher.PublishAsync(new EmployeeImportedIntegrationEvent(
                        request.CompanyId, createResult.EmployeeId, session.Id, row.RowNumber), cancellationToken);

                    row.MarkEmployeeImportedEventPublished(clock.UtcNowOffset());
                    await db.SaveChangesAsync(cancellationToken);
                }

                if (row.OpeningLeaveBalanceProcessedAt is null)
                {
                    // Leave Type Code was removed from the import template — Annual Leave is the
                    // only leave type an import ever sets an opening balance for, so it's
                    // hardcoded here rather than asking the user to specify a type.
                    // TryLayOpeningBalanceAsync computes the adjustment as a delta from the
                    // employee's current balance, so calling it again on retry (or when there is
                    // nothing to apply) is a safe no-op.
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

                    row.MarkOpeningLeaveBalanceProcessed(clock.UtcNowOffset());
                    await db.SaveChangesAsync(cancellationToken);
                }
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
        // or at a pre-existing employee. Only rows whose manager-assignment step has not already
        // completed are (re)processed — this keeps a retry from repeatedly re-resolving managers
        // for rows that finished this step on an earlier attempt.
        foreach (var (rowNumber, (employeeId, managerReference)) in createdByRow)
        {
            var stagingRow = rowsByNumber[rowNumber];
            if (stagingRow.ManagerAssignmentProcessedAt is not null)
                continue;

            if (string.IsNullOrWhiteSpace(managerReference))
            {
                stagingRow.MarkManagerAssignmentProcessed(clock.UtcNowOffset());
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

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
            {
                stagingRow.MarkManagerAssignmentProcessed(clock.UtcNowOffset());
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

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

            stagingRow.MarkManagerAssignmentProcessed(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
        }

        // A row is fully confirmed only once every mandatory step has completed. This must be
        // computed after both passes above, since manager assignment (pass two) can be the last
        // outstanding step for a row that already had its employee/events/leave-balance steps
        // done on an earlier attempt.
        var nowFinal = clock.UtcNowOffset();
        var rowsToFinalize = allValidRows.Where(r =>
            !r.IsFullyConfirmed
            && r.CreatedEmployeeId is not null
            && r.EmployeeCreatedEventPublishedAt is not null
            && r.EmployeeImportedEventPublishedAt is not null
            && r.OpeningLeaveBalanceProcessedAt is not null
            && r.ManagerAssignmentProcessedAt is not null);

        foreach (var row in rowsToFinalize)
            row.MarkFullyConfirmed(nowFinal);

        await db.SaveChangesAsync(cancellationToken);

        // Recompute cumulative counts from fully-completed rows (not merely rows with an employee
        // id), so a resumed/retried confirmation reports the whole session's true outcome and a
        // row with dangling downstream work is never counted as a success.
        var confirmedTotal = await db.ImportStagingEmployees
            .CountAsync(s => s.ImportSessionId == session.Id && s.FullyConfirmedAt != null, cancellationToken);
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
