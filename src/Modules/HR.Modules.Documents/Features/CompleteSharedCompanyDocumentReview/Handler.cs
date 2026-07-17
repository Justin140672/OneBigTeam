using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;

internal sealed class CompleteSharedCompanyDocumentReviewHandler(
    DocumentsDbContext db,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<CompleteSharedCompanyDocumentReviewResponse>> HandleAsync(
        CompleteSharedCompanyDocumentReviewRequest request,
        Guid reviewedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<CompleteSharedCompanyDocumentReviewResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var reviewDate = DateOnly.FromDateTime(clock.UtcNow);
        var nextReviewDate = ComputeNextReviewDate(document.ReviewFrequency, document.CustomReviewFrequencyMonths, reviewDate);

        // Captured before CompleteReview overwrites document.ReviewDate — this is the due date
        // that THIS review is fulfilling, not the next scheduled one.
        var previousReviewDate = document.ReviewDate;

        document.CompleteReview(reviewedBy, request.ReviewNotes, reviewDate, nextReviewDate, clock.UtcNowOffset());

        var historyEntry = SharedCompanyDocumentReviewHistory.Create(
            Guid.NewGuid(),
            document.CompanyId,
            document.Id,
            reviewDate,
            reviewedBy,
            request.ReviewNotes,
            previousReviewDate,
            clock.UtcNowOffset());
        db.SharedCompanyDocumentReviewHistories.Add(historyEntry);

        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new SharedCompanyDocumentReviewCompletedAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            previousReviewDate,
            reviewDate,
            document.LastReviewNotes,
            nextReviewDate,
            reviewedBy,
            clock.UtcNowOffset()), cancellationToken);

        return Result.Success(new CompleteSharedCompanyDocumentReviewResponse(
            document.Id,
            document.CompanyId,
            document.ReviewDate,
            document.LastReviewedAt!.Value,
            document.LastReviewedByEmployeeId!.Value,
            document.LastReviewNotes));
    }

    // Deliberately kept out of the domain entity — CompleteReview receives the next review date
    // already computed, the same way UpdateDetails receives reviewDate/reviewFrequency as given
    // values rather than computing anything itself.
    private static DateOnly? ComputeNextReviewDate(
        SharedCompanyDocumentReviewFrequency frequency,
        int? customReviewFrequencyMonths,
        DateOnly reviewDate) =>
        frequency switch
        {
            SharedCompanyDocumentReviewFrequency.Monthly    => reviewDate.AddMonths(1),
            SharedCompanyDocumentReviewFrequency.Quarterly  => reviewDate.AddMonths(3),
            SharedCompanyDocumentReviewFrequency.SixMonthly => reviewDate.AddMonths(6),
            SharedCompanyDocumentReviewFrequency.Yearly     => reviewDate.AddMonths(12),
            // Defensive: Custom should always carry a value, but fall back to 0 months rather than
            // throwing if it somehow doesn't.
            SharedCompanyDocumentReviewFrequency.Custom     => reviewDate.AddMonths(customReviewFrequencyMonths ?? 0),
            _                                                => null,
        };
}
