using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;
using static HR.Integration.Tests.Infrastructure.InternalOfferTestHelpers;

namespace HR.Integration.Tests;

/// <summary>
/// Internal vacancy offers: POST .../applications/{a}/offer for an Internal application requires the
/// full internal terms (start date, salary, frequency, currency, and a manager or "No manager"), snapshots
/// them onto the application with an offer version, and refuses incomplete or inconsistent terms.
/// Status codes: request-validator failures are 422, handler validation errors are 400.
/// </summary>
[Collection("Integration")]
public class OfferCandidateInternalTermsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public OfferCandidateInternalTermsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Offer_For_Internal_Application_Snapshots_The_Terms_At_Version_One()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        await SetTargetProfileAsync(_factory, s,
            workingDays: HR.Modules.Employees.Contracts.WorkingDays.Monday | HR.Modules.Employees.Contracts.WorkingDays.Tuesday,
            hoursPerDay: 8m, probationMonths: 6);
        var start = Today.AddDays(30);
        var deadline = Today.AddDays(10);

        var body = await MakeOfferOkAsync(recruiter, s, b =>
        {
            b["proposedStartDate"] = start.ToString("yyyy-MM-dd");
            b["responseDeadline"] = deadline.ToString("yyyy-MM-dd");
            b.Remove("hoursPerWeek");
        });

        Assert.Equal("AwaitingResponse", body.GetProperty("offerResponseStatus").GetString());
        var terms = body.GetProperty("offerTerms");
        Assert.Equal(1, terms.GetProperty("offerVersion").GetInt32());

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(s.OfferStageId, application.CurrentStageId);
        Assert.Equal(1, application.OfferVersion);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, application.OfferResponseStatus);
        Assert.Equal(recruiterId, application.OfferMadeByUserId);
        Assert.Equal(DefaultSalary, application.OfferedSalary);
        Assert.Equal(OfferSalaryFrequency.Annual, application.OfferedSalaryFrequency);
        Assert.Equal(DefaultCurrency, application.OfferCurrency);
        Assert.Equal(start, application.OfferedStartDate);
        Assert.Equal(deadline, application.OfferResponseDeadline);
        Assert.Equal("Engineering Manager", application.OfferJobTitle);
        Assert.Equal(s.World.Target.DepartmentId, application.OfferDepartmentId);
        Assert.Equal(s.World.Target.LocationId, application.OfferLocationId);
        Assert.Equal(s.World.Target.PositionProfileId, application.OfferPositionProfileId);
        Assert.Equal(s.World.Target.EmploymentTypeId, application.OfferEmploymentTypeId);
        Assert.Equal(s.World.NewManagerId, application.OfferProposedManagerId);
        Assert.Equal("Nathan Brooks", application.OfferProposedManagerName);
        Assert.False(application.OfferNoManager);
        Assert.Equal(8m, application.OfferHoursPerDay);
        Assert.Equal(16m, application.OfferHoursPerWeek);
        Assert.Equal(1m, application.OfferFte);
        Assert.Equal(6, application.OfferProbationMonths);
        Assert.NotNull(application.OfferTermsSnapshotAt);
        Assert.Equal(
            HR.Modules.Employees.Contracts.WorkingDays.Monday | HR.Modules.Employees.Contracts.WorkingDays.Tuesday,
            application.OfferWorkingDays);
    }

    [Fact]
    public async Task Post_Offer_Without_Salary_Defaults_To_The_Position_Profile_SalaryMin()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        await SetTargetProfileAsync(_factory, s, salaryMin: 61000m);

        await MakeOfferOkAsync(recruiter, s, b => b.Remove("offeredSalary"));

        Assert.Equal(61000m, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferedSalary);
    }

    [Fact]
    public async Task Post_Offer_Without_Salary_And_Without_Profile_Default_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b.Remove("offeredSalary"));

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_With_NoManager_Records_The_Decision_Instead_Of_A_Manager()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        await MakeOfferOkAsync(recruiter, s, b =>
        {
            b.Remove("proposedManagerId");
            b["noManager"] = true;
        });

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.True(application.OfferNoManager);
        Assert.Null(application.OfferProposedManagerId);
        Assert.Null(application.OfferProposedManagerName);
    }

    [Fact]
    public async Task Post_Offer_With_Response_Deadline_Of_Today_Is_Accepted()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        await MakeOfferOkAsync(recruiter, s, b => b["responseDeadline"] = Today.ToString("yyyy-MM-dd"));

        Assert.Equal(Today, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseDeadline);
    }

    [Fact]
    public async Task Post_Offer_With_Response_Deadline_Yesterday_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b["responseDeadline"] = Today.AddDays(-1).ToString("yyyy-MM-dd"));

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_Without_Manager_Choice_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b.Remove("proposedManagerId"));

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("proposedStartDate")]
    public async Task Post_Offer_Missing_A_Required_Internal_Term_Returns_BadRequest(string missingField)
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b.Remove(missingField));

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_With_Whitespace_Currency_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b["currency"] = "   ");

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_With_The_Applicant_As_Manager_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b["proposedManagerId"] = s.EmployeeId);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_With_An_Unknown_Manager_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b["proposedManagerId"] = Guid.NewGuid());

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_To_An_Employee_Who_Is_No_Longer_Active_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
            var employee = await db.Employees.SingleAsync(e => e.Id == s.EmployeeId);
            employee.Suspend(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var response = await MakeOfferAsync(recruiter, s);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_With_A_Manager_And_NoManager_Returns_422()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b["noManager"] = true);

        await AssertStatusAsync(HttpStatusCode.UnprocessableEntity, response);
        await AssertNoOfferAsync(s);
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("GBPP")]
    public async Task Post_Offer_With_A_Currency_That_Is_Not_Three_Letters_Returns_422(string currency)
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b["currency"] = currency);

        await AssertStatusAsync(HttpStatusCode.UnprocessableEntity, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_Without_Salary_Frequency_Returns_422()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await MakeOfferAsync(recruiter, s, b => b.Remove("offeredSalaryFrequency"));

        await AssertStatusAsync(HttpStatusCode.UnprocessableEntity, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_By_An_Employee_Without_Recruitment_Manage_Returns_Forbidden()
    {
        var companyId = Guid.NewGuid();
        var s = await SeedUnofferedAsync(_factory, companyId);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await MakeOfferAsync(employee, s);

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
        await AssertNoOfferAsync(s);
    }

    [Fact]
    public async Task Post_Offer_For_An_External_Application_Keeps_The_Existing_Behaviour_And_Records_No_Snapshot()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var external = await SeedExternalUnofferedAsync(companyId, s);

        var response = await recruiter.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{s.VacancyId}/applications/{external}/offer",
            new { offeredSalary = 50000m, offeredSalaryFrequency = "Annual" });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("offerTerms").ValueKind);

        var saved = await GetApplicationAsync(_factory, external);
        Assert.Null(saved.OfferTermsSnapshotAt);
        Assert.Equal(1, saved.OfferVersion);
        Assert.Empty(await GetOfferTasksForApplicationAsync(companyId, external));
    }

    private async Task<Guid> SeedExternalUnofferedAsync(Guid companyId, Scenario s)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Recruitment.Persistence.RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, now);
        var application = Application.Create(Guid.NewGuid(), companyId, s.VacancyId, candidate.Id, s.CvReviewStageId, null, now, ApplicationSource.Direct);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        await PassedInterviewSeed.AddForAllInterviewStagesAsync(_factory, companyId, application.Id);
        return application.Id;
    }

    private async Task<List<HR.Modules.Tasks.Domain.TaskItem>> GetOfferTasksForApplicationAsync(Guid companyId, Guid applicationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Tasks.Persistence.TasksDbContext>();
        return await db.TaskItems.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.SourceEntityId == applicationId)
            .ToListAsync();
    }

    private async Task AssertNoOfferAsync(Scenario s)
    {
        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Null(application.OfferResponseStatus);
        Assert.Null(application.OfferedSalary);
        Assert.Equal(0, application.OfferVersion);
        Assert.Equal(s.CvReviewStageId, application.CurrentStageId);
        Assert.Empty(await GetOfferTasksAsync(_factory, s));
    }
}
