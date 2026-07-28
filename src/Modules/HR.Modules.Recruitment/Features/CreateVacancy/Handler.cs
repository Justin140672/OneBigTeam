using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class CreateVacancyHandler(
    RecruitmentDbContext db,
    IClock clock,
    IPositionProfileReader positionProfileReader)
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

        var vacancy = Vacancy.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.AdvertTitle,
            request.AdvertDescription,
            request.HiringManagerId,
            now,
            request.AssignedRecruiterId);

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
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt));
    }
}
