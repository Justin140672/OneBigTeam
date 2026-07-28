using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.CreateExternalRecruiter;

internal sealed class CreateExternalRecruiterHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateExternalRecruiterResponse>> HandleAsync(
        CreateExternalRecruiterRequest request,
        CancellationToken cancellationToken)
    {
        // Duplicate agency names are explicitly allowed — no uniqueness validation performed here.
        var now = clock.UtcNowOffset();

        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.AgencyName,
            request.ContactName,
            request.ContactEmail,
            request.ContactTelephone,
            request.Website,
            request.Notes,
            now);

        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new ExternalRecruiterCreatedAuditEvent(recruiter.CompanyId, recruiter.Id, recruiter.AgencyName, now),
            cancellationToken);

        return Result.Success(new CreateExternalRecruiterResponse(
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
