using System.Security.Claims;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// OBT-721 workload action provider tests for vacancies awaiting recruiter action — Recruiter-only
/// (reporting:view-recruitment). No Manager/HR row-scoping applies to this category, matching
/// GetRecruitmentPipelineReport/GetVacancyPerformanceReport being Recruiter-only elsewhere.
/// </summary>
public class VacanciesAwaitingActionWorkloadActionProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new RecruitmentDbContext(options);
    }

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static Vacancy CreateOpenVacancy(
        Guid companyId, Guid hiringManagerId, DateOnly openedAt, Guid? assignedRecruiterId = null)
    {
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Engineer", null, hiringManagerId,
            Now, assignedRecruiterId);
        vacancy.Open(Now, openedAt);
        return vacancy;
    }

    [Fact]
    public async Task GetActionsAsync_RecruiterCaller_Sees_OpenVacancies_With_No_Recruiter_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        context.Vacancies.Add(CreateOpenVacancy(companyId, hiringManagerId, DateOnly.FromDateTime(Now.Date)));
        await context.SaveChangesAsync();

        var provider = new VacanciesAwaitingActionWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-recruitment"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Awaiting Assignment", action.Status);
        Assert.Equal("Vacancies Awaiting Action", action.ActionCategory);
    }

    [Fact]
    public async Task GetActionsAsync_NonRecruiterCaller_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.Vacancies.Add(CreateOpenVacancy(companyId, Guid.NewGuid(), DateOnly.FromDateTime(Now.Date)));
        await context.SaveChangesAsync();

        // Manager or HR, but not Recruiter — this category is Recruiter-only.
        var provider = new VacanciesAwaitingActionWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_Surfaces_Stale_Vacancy_With_Recruiter_Assigned_Open_30Plus_Days()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var staleOpenedAt = DateOnly.FromDateTime(Now.Date).AddDays(-45);

        context.Vacancies.Add(CreateOpenVacancy(companyId, hiringManagerId, staleOpenedAt, recruiterId));
        await context.SaveChangesAsync();

        var provider = new VacanciesAwaitingActionWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-recruitment"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Open 30+ Days", action.Status);
        Assert.StartsWith("Progress Stale Vacancy", action.ActionType);
    }

    [Fact]
    public async Task GetActionsAsync_Maps_DeepLink_And_Null_DueDate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        var vacancy = CreateOpenVacancy(companyId, hiringManagerId, DateOnly.FromDateTime(Now.Date));
        context.Vacancies.Add(vacancy);
        await context.SaveChangesAsync();

        var provider = new VacanciesAwaitingActionWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-recruitment"));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Null(action.DueDate);
        Assert.Equal($"/companies/{companyId}/vacancies/{vacancy.Id}/view", action.DeepLinkUrl);
        Assert.Equal(hiringManagerId, action.EmployeeId);
    }
}
