using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.AssignVacancyPositionProfile;

/// <summary>
/// Manual HR-review action: assigns (or re-assigns) the position profile for a single vacancy.
/// This is how ambiguous/unmatched rows surfaced by GetVacanciesNeedingPositionProfileReview get
/// resolved when VacancyPositionProfileMatcher cannot safely auto-assign one.
/// </summary>
internal sealed class AssignVacancyPositionProfileHandler(
    RecruitmentDbContext db,
    IClock clock,
    IPositionProfileReader positionProfileReader,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<AssignVacancyPositionProfileResponse>> HandleAsync(
        AssignVacancyPositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<AssignVacancyPositionProfileResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        // Cross-module validation: PositionProfile is owned by HR.Modules.Employees, so existence
        // and company-ownership are verified through the narrow IPositionProfileReader contract
        // rather than a direct module reference or a database foreign key.
        var positionProfileExists = await positionProfileReader.ExistsAsync(
            request.CompanyId, request.PositionProfileId, cancellationToken);

        if (!positionProfileExists)
            return Result.Failure<AssignVacancyPositionProfileResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var previousPositionProfileId = vacancy.PositionProfileId;
        var now = clock.UtcNowOffset();

        vacancy.AssignPositionProfile(request.PositionProfileId, now);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(
            new VacancyPositionProfileAssignedAuditEvent(
                vacancy.CompanyId, vacancy.Id, previousPositionProfileId, request.PositionProfileId, "manual", now),
            cancellationToken);

        return Result.Success(new AssignVacancyPositionProfileResponse(
            vacancy.Id,
            vacancy.CompanyId,
            vacancy.PositionProfileId,
            vacancy.AdvertTitle,
            vacancy.Status,
            vacancy.UpdatedAt));
    }
}
