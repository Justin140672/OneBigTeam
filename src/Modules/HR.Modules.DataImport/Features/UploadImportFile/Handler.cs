using ClosedXML.Excel;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.SharedKernel;

namespace HR.Modules.DataImport.Features.UploadImportFile;

internal sealed class UploadImportFileHandler(
    DataImportDbContext db,
    IImportFileStorageService storage,
    IImportFileValidator fileValidator,
    IClock clock)
{
    public async Task<Result<UploadImportFileResponse>> HandleAsync(
        UploadImportFileRequest request,
        Guid initiatedByUserId,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadImportFileResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        // Verify file content matches the declared content type (prevents extension/MIME spoofing).
        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadImportFileResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        int totalRows;
        try
        {
            totalRows = CountXlsxDataRows(fileStream);
        }
        catch (Exception ex)
        {
            return Result.Failure<UploadImportFileResponse>(
                Error.Validation($"The file could not be read: {ex.Message}"));
        }

        if (totalRows == 0)
        {
            return Result.Failure<UploadImportFileResponse>(
                Error.Validation("The uploaded file has no data rows to import. Add at least one employee row below the header and try again."));
        }

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            $"{request.CompanyId}",
            cancellationToken);

        var now = clock.UtcNowOffset();

        var session = ImportSession.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EntityType,
            file.FileName,
            totalRows,
            initiatedByUserId,
            storageKey,
            file.ContentType,
            now);

        db.ImportSessions.Add(session);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Best-effort: remove the already-uploaded file so it doesn't become an orphan.
            try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }
            throw;
        }

        return Result.Success(new UploadImportFileResponse(
            session.Id,
            session.CompanyId,
            session.EntityType,
            session.FileName,
            session.Status.ToString(),
            session.TotalRows,
            session.CreatedAt));
    }

    // Determines the workbook's data row count (excluding the header row).
    private static int CountXlsxDataRows(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var worksheet = workbook.Worksheet(1);
        var rowCount = worksheet.RangeUsed()?.RowCount() ?? 0;
        return Math.Max(0, rowCount - 1); // exclude header row
    }
}
