using System.Text.Json;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.DataImport.Features.ValidateImportSession;

internal sealed class ValidateImportSessionHandler(
    DataImportDbContext db,
    IImportFileStorageService storage,
    EmployeeImportFileParser parser,
    EmployeeStagingRowValidator rowValidator,
    IClock clock,
    ILogger<ValidateImportSessionHandler> logger)
{
    public async Task<Result<ValidateImportSessionResponse>> HandleAsync(
        ValidateImportSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions
            .SingleOrDefaultAsync(
                s => s.Id == request.ImportSessionId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (session is null)
        {
            return Result.Failure<ValidateImportSessionResponse>(
                Error.NotFound($"Import session '{request.ImportSessionId}' was not found."));
        }

        if (session.Status != ImportStatus.Pending)
        {
            return Result.Failure<ValidateImportSessionResponse>(
                Error.Conflict($"Import session '{request.ImportSessionId}' has already been processed (status: {session.Status})."));
        }

        var now = clock.UtcNowOffset();
        session.Start(now);
        await db.SaveChangesAsync(cancellationToken);

        EmployeeImportParseResult parseResult;

        try
        {
            var mapping = StandardEmployeeColumnMapping.Default.WithOverrides(request.ColumnMapping);

            await using var fileStream = await storage.OpenReadAsync(session.StorageKey, cancellationToken);
            parseResult = parser.Parse(fileStream, mapping);
        }
        catch (Exception ex)
        {
            session.Fail($"The file could not be read: {ex.Message}", clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);

            // Security review finding #2: the raw file is never read again once validation has
            // failed, so delete it immediately (best-effort); PurgeImportSessionFilesJob is the
            // durable safety net if this inline attempt itself fails or the process crashes
            // before it runs.
            await TryDeleteSessionFileAsync(session, cancellationToken);

            return Result.Failure<ValidateImportSessionResponse>(
                Error.Validation($"The file could not be read: {ex.Message}"));
        }

        var validationResults = await rowValidator.ValidateAsync(
            request.CompanyId,
            parseResult.Rows,
            parseResult.MappedFields,
            cancellationToken);

        var resultsByRow = validationResults.ToDictionary(r => r.RowNumber);

        var successfulRows = 0;
        var failedRows = 0;

        foreach (var row in parseResult.Rows)
        {
            var validation = resultsByRow[row.RowNumber];
            var isValid = validation.IsValid;

            if (isValid)
                successfulRows++;
            else
                failedRows++;

            var rawDataJson = JsonSerializer.Serialize(row.Fields);

            var staging = ImportStagingEmployee.Create(
                Guid.NewGuid(),
                request.CompanyId,
                session.Id,
                row.RowNumber,
                GetField(row, "EmployeeNumber"),
                GetField(row, "WorkEmail"),
                GetField(row, "ManagerReference"),
                validation.DepartmentId,
                validation.LocationId,
                validation.EmploymentTypeId,
                validation.PositionProfileId,
                rawDataJson,
                isValid,
                now,
                validation.ExistingEmployeeIdToUpdate);

            db.ImportStagingEmployees.Add(staging);

            foreach (var error in validation.Errors)
            {
                var rowError = ImportRowError.Create(
                    Guid.NewGuid(),
                    request.CompanyId,
                    session.Id,
                    row.RowNumber,
                    ImportRowErrorSeverity.Error,
                    error,
                    rawDataJson,
                    now);

                db.ImportRowErrors.Add(rowError);
            }

            foreach (var warning in validation.Warnings)
            {
                var rowWarning = ImportRowError.Create(
                    Guid.NewGuid(),
                    request.CompanyId,
                    session.Id,
                    row.RowNumber,
                    ImportRowErrorSeverity.Warning,
                    warning,
                    rawDataJson,
                    now);

                db.ImportRowErrors.Add(rowWarning);
            }
        }

        session.Validate(successfulRows, failedRows, clock.UtcNowOffset());

        await db.SaveChangesAsync(cancellationToken);

        // Security review finding #2: ConfirmImportSession works entirely from the staging rows
        // persisted above, never from the raw workbook, so the file is no longer needed by the
        // workflow the moment validation completes — delete it now rather than waiting for the
        // sweep job. Best-effort: a failure here just leaves FileDeletedAt unset for
        // PurgeImportSessionFilesJob to retry.
        await TryDeleteSessionFileAsync(session, cancellationToken);

        return Result.Success(new ValidateImportSessionResponse(
            session.Id,
            session.Status.ToString(),
            session.TotalRows,
            session.SuccessfulRows,
            session.FailedRows));
    }

    private static string? GetField(ParsedImportRow row, string field) =>
        row.Fields.TryGetValue(field, out var value) ? value : null;

    /// <summary>
    /// Best-effort, idempotent, retryable deletion of a session's raw uploaded file. Deletion
    /// status (FileDeletedAt / FileDeletionAttemptCount) is recorded on the session separately
    /// from its business Status, so this can safely run again from here or from
    /// PurgeImportSessionFilesJob without risk of double-processing.
    /// </summary>
    private async Task TryDeleteSessionFileAsync(ImportSession session, CancellationToken cancellationToken)
    {
        if (session.FileDeletedAt is not null || string.IsNullOrWhiteSpace(session.StorageKey))
            return;

        try
        {
            await storage.DeleteAsync(session.StorageKey, cancellationToken);
            session.MarkFileDeleted(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Not treated as a hard failure — deletion is a cleanup concern, not a business
            // outcome. PurgeImportSessionFilesJob retries and logs an exhausted-attempts error if
            // this keeps failing.
            logger.LogWarning(ex,
                "Failed to delete import session {ImportSessionId} raw file (storage key {StorageKey}) inline after validation; will retry via the sweep job.",
                session.Id, session.StorageKey);

            session.RecordFileDeletionAttemptFailed(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
