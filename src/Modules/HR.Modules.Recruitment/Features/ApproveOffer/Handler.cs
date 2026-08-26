using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.ApproveOffer;

/// <summary>
/// SET-05: explicit per-application approval step, required before OfferCandidateHandler will allow
/// the application to move to the offer stage when the company's OfferApprovalRequired setting is
/// on. Uses "recruitment:manage", the same policy the offer action itself requires.
/// </summary>
internal sealed class ApproveOfferHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ApproveOfferResponse>> HandleAsync(
        ApproveOfferRequest request,
        Guid approvedBy,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<ApproveOfferResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<ApproveOfferResponse>(
                Error.Validation("Cannot approve an offer for an application that has been withdrawn."));

        var now = clock.UtcNowOffset();
        application.ApproveOffer(approvedBy, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new OfferApprovedAuditEvent(
                application.CompanyId, application.Id, application.VacancyId, application.CandidateId, approvedBy, now),
            cancellationToken);

        return Result.Success(new ApproveOfferResponse(
            application.Id, application.CompanyId, application.OfferApprovedAt!.Value, application.OfferApprovedByUserId!.Value));
    }
}
