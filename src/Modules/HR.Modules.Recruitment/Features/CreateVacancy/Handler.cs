using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class CreateVacancyHandler(
    RecruitmentDbContext db,
    IClock clock,
    IPositionProfileReader positionProfileReader,
    RecruitmentStageSeeder stageSeeder)
{
    public async Task<Result<CreateVacancyResponse>> HandleAsync(
        CreateVacancyRequest request,
        CancellationToken cancellationToken)
    {
        // Cross-module validation: PositionProfile is owned by HR.Modules.Employees, so existence
        // and company-ownership are verified through the narrow IPositionProfileReader contract
        // rather than a direct module reference or a database foreign key.
        var positionProfileExists = await positionProfileReader.ExistsAsync(
            request.CompanyId, request.PositionProfileId, cancellationToken);

        if (!positionProfileExists)
            return Result.Failure<CreateVacancyResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        // A position profile can only be recruited against by one live vacancy at a time — Closed
        // and Cancelled are the only terminal statuses, so any other status (Draft/OnHold/Open)
        // counts as "already in progress" here.
        var hasConcurrentVacancy = await db.Vacancies
            .AsNoTracking()
            .AnyAsync(
                v => v.CompanyId == request.CompanyId
                    && v.PositionProfileId == request.PositionProfileId
                    && v.Status != VacancyStatus.Closed
                    && v.Status != VacancyStatus.Cancelled,
                cancellationToken);

        if (hasConcurrentVacancy)
            return Result.Failure<CreateVacancyResponse>(
                Error.Validation("This position profile already has an open vacancy. Close or cancel it before opening another."));

        // Ticket #81: AssignedRecruiterId is an optional FK to ExternalRecruiter (the external agency),
        // not an Employee — existence/company-ownership/active checks happen here via direct EF Core
        // access, since ExternalRecruiter lives in this same module/schema.
        if (request.AssignedRecruiterId is { } requestedRecruiterId)
        {
            var recruiter = await db.ExternalRecruiters
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    r => r.Id == requestedRecruiterId && r.CompanyId == request.CompanyId,
                    cancellationToken);

            if (recruiter is null)
                return Result.Failure<CreateVacancyResponse>(
                    Error.NotFound($"External recruiter '{requestedRecruiterId}' was not found."));

            if (!recruiter.IsActive)
                return Result.Failure<CreateVacancyResponse>(
                    Error.Validation($"External recruiter '{recruiter.AgencyName}' is inactive and cannot be assigned to a vacancy."));
        }

        // Department is no longer stored on Vacancy at all — it is always derived from the linked
        // Position Profile at the read layer (see GetVacancyHandler/ListVacanciesHandler), so there is
        // nothing to resolve or persist here at create time.
        var now = clock.UtcNowOffset();

        // Ticket #98: creating a company's first Vacancy is the chosen "recruitment enabled" moment
        // (see RecruitmentStageSeeder's remarks) — idempotent, so this is a no-op for every vacancy
        // after the first.
        await stageSeeder.EnsureDefaultStagesSeededAsync(request.CompanyId, now, cancellationToken);

        var vacancy = Vacancy.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.AdvertTitle,
            request.AdvertDescription,
            request.HiringManagerId,
            now,
            request.AssignedRecruiterId,
            request.IsAdvertisedInternally);

        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateVacancyResponse(
            vacancy.Id,
            vacancy.CompanyId,
            vacancy.PositionProfileId,
            vacancy.AdvertTitle,
            vacancy.AdvertDescription,
            vacancy.Status,
            vacancy.HiringManagerId,
            vacancy.AssignedRecruiterId,
            vacancy.IsAdvertisedInternally,
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt));
    }
}
