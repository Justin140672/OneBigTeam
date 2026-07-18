using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetVacanciesNeedingPositionProfileReview;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// LEGACY / dead-in-practice: <see cref="Vacancy.PositionProfileId"/> is now a non-nullable
/// <see cref="Guid"/> (see the comment on that property), so there is no compiler-representable way
/// to construct a "vacancy needing position profile review" any more — every vacancy always has a
/// PositionProfileId. GetVacanciesNeedingPositionProfileReviewHandler was rewritten to always
/// short-circuit and return an empty result; these tests assert exactly that, regardless of what
/// vacancy data exists in the database.
/// </summary>
public class GetVacanciesNeedingPositionProfileReviewHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Always_Returns_Empty_Result_When_No_Vacancies_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var handler = new GetVacanciesNeedingPositionProfileReviewHandler(db, new VacancyPositionProfileMatcher(new FakePositionProfileReader()));

        var result = await handler.HandleAsync(new GetVacanciesNeedingPositionProfileReviewRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Always_Returns_Empty_Result_Even_When_Vacancies_Exist_With_A_PositionProfileId()
    {
        // Every vacancy that can be constructed via the public API has a non-null PositionProfileId,
        // so the legacy "needs review" query — which used to look for a null PositionProfileId — can
        // never find anything, no matter how many vacancies exist.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.Vacancies.Add(Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now));
        db.Vacancies.Add(Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Frontend Engineer", null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var handler = new GetVacanciesNeedingPositionProfileReviewHandler(db, new VacancyPositionProfileMatcher(new FakePositionProfileReader()));

        var result = await handler.HandleAsync(new GetVacanciesNeedingPositionProfileReviewRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
