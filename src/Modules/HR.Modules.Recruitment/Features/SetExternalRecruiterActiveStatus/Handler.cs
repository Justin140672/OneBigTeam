using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;

internal sealed class SetExternalRecruiterActiveStatusHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<SetExternalRecruiterActiveStatusResponse>> HandleAsync(
        SetExternalRecruiterActiveStatusRequest request,
        CancellationToken cancellationToken)
    {
        var recruiter = await db.ExternalRecruiters
            .SingleOrDefaultAsync(
                r => r.Id == request.ExternalRecruiterId && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (recruiter is null)
            return Result.Failure<SetExternalRecruiterActiveStatusResponse>(
                Error.NotFound($"External recruiter '{request.ExternalRecruiterId}' was not found."));

        var previousIsActive = recruiter.IsActive;
        var now = clock.UtcNowOffset();

        // Never deletes the row — just flips the flag, per SetActiveStatus's remarks.
        recruiter.SetActiveStatus(request.IsActive, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new ExternalRecruiterActiveStatusChangedAuditEvent(
                recruiter.CompanyId, recruiter.Id, recruiter.AgencyName, previousIsActive, recruiter.IsActive, now),
            cancellationToken);

        return Result.Success(new SetExternalRecruiterActiveStatusResponse(
            recruiter.Id,
            recruiter.CompanyId,
            recruiter.AgencyName,
            recruiter.IsActive,
            recruiter.UpdatedAt));
    }
}
