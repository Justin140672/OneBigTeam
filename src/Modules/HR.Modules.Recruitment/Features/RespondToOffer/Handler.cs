using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.RespondToOffer;

/// <summary>
/// Ticket 2: records the explicit response to an offer previously made via OfferCandidate —
/// Accepted, Declined or Withdrawn. Does not itself move the pipeline stage: an accepted offer stays
/// on the offer stage until HireCandidate runs; a declined/withdrawn offer keeps its stage for
/// history (mirrors how candidate withdrawal is orthogonal to CurrentStageId). Hire is blocked
/// downstream for declined/withdrawn offers.
/// </summary>
internal sealed class RespondToOfferHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<RespondToOfferResponse>> HandleAsync(
        RespondToOfferRequest request,
        Guid performedBy,
        CancellationToken cancellationToken)
    {
        var target = Enum.Parse<OfferResponseStatus>(request.Status, ignoreCase: true);

        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<RespondToOfferResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.WithdrawnAt is not null)
            return Result.Failure<RespondToOfferResponse>(
                Error.Validation("Cannot record an offer response for an application that has been withdrawn."));

        if (application.OfferResponseStatus is null)
            return Result.Failure<RespondToOfferResponse>(
                Error.Validation("No offer has been made for this application yet."));

        if (application.OfferResponseStatus != OfferResponseStatus.AwaitingResponse)
            return Result.Failure<RespondToOfferResponse>(
                Error.Conflict($"This offer has already been resolved as '{application.OfferResponseStatus}'."));

        var now = clock.UtcNowOffset();
        var previousStatus = application.OfferResponseStatus.Value.ToString();

        application.RespondToOffer(target, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new OfferResponseRecordedAuditEvent(
                application.CompanyId,
                application.Id,
                application.VacancyId,
                application.CandidateId,
                previousStatus,
                target.ToString(),
                performedBy,
                now),
            cancellationToken);

        return Result.Success(new RespondToOfferResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.CurrentStageId,
            application.OfferResponseStatus!.Value.ToString(),
            application.OfferedSalary,
            application.OfferedSalaryFrequency?.ToString(),
            application.OfferedStartDate,
            application.OfferDate,
            application.OfferNotes,
            application.OfferMadeAt,
            application.OfferRespondedAt));
    }
}
