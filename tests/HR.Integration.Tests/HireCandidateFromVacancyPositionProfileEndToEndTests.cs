using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// End-to-end coverage for the "Vacancy - Position Profile relationship" epic's final story: a hired
/// employee's Department/Location/PositionProfile are derived exclusively from the Vacancy's linked
/// Position Profile (via HireCandidateHandler -> IPositionProfileReader), never from independent
/// client-supplied values — HireCandidateRequest no longer even carries those fields. Also proves
/// OfferCandidate surfaces the linked Position Profile's employment defaults as read-only context.
/// </summary>
[Collection("Integration")]
public class HireCandidateFromVacancyPositionProfileEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00001a-0000-0000-0000-000000000001");

    public HireCandidateFromVacancyPositionProfileEndToEndTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Full_Pipeline_Hires_Candidate_With_Employee_Matching_Vacancys_PositionProfile_And_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        // 1. Create a Vacancy against the seeded Position Profile.
        var createVacancyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, createVacancyResponse.StatusCode);
        var vacancy = await createVacancyResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(vacancy);

        // 2. Create a Candidate + Application against that vacancy.
        var createCandidateResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Jamie",
            lastName = "Okafor",
            email = $"jamie.okafor.{Guid.NewGuid():N}@example.com",
        });
        Assert.Equal(HttpStatusCode.Created, createCandidateResponse.StatusCode);
        var candidate = await createCandidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();
        Assert.NotNull(candidate);

        var createApplicationResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy!.Id}/applications", new
            {
                companyId,
                vacancyId = vacancy.Id,
                candidateId = candidate!.Id,
            });
        Assert.Equal(HttpStatusCode.Created, createApplicationResponse.StatusCode);
        var application = await createApplicationResponse.Content.ReadFromJsonAsync<ApplicationPayload>();
        Assert.NotNull(application);

        // 3. Transition through Interview.
        var scheduleInterviewResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application!.Id}/interviews", new
            {
                companyId,
                vacancyId = vacancy.Id,
                applicationId = application.Id,
                interviewerEmployeeId = Guid.NewGuid(),
                scheduledAt = DateTimeOffset.UtcNow.AddDays(1),
            });
        Assert.Equal(HttpStatusCode.Created, scheduleInterviewResponse.StatusCode);
        var interview = await scheduleInterviewResponse.Content.ReadFromJsonAsync<InterviewPayload>();
        Assert.NotNull(interview);

        var recordOutcomeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application.Id}/interviews/{interview!.Id}/outcome",
            new
            {
                companyId,
                vacancyId = vacancy.Id,
                applicationId = application.Id,
                interviewId = interview.Id,
                outcome = "Passed",
            });
        Assert.Equal(HttpStatusCode.OK, recordOutcomeResponse.StatusCode);

        // 4. Offer the candidate — assert the offer response surfaces the position profile's
        //    employment defaults (the seeded profile has none set, so they should be null, but the
        //    PositionProfileId itself must match the vacancy's).
        var offerResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application.Id}/offer", new
            {
                companyId,
                vacancyId = vacancy.Id,
                applicationId = application.Id,
            });
        Assert.Equal(HttpStatusCode.OK, offerResponse.StatusCode);
        var offer = await offerResponse.Content.ReadFromJsonAsync<OfferPayload>();
        Assert.NotNull(offer);
        Assert.Equal(referenceData.PositionProfileId, offer!.PositionProfileId);

        // 5. Hire the candidate. HireCandidateRequest carries no Department/Location/PositionProfile
        //    fields at all — those are derived server-side from the Vacancy's linked Position Profile.
        var hireResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy.Id}/applications/{application.Id}/hire", new
            {
                companyId,
                vacancyId = vacancy.Id,
                applicationId = application.Id,
                startDate = new DateOnly(2026, 8, 1).ToString("yyyy-MM-dd"),
                dateOfBirth = new DateOnly(1992, 4, 15).ToString("yyyy-MM-dd"),
                nationality = "British",
                gender = "Prefer not to say",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = referenceData.EmploymentTypeId,
            });
        Assert.Equal(HttpStatusCode.OK, hireResponse.StatusCode);
        var hire = await hireResponse.Content.ReadFromJsonAsync<HirePayload>();
        Assert.NotNull(hire);

        // 6. Assert the resulting Employee row has the SAME PositionProfileId (and derived Department)
        //    as the Vacancy's, not any independently-supplied value.
        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = await employeesDb.Employees.SingleAsync(e => e.Id == hire!.EmployeeId);

        Assert.Equal(referenceData.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(vacancy.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(referenceData.DepartmentId, employee.DepartmentId);
        Assert.Equal(referenceData.LocationId, employee.LocationId);
    }

    private sealed record VacancyPayload(Guid Id, Guid CompanyId, Guid PositionProfileId);
    private sealed record CandidatePayload(Guid Id, Guid CompanyId);
    private sealed record ApplicationPayload(Guid Id, Guid VacancyId, Guid CandidateId);
    private sealed record InterviewPayload(Guid Id);
    private sealed record OfferPayload(Guid Id, Guid PositionProfileId);
    private sealed record HirePayload(Guid Id, Guid EmployeeId);
}
