using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocument;

internal sealed class UploadSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IFileUploadValidator fileValidator,
    IVirusScanService virusScanner,
    IEmployeeAudienceReader audienceReader,
    IClock clock)
{
    public async Task<Result<UploadSharedCompanyDocumentResponse>> HandleAsync(
        UploadSharedCompanyDocumentRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        // Supported file type + maximum file size.
        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentResponse>(validationResult.Error);

        // Safe storage filename — strip any directory component a crafted FileName (e.g.
        // "../../evil.pdf" or "..\..\evil.pdf") might carry, so it can never escape the storage
        // folder the same way the file-name segment of the storage key otherwise would. Split on
        // both separators explicitly rather than relying on Path.GetFileName, whose separator
        // handling is platform-dependent (it only treats '\' as a separator on Windows).
        var safeFileName = file.FileName.Split(['/', '\\']).Last();
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.Validation("File name is not valid."));
        }

        // Company ownership — the category must belong to the same company as the document.
        var categoryExists = await db.CompanyDocumentCategories
            .AnyAsync(
                c => c.Id == request.CategoryId &&
                     c.CompanyId == request.CompanyId &&
                     c.IsActive,
                cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.NotFound($"Document category '{request.CategoryId}' was not found."));
        }

        if (request.AudienceDepartmentId is not null && request.AudienceLocationId is not null)
        {
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.Validation("A document's audience can be scoped to a department or a location, not both."));
        }

        if (request.AudienceDepartmentId is not null &&
            !await audienceReader.DepartmentExistsAsync(request.CompanyId, request.AudienceDepartmentId.Value, cancellationToken))
        {
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.NotFound($"Department '{request.AudienceDepartmentId}' was not found."));
        }

        if (request.AudienceLocationId is not null &&
            !await audienceReader.LocationExistsAsync(request.CompanyId, request.AudienceLocationId.Value, cancellationToken))
        {
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.NotFound($"Location '{request.AudienceLocationId}' was not found."));
        }

        await using var fileStream = file.OpenReadStream();

        var scanResult = await virusScanner.ScanAsync(fileStream, safeFileName, cancellationToken);
        if (!scanResult.IsClean)
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.Validation($"File was rejected: {scanResult.ThreatName}."));

        fileStream.Seek(0, SeekOrigin.Begin);

        // Verify file content matches the declared content type (prevents extension/MIME spoofing).
        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        // Existing Supabase-backed document storage (same IDocumentStorageService used for
        // employee documents) — falls back to local disk storage only when Supabase isn't
        // configured for this environment.
        var storageKey = await storage.UploadAsync(
            fileStream,
            safeFileName,
            file.ContentType,
            $"{request.CompanyId}/shared-documents",
            cancellationToken);

        var now = clock.UtcNowOffset();

        // Always created as a Draft — SharedCompanyDocument.Create hard-codes this, publishing
        // is a separate, explicit action (shared-document:publish).
        var document = SharedCompanyDocument.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.Title,
            request.Description,
            request.CategoryId,
            storageKey,
            safeFileName,
            file.Length,
            file.ContentType,
            request.EffectiveDate,
            request.ReviewDate,
            request.AudienceDepartmentId,
            request.AudienceLocationId,
            request.RequiresAcknowledgement,
            uploadedBy,
            now);

        // Every version (including this first one) gets its own history row — see
        // SharedCompanyDocumentVersion's doc comment for why version 1 isn't a special case.
        var version = SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(),
            request.CompanyId,
            document.Id,
            document.VersionNumber,
            storageKey,
            safeFileName,
            file.Length,
            file.ContentType,
            uploadedBy,
            now);

        db.SharedCompanyDocuments.Add(document);
        db.SharedCompanyDocumentVersions.Add(version);

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

        return Result.Success(new UploadSharedCompanyDocumentResponse(
            document.Id,
            document.CompanyId,
            document.Title,
            document.Description,
            document.CategoryId,
            document.FileName,
            document.FileSize,
            document.ContentType,
            document.VersionNumber,
            document.Status.ToString(),
            document.EffectiveDate,
            document.ReviewDate,
            document.AudienceDepartmentId,
            document.AudienceLocationId,
            document.RequiresAcknowledgement,
            document.CreatedBy,
            document.CreatedAt));
    }
}
