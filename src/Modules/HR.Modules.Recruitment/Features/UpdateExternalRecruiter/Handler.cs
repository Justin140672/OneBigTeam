using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.UpdateExternalRecruiter;

internal sealed class UpdateExternalRecruiterHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<UpdateExternalRecruiterResponse>> HandleAsync(
        UpdateExternalRecruiterRequest request,
        CancellationToken cancellationToken)
    {
        var recruiter = await db.ExternalRecruiters
            .SingleOrDefaultAsync(
                r => r.Id == request.ExternalRecruiterId && r.CompanyId == request.CompanyId,
                cancellationToken);

        if (recruiter is null)
            return Result.Failure<UpdateExternalRecruiterResponse>(
                Error.NotFound($"External recruiter '{request.ExternalRecruiterId}' was not found."));

        var before = new ExternalRecruiterAuditSnapshot(
            recruiter.AgencyName,
            recruiter.ContactName,
            recruiter.ContactEmail,
            recruiter.ContactTelephone,
            recruiter.Website,
            recruiter.Notes);

        var now = clock.UtcNowOffset();

        recruiter.UpdateDetails(
            request.AgencyName,
            request.ContactName,
            request.ContactEmail,
            request.ContactTelephone,
            request.Website,
            request.Notes,
            now);

        await db.SaveChangesAsync(cancellationToken);

        var after = new ExternalRecruiterAuditSnapshot(
            recruiter.AgencyName,
            recruiter.ContactName,
            recruiter.ContactEmail,
            recruiter.ContactTelephone,
            recruiter.Website,
            recruiter.Notes);

        await auditPublisher.PublishAsync(
            new ExternalRecruiterUpdatedAuditEvent(recruiter.CompanyId, recruiter.Id, before, after, now),
            cancellationToken);

        return Result.Success(new UpdateExternalRecruiterResponse(
            recruiter.Id,
            recruiter.CompanyId,
            recruiter.AgencyName,
            recruiter.ContactName,
            recruiter.ContactEmail,
            recruiter.ContactTelephone,
            recruiter.Website,
            recruiter.Notes,
            recruiter.IsActive,
            recruiter.CreatedAt,
            recruiter.UpdatedAt));
    }
}
