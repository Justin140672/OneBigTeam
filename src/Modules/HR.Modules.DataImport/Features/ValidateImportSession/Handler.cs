using System.Text.Json;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Features.ValidateImportSession;

internal sealed class ValidateImportSessionHandler(
    DataImportDbContext db,
    IImportFileStorageService storage,
    EmployeeImportFileParser parser,
    EmployeeStagingRowValidator rowValidator,
    IClock clock)
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
            parseResult = parser.Parse(fileStream, session.FileName, mapping);
        }
        catch (Exception ex)
        {
            session.Fail($"The file could not be read: {ex.Message}", clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);

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
                now);

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

        return Result.Success(new ValidateImportSessionResponse(
            session.Id,
            session.Status.ToString(),
            session.TotalRows,
            session.SuccessfulRows,
            session.FailedRows));
    }

    private static string? GetField(ParsedImportRow row, string field) =>
        row.Fields.TryGetValue(field, out var value) ? value : null;
}
