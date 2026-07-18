using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed class OfferCandidateHandler(
    RecruitmentDbContext db,
    IClock clock,
    IPositionProfileReader positionProfileReader)
{
    public async Task<Result<OfferCandidateResponse>> HandleAsync(
        OfferCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var application = await db.Applications
            .SingleOrDefaultAsync(
                a => a.Id == request.ApplicationId &&
                     a.CompanyId == request.CompanyId &&
                     a.VacancyId == request.VacancyId,
                cancellationToken);

        if (application is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.NotFound($"Application '{request.ApplicationId}' was not found."));

        if (application.Status != ApplicationStatus.Interviewed)
            return Result.Failure<OfferCandidateResponse>(
                Error.Validation($"Cannot make an offer for an application with status '{application.Status}'."));

        var vacancy = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                v => v.Id == request.VacancyId && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (vacancy is null)
            return Result.Failure<OfferCandidateResponse>(
                Error.NotFound($"Vacancy '{request.VacancyId}' was not found."));

        var now = clock.UtcNowOffset();

        application.Offer(now);
        await db.SaveChangesAsync(cancellationToken);

        // Cross-module read: informational-only employment defaults from the linked Position Profile
        // (owned by HR.Modules.Employees), resolved via the narrow IPositionProfileReader contract. See
        // OfferCandidateResponse's remarks — this does not affect the offer itself.
        var employmentDefaults = await positionProfileReader.GetEmploymentDefaultsAsync(
            request.CompanyId, vacancy.PositionProfileId, cancellationToken);

        return Result.Success(new OfferCandidateResponse(
            application.Id,
            application.VacancyId,
            application.CandidateId,
            application.Status,
            application.InterviewOutcome,
            application.Notes,
            application.AppliedAt,
            application.CreatedAt,
            application.UpdatedAt,
            vacancy.PositionProfileId,
            employmentDefaults?.Title,
            employmentDefaults?.SalaryMin,
            employmentDefaults?.SalaryMax,
            employmentDefaults?.SalaryType,
            employmentDefaults?.WorkingDaysOverride,
            employmentDefaults?.HoursPerDayOverride,
            employmentDefaults?.ProbationMonthsOverride,
            employmentDefaults?.DefaultLeavePolicyId,
            employmentDefaults?.LocationName));
    }
}
