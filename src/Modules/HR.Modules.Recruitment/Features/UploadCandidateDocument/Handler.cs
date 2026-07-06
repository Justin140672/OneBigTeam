using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

internal sealed class UploadCandidateDocumentHandler(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IOptions<CandidateDocumentUploadOptions> options,
    IClock clock)
{
    public async Task<Result<UploadCandidateDocumentResponse>> HandleAsync(
        UploadCandidateDocumentRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var candidateExists = await db.Candidates
            .AnyAsync(c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId, cancellationToken);

        if (!candidateExists)
            return Result.Failure<UploadCandidateDocumentResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        var file = request.File;
        var validationResult = Validate(file.FileName, file.ContentType, file.Length, options.Value);
        if (validationResult.IsFailure)
            return Result.Failure<UploadCandidateDocumentResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            $"{request.CompanyId}/{request.CandidateId}",
            cancellationToken);

        var now = clock.UtcNowOffset();

        var document = CandidateDocument.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.CandidateId,
            request.Title,
            file.FileName,
            file.Length,
            file.ContentType,
            storageKey,
            uploadedBy,
            now);

        db.CandidateDocuments.Add(document);

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

        return Result.Success(new UploadCandidateDocumentResponse(
            document.Id,
            document.CompanyId,
            document.CandidateId,
            document.Title,
            document.FileName,
            document.FileSize,
            document.ContentType,
            document.CreatedAt));
    }

    private static Result Validate(string fileName, string contentType, long fileSize, CandidateDocumentUploadOptions options)
    {
        if (fileSize <= 0)
            return Result.Failure(Error.Validation("File must not be empty."));

        if (fileSize > options.MaxFileSizeBytes)
        {
            var maxMb = options.MaxFileSizeBytes / (1024.0 * 1024.0);
            return Result.Failure(Error.Validation($"File size exceeds the maximum allowed size of {maxMb:0.##} MB."));
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) ||
            !options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", options.AllowedExtensions);
            return Result.Failure(Error.Validation($"File type '{extension}' is not allowed. Allowed types: {allowed}."));
        }

        var normalizedContentType = contentType.Split(';')[0].Trim();
        if (!options.AllowedContentTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", options.AllowedContentTypes);
            return Result.Failure(Error.Validation($"Content type '{normalizedContentType}' is not allowed. Allowed types: {allowed}."));
        }

        return Result.Success();
    }
}
