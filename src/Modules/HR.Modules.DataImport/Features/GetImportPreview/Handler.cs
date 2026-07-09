using System.Text.Json;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Features.GetImportPreview;

internal sealed class GetImportPreviewHandler(DataImportDbContext db)
{
    public async Task<Result<GetImportPreviewResponse>> HandleAsync(
        GetImportPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var session = await db.ImportSessions
            .SingleOrDefaultAsync(
                s => s.Id == request.ImportSessionId && s.CompanyId == request.CompanyId,
                cancellationToken);

        if (session is null)
        {
            return Result.Failure<GetImportPreviewResponse>(
                Error.NotFound($"Import session '{request.ImportSessionId}' was not found."));
        }

        var stagingRows = await db.ImportStagingEmployees
            .Where(s => s.ImportSessionId == session.Id && s.CompanyId == request.CompanyId)
            .OrderBy(s => s.RowNumber)
            .ToListAsync(cancellationToken);

        var rowErrors = await db.ImportRowErrors
            .Where(e => e.ImportSessionId == session.Id && e.CompanyId == request.CompanyId)
            .OrderBy(e => e.RowNumber)
            .ToListAsync(cancellationToken);

        var validRows = stagingRows
            .Where(s => s.IsValid)
            .Select(s => ToPreviewRow(s))
            .ToList();

        // Warning-severity row messages produced by the validator's lookup resolution step
        // consistently start with the entity kind followed by "did not exist and was created" —
        // treat those as the "reference data created" list; every other message (errors, and any
        // other warnings) goes into the general row-errors list.
        var referenceDataCreated = rowErrors
            .Where(e => e.Severity == ImportRowErrorSeverity.Warning && e.ErrorMessage.Contains("did not exist and was created"))
            .Select(e => new ImportPreviewRowError(e.RowNumber, e.Severity.ToString(), e.ErrorMessage))
            .ToList();

        var otherRowErrors = rowErrors
            .Where(e => !(e.Severity == ImportRowErrorSeverity.Warning && e.ErrorMessage.Contains("did not exist and was created")))
            .Select(e => new ImportPreviewRowError(e.RowNumber, e.Severity.ToString(), e.ErrorMessage))
            .ToList();

        var invalidRowCount = stagingRows.Count(s => !s.IsValid);

        return Result.Success(new GetImportPreviewResponse(
            session.Id,
            session.Status.ToString(),
            session.TotalRows,
            validRows.Count,
            invalidRowCount,
            validRows,
            otherRowErrors,
            referenceDataCreated));
    }

    private static ImportPreviewRow ToPreviewRow(ImportStagingEmployee staging)
    {
        Dictionary<string, string?> fields;
        try
        {
            fields = JsonSerializer.Deserialize<Dictionary<string, string?>>(staging.RawData) ?? [];
        }
        catch (JsonException)
        {
            fields = [];
        }

        return new ImportPreviewRow(
            staging.RowNumber,
            fields.GetValueOrDefault("FirstName"),
            fields.GetValueOrDefault("LastName"),
            staging.WorkEmail,
            fields.GetValueOrDefault("DepartmentName"),
            fields.GetValueOrDefault("LocationName"),
            fields.GetValueOrDefault("EmploymentTypeName"),
            fields.GetValueOrDefault("PositionProfileTitle"),
            staging.ManagerReference,
            fields.GetValueOrDefault("StartDate"));
    }
}
