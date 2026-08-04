using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Proves a Vacancy (and its linked Position Profile) in one company can never leak to, or be acted
/// on by, another company's caller — including through the new ListVacancies PositionProfileId /
/// DepartmentId filters, and through the Hire/Offer actions that now derive Department/Location from
/// the Vacancy's linked Position Profile.
/// </summary>
[Collection("Integration")]
public class RecruitmentPositionProfileTenantIsolationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00001b-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public RecruitmentPositionProfileTenantIsolationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, companyId);
        return client;
    }

    [Fact]
    public async Task Get_Vacancies_For_Company_B_Never_Returns_Company_As_Vacancy_Or_PositionProfile_Info()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var referenceDataA = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyA);

        Guid vacancyAId;
        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancyA = Vacancy.Create(
                Guid.NewGuid(), companyA, referenceDataA.PositionProfileId, "Company A Secret Role", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancyA);
            await recruitmentDb.SaveChangesAsync();
            vacancyAId = vacancyA.Id;
        }

        using var clientB = await AuthenticatedClient(companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.DoesNotContain(payload.Items, i => i.Id == vacancyAId);
    }

    [Fact]
    public async Task Company_B_Cannot_Offer_Or_Hire_Against_Company_As_Vacancy_And_Application()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var referenceDataA = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyA);

        Guid vacancyAId, applicationAId;
        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancyA = Vacancy.Create(
                Guid.NewGuid(), companyA, referenceDataA.PositionProfileId, "Company A Role", null, Guid.NewGuid(), Now);
            var candidateA = Candidate.Create(
                Guid.NewGuid(), companyA, "Ada", "Lovelace", $"ada.{Guid.NewGuid():N}@example.com", null, null, Now);
            var stages = RecruitmentStageSeeder.BuildDefaultStages(companyA, Now);
            var offerStageId = stages.Single(s => s.Name == "Offer").Id;
            var applicationA = Application.Create(Guid.NewGuid(), companyA, vacancyA.Id, candidateA.Id, offerStageId, null, Now);

            recruitmentDb.RecruitmentStages.AddRange(stages);
            recruitmentDb.Vacancies.Add(vacancyA);
            recruitmentDb.Candidates.Add(candidateA);
            recruitmentDb.Applications.Add(applicationA);
            await recruitmentDb.SaveChangesAsync();

            vacancyAId = vacancyA.Id;
            applicationAId = applicationA.Id;
        }

        // Company B's caller supplies Company A's real Vacancy/Application IDs, but authenticates as
        // Company B — the request must be scoped away (404), never operate on Company A's data.
        using var clientB = await AuthenticatedClient(companyB);

        var offerResponse = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{vacancyAId}/applications/{applicationAId}/offer", new
            {
                companyId = companyB,
                vacancyId = vacancyAId,
                applicationId = applicationAId,
            });
        Assert.Equal(HttpStatusCode.NotFound, offerResponse.StatusCode);

        var hireResponse = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{vacancyAId}/applications/{applicationAId}/hire", new
            {
                companyId = companyB,
                vacancyId = vacancyAId,
                applicationId = applicationAId,
                startDate = new DateOnly(2026, 8, 1).ToString("yyyy-MM-dd"),
                dateOfBirth = new DateOnly(1992, 4, 15).ToString("yyyy-MM-dd"),
                nationality = "British",
                gender = "Prefer not to say",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = Guid.NewGuid(),
            });
        Assert.Equal(HttpStatusCode.NotFound, hireResponse.StatusCode);
    }

    [Fact]
    public async Task ListVacancies_Filtered_By_PositionProfileId_From_Another_Company_Returns_Empty()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var referenceDataA = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyA);
        await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyB);

        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            recruitmentDb.Vacancies.Add(Vacancy.Create(
                Guid.NewGuid(), companyA, referenceDataA.PositionProfileId, "Company A Role", null, Guid.NewGuid(), Now));
            await recruitmentDb.SaveChangesAsync();
        }

        using var clientB = await AuthenticatedClient(companyB);

        var byPositionProfile = await clientB.GetAsync(
            $"/api/companies/{companyB}/vacancies?PositionProfileId={referenceDataA.PositionProfileId}");
        Assert.Equal(HttpStatusCode.OK, byPositionProfile.StatusCode);
        var positionProfilePayload = await byPositionProfile.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(positionProfilePayload);
        Assert.Empty(positionProfilePayload!.Items);

        var byDepartment = await clientB.GetAsync(
            $"/api/companies/{companyB}/vacancies?DepartmentId={referenceDataA.DepartmentId}");
        Assert.Equal(HttpStatusCode.OK, byDepartment.StatusCode);
        var departmentPayload = await byDepartment.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(departmentPayload);
        Assert.Empty(departmentPayload!.Items);
    }

    private sealed record ListPayload(List<VacancyListItemPayload> Items);
    private sealed record VacancyListItemPayload(Guid Id, Guid PositionProfileId);
}
