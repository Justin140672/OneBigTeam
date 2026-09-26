using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.DownloadCandidateDocument;

internal sealed class DownloadCandidateDocumentHandler(RecruitmentDbContext db, ICandidateDocumentStorageService storage)
{
    /// <summary>Pending/Scanning: the file has not been proven safe yet — mapped to 409.</summary>
    internal const string ScanPendingCode = "document_scan_pending";

    /// <summary>Infected: malware was detected and the file is quarantined — mapped to 403.</summary>
    internal const string QuarantinedCode = "document_quarantined";

    /// <summary>Failed: every scan attempt failed — the file is blocked — mapped to 403.</summary>
    internal const string ScanFailedCode = "document_scan_failed";

    public async Task<Result<Uri>> HandleAsync(
        DownloadCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var document = await db.CandidateDocuments
            .AsNoTracking()
            .Where(cd => cd.Id == request.DocumentId &&
                         cd.CompanyId == request.CompanyId &&
                         cd.CandidateId == request.CandidateId)
            .Select(cd => new { cd.StorageKey, cd.ScanStatus })
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
            return Result.Failure<Uri>(Error.NotFound($"Candidate document '{request.DocumentId}' was not found."));

        // [P1] A signed URL is only ever minted for a document whose malware scan is Clean. This is the
        // single server-side download path for candidate documents; the web app's CV proxy and every
        // UI link go through it.
        var blocked = CheckDownloadable(document.ScanStatus);
        if (blocked is not null)
            return Result.Failure<Uri>(blocked);

        var url = await storage.GetDownloadUrlAsync(document.StorageKey, cancellationToken);

        return Result.Success(url);
    }

    internal static Error? CheckDownloadable(CandidateDocumentScanStatus status) => status switch
    {
        CandidateDocumentScanStatus.Clean => null,
        CandidateDocumentScanStatus.Pending or CandidateDocumentScanStatus.Scanning => new Error(
            ScanPendingCode,
            "This document is still being security checked and cannot be downloaded yet. Please try again shortly."),
        CandidateDocumentScanStatus.Infected => new Error(
            QuarantinedCode,
            "This document failed a security check and has been quarantined. It cannot be downloaded."),
        _ => new Error(
            ScanFailedCode,
            "This document could not be security checked and cannot be downloaded. Please upload it again."),
    };
}
