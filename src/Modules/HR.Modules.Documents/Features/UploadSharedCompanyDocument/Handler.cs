using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadSharedCompanyDocument;

internal sealed class UploadSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IFileUploadValidator fileValidator,
    SharedCompanyDocumentAudienceRuleBuilder audienceRuleBuilder,
    IEmployeeAudienceReader employeeAudienceReader,
    IAuditEventPublisher auditPublisher,
    IClock clock,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<UploadSharedCompanyDocumentResponse>> HandleAsync(
        UploadSharedCompanyDocumentRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentResponse>(validationResult.Error);

        var safeFileName = file.FileName.Split(['/', '\\']).Last();
        if (string.IsNullOrWhiteSpace(safeFileName) ||
            safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.Validation("File name is not valid."));
        }

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

        if (request.ReviewOwnerEmployeeId is { } reviewOwnerEmployeeId &&
            !await employeeAudienceReader.EmployeeExistsAsync(request.CompanyId, reviewOwnerEmployeeId, cancellationToken))
        {
            return Result.Failure<UploadSharedCompanyDocumentResponse>(
                Error.NotFound($"Employee '{reviewOwnerEmployeeId}' was not found."));
        }

        var documentId = Guid.NewGuid();

        var ruleBuildResult = await audienceRuleBuilder.BuildAsync(
            request.CompanyId,
            documentId,
            request.AudienceDepartmentIds,
            request.AudienceLocationIds,
            request.AudiencePositionProfileIds,
            request.AudienceEmployeeIds,
            cancellationToken);

        if (ruleBuildResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentResponse>(ruleBuildResult.Error);

        await using var fileStream = file.OpenReadStream();

        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadSharedCompanyDocumentResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            safeFileName,
            file.ContentType,
            $"{request.CompanyId}/shared-documents",
            cancellationToken);

        var now = clock.UtcNowOffset();

        var document = SharedCompanyDocument.Create(
            documentId,
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
            request.ReviewFrequency,
            request.CustomReviewFrequencyMonths,
            request.ReviewOwnerEmployeeId,
            request.RequiresAcknowledgement,
            request.AcknowledgementDueDate,
            request.AcknowledgementStatement,
            uploadedBy,
            now);

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
            now,
            versionNote: null,
            requiresAcknowledgement: document.RequiresAcknowledgement,
            effectiveDate: document.EffectiveDate,
            acknowledgementStatement: document.AcknowledgementStatement);

        db.SharedCompanyDocuments.Add(document);
        db.SharedCompanyDocumentVersions.Add(version);
        db.SharedCompanyDocumentAudienceRules.AddRange(ruleBuildResult.Value!);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }
            throw;
        }

        await auditPublisher.PublishAsync(new SharedCompanyDocumentCreatedAuditEvent(
            document.CompanyId, document.Id, document.Title, document.CategoryId, uploadedBy, now), cancellationToken);

        await auditPublisher.PublishAsync(new SharedCompanyDocumentFileUploadedAuditEvent(
            document.CompanyId, document.Id, document.Title, safeFileName, file.Length, document.VersionNumber, uploadedBy, now), cancellationToken);

        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.SharedCompanyDocument, document.Id, document.CompanyId, null));
        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.SharedCompanyDocumentVersion, version.Id, document.CompanyId, null));

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
            document.ReviewFrequency.ToString(),
            document.CustomReviewFrequencyMonths,
            document.ReviewOwnerEmployeeId,
            request.AudienceDepartmentIds,
            request.AudienceLocationIds,
            request.AudiencePositionProfileIds,
            request.AudienceEmployeeIds,
            document.RequiresAcknowledgement,
            document.AcknowledgementDueDate,
            document.AcknowledgementStatement,
            document.CreatedBy,
            document.CreatedAt));
    }
}
