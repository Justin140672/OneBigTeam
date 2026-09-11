using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 2 end-to-end: an accepted offer's proposed start date and agreed salary flow through to the
/// hired Employee — HR does not re-key either. Also proves a declined offer blocks the hire outright.
/// </summary>
[Collection("Integration")]
public class HireCandidateUsesAcceptedOfferEndToEndTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("ce000043-0000-0000-0000-000000000001");

    public HireCandidateUsesAcceptedOfferEndToEndTests(ApiWebApplicationFactory factory)
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

    private async Task<(Guid VacancyId, Guid ApplicationId)> SeedVacancyCandidateApplicationAsync(
        HttpClient client, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData referenceData)
    {
        var vacancyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.Created, vacancyResponse.StatusCode);
        var vacancy = await vacancyResponse.Content.ReadFromJsonAsync<VacancyPayload>();

        var candidateResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Jamie",
            lastName = "Okafor",
            email = $"jamie.okafor.{Guid.NewGuid():N}@example.com",
        });
        Assert.Equal(HttpStatusCode.Created, candidateResponse.StatusCode);
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        var applicationResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy!.Id}/applications", new
            {
                companyId,
                vacancyId = vacancy.Id,
                candidateId = candidate!.Id,
            });
        Assert.Equal(HttpStatusCode.Created, applicationResponse.StatusCode);
        var application = await applicationResponse.Content.ReadFromJsonAsync<ApplicationPayload>();

        return (vacancy.Id, application!.Id);
    }

    [Fact]
    public async Task Hire_Without_StartDate_Uses_Accepted_Offer_Start_Date_And_Salary()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var (vacancyId, applicationId) = await SeedVacancyCandidateApplicationAsync(client, companyId, referenceData);

        var proposedStartDate = new DateOnly(2026, 11, 2);

        var offerResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", new
            {
                companyId,
                vacancyId,
                applicationId,
                offeredSalary = 73500m,
                offeredSalaryFrequency = "Annual",
                proposedStartDate = proposedStartDate.ToString("yyyy-MM-dd"),
            });
        Assert.Equal(HttpStatusCode.OK, offerResponse.StatusCode);

        var acceptResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response", new
            {
                companyId,
                vacancyId,
                applicationId,
                status = "Accepted",
            });
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        var hireResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/hire", new
            {
                companyId,
                vacancyId,
                applicationId,
                dateOfBirth = new DateOnly(1992, 4, 15).ToString("yyyy-MM-dd"),
                nationality = "British",
                gender = "Prefer not to say",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = referenceData.EmploymentTypeId,
            });
        Assert.Equal(HttpStatusCode.OK, hireResponse.StatusCode);
        var hire = await hireResponse.Content.ReadFromJsonAsync<HirePayload>();
        Assert.NotNull(hire);

        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = await employeesDb.Employees.SingleAsync(e => e.Id == hire!.EmployeeId);
        Assert.Equal(proposedStartDate, employee.StartDate);

        var compensation = await employeesDb.Compensations.SingleAsync(c => c.EmployeeId == hire!.EmployeeId);
        Assert.Equal(73500m, compensation.Salary);
        Assert.Equal("GBP", compensation.Currency);
    }

    [Fact]
    public async Task Hire_After_Offer_Declined_Returns_BadRequest_And_Creates_No_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var (vacancyId, applicationId) = await SeedVacancyCandidateApplicationAsync(client, companyId, referenceData);

        var offerResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", new
            {
                companyId,
                vacancyId,
                applicationId,
                offeredSalary = 60000m,
                offeredSalaryFrequency = "Annual",
                proposedStartDate = new DateOnly(2026, 11, 2).ToString("yyyy-MM-dd"),
            });
        Assert.Equal(HttpStatusCode.OK, offerResponse.StatusCode);

        var declineResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response", new
            {
                companyId,
                vacancyId,
                applicationId,
                status = "Declined",
            });
        Assert.Equal(HttpStatusCode.OK, declineResponse.StatusCode);

        var hireResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/hire", new
            {
                companyId,
                vacancyId,
                applicationId,
                dateOfBirth = new DateOnly(1992, 4, 15).ToString("yyyy-MM-dd"),
                nationality = "British",
                gender = "Prefer not to say",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = referenceData.EmploymentTypeId,
            });
        Assert.Equal(HttpStatusCode.BadRequest, hireResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        Assert.Equal(0, await employeesDb.Employees.CountAsync(e => e.CompanyId == companyId));
    }

    private sealed record VacancyPayload(Guid Id);
    private sealed record CandidatePayload(Guid Id);
    private sealed record ApplicationPayload(Guid Id);
    private sealed record HirePayload(Guid Id, Guid EmployeeId);
}
