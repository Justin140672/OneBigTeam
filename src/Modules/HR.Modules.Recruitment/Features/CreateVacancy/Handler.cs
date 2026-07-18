using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

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
            now);

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
            vacancy.OpenedAt,
            vacancy.ClosedAt,
            vacancy.CreatedAt,
            vacancy.UpdatedAt));
    }
}
