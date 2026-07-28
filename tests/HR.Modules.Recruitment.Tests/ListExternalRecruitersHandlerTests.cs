using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListExternalRecruiters;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ListExternalRecruitersHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Filters_By_Search_Term_Against_AgencyName_And_ContactName()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var acme = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", "Jane Smith", null, null, null, null, Now);
        var beta = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Beta Talent", "John Doe", null, null, null, null, Now);
        db.ExternalRecruiters.AddRange(acme, beta);
        await db.SaveChangesAsync();

        var result = await new ListExternalRecruitersHandler(db).HandleAsync(
            new ListExternalRecruitersRequest(companyId, "acme", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal(acme.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_IsActive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var active = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var inactive = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Beta Talent", null, null, null, null, null, Now);
        inactive.SetActiveStatus(false, Now);
        db.ExternalRecruiters.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var result = await new ListExternalRecruitersHandler(db).HandleAsync(
            new ListExternalRecruitersRequest(companyId, null, false),
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal(inactive.Id, result.Value.Items[0].Id);
    }

    [Fact]
    public async Task HandleAsync_Paginates_Results()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            db.ExternalRecruiters.Add(ExternalRecruiter.Create(Guid.NewGuid(), companyId, $"Agency {i}", null, null, null, null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListExternalRecruitersHandler(db).HandleAsync(
            new ListExternalRecruitersRequest(companyId, null, null, PageNumber: 2, PageSize: 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task HandleAsync_Returns_LinkedVacancyCount_Counting_Vacancies_Currently_Assigned_To_Recruiter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        var vacancy1 = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now, recruiter.Id);
        var vacancy2 = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Frontend Engineer", null, Guid.NewGuid(), Now, recruiter.Id);
        db.ExternalRecruiters.Add(recruiter);
        db.Vacancies.AddRange(vacancy1, vacancy2);
        await db.SaveChangesAsync();

        var result = await new ListExternalRecruitersHandler(db).HandleAsync(
            new ListExternalRecruitersRequest(companyId, null, null),
            CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Single().LinkedVacancyCount);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Recruiters_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        db.ExternalRecruiters.Add(ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now));
        db.ExternalRecruiters.Add(ExternalRecruiter.Create(Guid.NewGuid(), otherCompanyId, "Beta Talent", null, null, null, null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListExternalRecruitersHandler(db).HandleAsync(
            new ListExternalRecruitersRequest(companyId, null, null),
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
