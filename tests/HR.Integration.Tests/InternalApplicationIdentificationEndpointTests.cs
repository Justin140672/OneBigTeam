using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Internal recruitment Ticket 6: identify internal applications across the recruiter-facing read
/// endpoints and recruitment reports. Application.Source == Internal is the ONLY authoritative
/// internal indicator — Candidate.EmployeeId alone is not, because HireCandidate links external
/// candidates to their new employee record. Every seeded company therefore carries:
/// <list type="bullet">
/// <item>an internal application (employee-linked candidate, Source Internal, CV Review, 1 interview),</item>
/// <item>an external Direct application (CV Review),</item>
/// <item>a legacy application with a null Source (Application Received),</item>
/// <item>a hired external candidate (Source Direct, Candidate.EmployeeId set, reached Offer then Hired).</item>
/// </list>
/// Expected report counts for the vacancy: all = 4 candidates, internal = 1, external = 3; offers /
/// hires only on the external side (the hired external candidate); interviews only on the internal side.
///
/// Every test creates its own company, vacancy and users so the class is independent and parallel-safe.
/// See the HR.Modules.Recruitment.Tests / HR.Modules.Reporting.Tests unit tests for handler-level coverage.
/// </summary>
[Collection("Integration")]
public class InternalApplicationIdentificationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public InternalApplicationIdentificationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }


    private async Task<HttpClient> RecruiterClientAsync(Guid companyId)
    {
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private sealed record Seeded(
        Guid VacancyId,
        string VacancyTitle,
        Guid ApplicationReceivedStageId,
        Guid CvReviewStageId,
        Guid HiredStageId,
        Guid InternalEmployeeId,
        Guid InternalCandidateId,
        Guid InternalApplicationId,
        Guid DirectApplicationId,
        Guid LegacyApplicationId,
        Guid HiredExternalCandidateId,
        Guid HiredExternalApplicationId,
        Guid HiredExternalEmployeeId);

    private async Task<Seeded> SeedMixedAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, now);
        var received = stages.Single(s => s.Name == "Application Received");
        var cvReview = stages.Single(s => s.Name == "CV Review");
        var offer    = stages.Single(s => s.Name == "Offer");
        var hired    = stages.Single(s => s.TerminalOutcome == RecruitmentStageTerminalOutcome.Hired);
        db.RecruitmentStages.AddRange(stages);

        var title = $"Internal Id Role {Guid.NewGuid():N}";
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), title, null, Guid.NewGuid(), now);
        vacancy.Open(now, DateOnly.FromDateTime(now.UtcDateTime));
        db.Vacancies.Add(vacancy);

        var internalEmployeeId = Guid.NewGuid();
        var internalCandidate = Candidate.CreateForEmployee(
            Guid.NewGuid(), companyId, internalEmployeeId, "Priya", "Shah", $"priya.{Guid.NewGuid():N}@acme.example", null, now);
        var internalApp = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, internalCandidate.Id, cvReview.Id, null, now, ApplicationSource.Internal);

        var directCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, now);
        var directApp = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, directCandidate.Id, cvReview.Id, null, now.AddSeconds(1), ApplicationSource.Direct);

        var legacyCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Noah", "Patel", $"noah.{Guid.NewGuid():N}@example.com", null, null, now);
        var legacyApp = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, legacyCandidate.Id, received.Id, null, now.AddSeconds(2));

        var hiredEmployeeId = Guid.NewGuid();
        var hiredCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", $"liam.{Guid.NewGuid():N}@example.com", null, null, now);
        hiredCandidate.LinkToEmployee(hiredEmployeeId, now);
        var hiredApp = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, hiredCandidate.Id, hired.Id, null, now.AddSeconds(3), ApplicationSource.Direct);

        db.Candidates.AddRange(internalCandidate, directCandidate, legacyCandidate, hiredCandidate);
        db.Applications.AddRange(internalApp, directApp, legacyApp, hiredApp);

        db.Interviews.Add(Interview.Create(Guid.NewGuid(), companyId, internalApp.Id, Guid.NewGuid(), now.AddDays(1), 30, null, now));
        db.ApplicationStageHistoryEntries.AddRange(
            ApplicationStageHistoryEntry.Create(Guid.NewGuid(), companyId, hiredApp.Id, cvReview.Id, offer.Id, null, null, now),
            ApplicationStageHistoryEntry.Create(Guid.NewGuid(), companyId, hiredApp.Id, offer.Id, hired.Id, null, null, now.AddSeconds(1)));

        await db.SaveChangesAsync();

        return new Seeded(
            vacancy.Id, title, received.Id, cvReview.Id, hired.Id,
            internalEmployeeId, internalCandidate.Id, internalApp.Id,
            directApp.Id, legacyApp.Id,
            hiredCandidate.Id, hiredApp.Id, hiredEmployeeId);
    }

    private static string DetailUrl(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}";

    private static string ListUrl(Guid companyId, Guid vacancyId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications";

    private static string SearchUrl(Guid companyId) =>
        $"/api/companies/{companyId}/recruitment/applications/search";

    private static string ReportUrl(Guid companyId, string report) =>
        $"/api/companies/{companyId}/reporting/{report}";

    private static string[] CsvRowStartingWith(string body, string firstCell)
    {
        var line = body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Single(l => l.StartsWith(firstCell, StringComparison.Ordinal) || l.StartsWith("\"" + firstCell, StringComparison.Ordinal));
        return line.Split(',').Select(c => c.Trim('"')).ToArray();
    }


    [Theory]
    [InlineData("vacancies/{v}/applications?isInternal=true")]
    [InlineData("vacancies/{v}/applications/{a}")]
    [InlineData("vacancies/{v}/kanban")]
    [InlineData("recruitment/applications/search?isInternal=true&candidateId={a}")]
    [InlineData("reporting/recruitment-pipeline?isInternal=true")]
    [InlineData("reporting/recruitment-pipeline/export?isInternal=true")]
    [InlineData("reporting/vacancy-performance?isInternal=false")]
    [InlineData("reporting/vacancy-performance/export?isInternal=false")]
    [InlineData("reporting/recruitment-pipeline-summary?isInternal=true")]
    [InlineData("reporting/recruitment-pipeline-summary/export?isInternal=true")]
    public async Task Get_Returns_Unauthorized_For_Anonymous_Request(string path)
    {
        using var client = _factory.CreateClient();
        var url = $"/api/companies/{Guid.NewGuid()}/" + path
            .Replace("{v}", Guid.NewGuid().ToString())
            .Replace("{a}", Guid.NewGuid().ToString());

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task Get_Application_Detail_Internal_Returns_IsInternal_True_And_EmployeeId()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(DetailUrl(companyId, seeded.VacancyId, seeded.InternalApplicationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal(seeded.InternalApplicationId, payload!.Id);
        Assert.Equal("Internal", payload.Source);
        Assert.True(payload.IsInternal);
        Assert.Equal(seeded.InternalEmployeeId, payload.EmployeeId);
    }

    [Fact]
    public async Task Get_Application_Detail_External_Direct_And_Legacy_Return_IsInternal_False_And_Null_EmployeeId()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        foreach (var applicationId in new[] { seeded.DirectApplicationId, seeded.LegacyApplicationId })
        {
            var response = await client.GetAsync(DetailUrl(companyId, seeded.VacancyId, applicationId));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<DetailPayload>();
            Assert.NotNull(payload);
            Assert.False(payload!.IsInternal);
            Assert.Null(payload.EmployeeId);
        }
    }

    [Fact]
    public async Task Get_Application_Detail_Hired_External_Candidate_Is_Not_Internal_And_EmployeeId_Is_Null()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(DetailUrl(companyId, seeded.VacancyId, seeded.HiredExternalApplicationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Direct", payload!.Source);
        Assert.False(payload.IsInternal);
        Assert.Null(payload.EmployeeId);
    }

    [Fact]
    public async Task Get_Application_Detail_For_Application_Created_Via_Employee_Apply_Endpoint_Is_Internal()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee, companyId);
        await SeedEmployeeAsync(employeeUserId, companyId);
        var vacancyId = await SeedInternallyAdvertisedVacancyAsync(companyId);

        using var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeUserId.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, employeeUserId, companyId);

        var applyResponse = await employeeClient.PostAsync(
            $"/api/companies/{companyId}/internal-vacancies/{vacancyId}/applications", PdfCvForm());
        Assert.Equal(HttpStatusCode.Created, applyResponse.StatusCode);
        var created = await applyResponse.Content.ReadFromJsonAsync<ApplyCreatedPayload>();
        Assert.NotNull(created);

        using var recruiterClient = await RecruiterClientAsync(companyId);
        var response = await recruiterClient.GetAsync(DetailUrl(companyId, vacancyId, created!.ApplicationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DetailPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Internal", payload!.Source);
        Assert.True(payload.IsInternal);
        Assert.Equal(employeeUserId, payload.EmployeeId);

        var listResponse = await recruiterClient.GetAsync(ListUrl(companyId, vacancyId) + "?isInternal=true");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        var item = Assert.Single(list!.Items);
        Assert.Equal(created.ApplicationId, item.Id);
        Assert.Equal(employeeUserId, item.EmployeeId);
    }


    [Fact]
    public async Task Get_Vacancy_Applications_Without_Filter_Returns_All_With_IsInternal_And_EmployeeId()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ListUrl(companyId, seeded.VacancyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var items = payload!.Items.ToDictionary(i => i.Id);
        Assert.Equal(4, items.Count);

        Assert.True(items[seeded.InternalApplicationId].IsInternal);
        Assert.Equal(seeded.InternalEmployeeId, items[seeded.InternalApplicationId].EmployeeId);

        foreach (var externalId in new[] { seeded.DirectApplicationId, seeded.LegacyApplicationId, seeded.HiredExternalApplicationId })
        {
            Assert.False(items[externalId].IsInternal);
            Assert.Null(items[externalId].EmployeeId);
        }
    }

    [Fact]
    public async Task Get_Vacancy_Applications_IsInternal_True_Returns_Only_Internal()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ListUrl(companyId, seeded.VacancyId) + "?isInternal=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        var item = Assert.Single(payload!.Items);
        Assert.Equal(seeded.InternalApplicationId, item.Id);
        Assert.True(item.IsInternal);
        Assert.Equal(seeded.InternalEmployeeId, item.EmployeeId);
    }

    [Fact]
    public async Task Get_Vacancy_Applications_IsInternal_False_Returns_External_Including_Legacy_And_Hired_External()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ListUrl(companyId, seeded.VacancyId) + "?isInternal=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(
            new[] { seeded.DirectApplicationId, seeded.LegacyApplicationId, seeded.HiredExternalApplicationId }.OrderBy(x => x),
            payload!.Items.Select(i => i.Id).OrderBy(x => x));
        Assert.All(payload.Items, i =>
        {
            Assert.False(i.IsInternal);
            Assert.Null(i.EmployeeId);
        });
    }

    [Fact]
    public async Task Get_Vacancy_Applications_IsInternal_Combines_With_StageId()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(
            ListUrl(companyId, seeded.VacancyId) + $"?stageId={seeded.CvReviewStageId}&isInternal=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(seeded.DirectApplicationId, Assert.Single(payload!.Items).Id);
    }


    [Fact]
    public async Task Get_Kanban_Internal_And_External_Share_Stage_Column_With_IsInternal_And_EmployeeId()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/kanban");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<KanbanPayload>();
        Assert.NotNull(payload);
        Assert.Equal(6, payload!.Columns.Count);

        var cvReview = payload.Columns.Single(c => c.StageId == seeded.CvReviewStageId);
        Assert.Equal(2, cvReview.Count);
        var internalCard = cvReview.Candidates.Single(c => c.ApplicationId == seeded.InternalApplicationId);
        Assert.True(internalCard.IsInternal);
        Assert.Equal(seeded.InternalEmployeeId, internalCard.EmployeeId);
        var directCard = cvReview.Candidates.Single(c => c.ApplicationId == seeded.DirectApplicationId);
        Assert.False(directCard.IsInternal);
        Assert.Null(directCard.EmployeeId);

        var hiredCard = payload.Columns.Single(c => c.StageId == seeded.HiredStageId).Candidates.Single();
        Assert.Equal(seeded.HiredExternalApplicationId, hiredCard.ApplicationId);
        Assert.False(hiredCard.IsInternal);
        Assert.Null(hiredCard.EmployeeId);

        var legacyCard = payload.Columns.Single(c => c.StageId == seeded.ApplicationReceivedStageId).Candidates.Single();
        Assert.False(legacyCard.IsInternal);
        Assert.Null(legacyCard.EmployeeId);
    }


    [Fact]
    public async Task Get_Search_IsInternal_True_Returns_Only_Internal_With_Stage_And_Withdrawn_Fields()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(SearchUrl(companyId) + "?isInternal=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TotalCount);
        var item = Assert.Single(payload.Items);
        Assert.Equal(seeded.InternalApplicationId, item.ApplicationId);
        Assert.True(item.IsInternal);
        Assert.Equal(seeded.InternalEmployeeId, item.EmployeeId);
        Assert.Equal("CV Review", item.CurrentStageName);
        Assert.False(item.IsWithdrawn);
    }

    [Fact]
    public async Task Get_Search_IsInternal_False_Returns_External_Including_Legacy_And_Hired_External()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(SearchUrl(companyId) + "?isInternal=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.TotalCount);
        Assert.Equal(
            new[] { seeded.DirectApplicationId, seeded.LegacyApplicationId, seeded.HiredExternalApplicationId }.OrderBy(x => x),
            payload.Items.Select(i => i.ApplicationId).OrderBy(x => x));
        Assert.All(payload.Items, i =>
        {
            Assert.False(i.IsInternal);
            Assert.Null(i.EmployeeId);
        });
        Assert.Equal("Hired", payload.Items.Single(i => i.ApplicationId == seeded.HiredExternalApplicationId).CurrentStageName);
    }

    [Fact]
    public async Task Get_Search_Without_IsInternal_Returns_All_Four()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        await SeedMixedAsync(companyId);

        var response = await client.GetAsync(SearchUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.Equal(4, payload!.TotalCount);
    }

    [Fact]
    public async Task Get_Search_CandidateId_Returns_Only_That_Candidates_Applications()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(SearchUrl(companyId) + $"?candidateId={seeded.HiredExternalCandidateId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(seeded.HiredExternalApplicationId, item.ApplicationId);
        Assert.Equal(seeded.HiredExternalCandidateId, item.CandidateId);
        Assert.False(item.IsInternal);
        Assert.Null(item.EmployeeId);
    }

    [Fact]
    public async Task Get_Search_CandidateId_Combined_With_IsInternal_False_Excludes_Internal_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(SearchUrl(companyId) + $"?candidateId={seeded.InternalCandidateId}&isInternal=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.Equal(0, payload!.TotalCount);
        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task Get_Search_CandidateId_From_Another_Company_Returns_Nothing()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        await SeedMixedAsync(companyId);
        var other = await SeedMixedAsync(otherCompanyId);

        var response = await client.GetAsync(SearchUrl(companyId) + $"?candidateId={other.InternalCandidateId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SearchPayload>();
        Assert.Equal(0, payload!.TotalCount);
    }

    [Fact]
    public async Task Get_Search_CandidateId_Empty_Guid_Returns_UnprocessableEntity()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);

        var response = await client.GetAsync(SearchUrl(companyId) + $"?candidateId={Guid.Empty}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }


    [Theory]
    [InlineData("", 4, 1, 1, 1)]
    [InlineData("&isInternal=true", 1, 1, 0, 0)]
    [InlineData("&isInternal=false", 3, 0, 1, 1)]
    public async Task Get_RecruitmentPipeline_By_Vacancy_Applies_IsInternal(string filter, int candidates, int interviews, int offers, int hires)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ReportUrl(companyId, "recruitment-pipeline") + "?groupBy=Vacancy" + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PipelinePayload>();
        Assert.NotNull(payload);
        var row = Assert.Single(payload!.Items);
        Assert.Equal(seeded.VacancyId.ToString(), row.GroupKey);
        Assert.Equal(candidates, row.Candidates);
        Assert.Equal(interviews, row.Interviews);
        Assert.Equal(offers, row.Offers);
        Assert.Equal(hires, row.Hires);
    }

    [Theory]
    [InlineData("", 4)]
    [InlineData("?isInternal=true", 1)]
    [InlineData("?isInternal=false", 3)]
    public async Task Get_RecruitmentPipeline_By_Recruiter_Applies_IsInternal(string filter, int candidates)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ReportUrl(companyId, "recruitment-pipeline") + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PipelinePayload>();
        var row = Assert.Single(payload!.Items);
        Assert.Equal(1, row.Vacancies);
        Assert.Equal(candidates, row.Candidates);
    }

    [Theory]
    [InlineData("", "4")]
    [InlineData("&isInternal=true", "1")]
    [InlineData("&isInternal=false", "3")]
    public async Task Export_RecruitmentPipeline_Applies_IsInternal(string filter, string expectedCandidates)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ReportUrl(companyId, "recruitment-pipeline/export") + "?format=Csv&groupBy=Vacancy" + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Group,Vacancies,Candidates,Interviews,Offers,Hires", body);
        var row = CsvRowStartingWith(body, seeded.VacancyTitle);
        Assert.Equal(expectedCandidates, row[2]);
    }


    [Theory]
    [InlineData("", 4, 1, 1, true)]
    [InlineData("?isInternal=true", 1, 1, 0, false)]
    [InlineData("?isInternal=false", 3, 0, 1, true)]
    public async Task Get_VacancyPerformance_Applies_IsInternal(string filter, int candidates, int interviews, int offers, bool expectHireDate)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ReportUrl(companyId, "vacancy-performance") + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPerformancePayload>();
        Assert.NotNull(payload);
        var row = Assert.Single(payload!.Items);
        Assert.Equal(seeded.VacancyId, row.VacancyId);
        Assert.Equal(candidates, row.CandidateCount);
        Assert.Equal(interviews, row.InterviewCount);
        Assert.Equal(offers, row.OfferCount);
        Assert.Equal(expectHireDate, row.HireDate is not null);
    }

    [Theory]
    [InlineData("", "4")]
    [InlineData("&isInternal=true", "1")]
    [InlineData("&isInternal=false", "3")]
    public async Task Export_VacancyPerformance_Applies_IsInternal(string filter, string expectedCandidates)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ReportUrl(companyId, "vacancy-performance/export") + "?format=Csv" + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Vacancy,Days Open,Candidates,Interviews,Offers,Hire Date", body);
        var row = CsvRowStartingWith(body, seeded.VacancyTitle);
        Assert.Equal(expectedCandidates, row[2]);
    }


    [Theory]
    [InlineData("", 4, 2, 1, 1)]
    [InlineData("?isInternal=true", 1, 1, 0, 0)]
    [InlineData("?isInternal=false", 3, 1, 1, 1)]
    public async Task Get_RecruitmentPipelineSummary_Applies_IsInternal(string filter, int candidates, int cvReview, int received, int hired)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ReportUrl(companyId, "recruitment-pipeline-summary") + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        var row = Assert.Single(payload!.Vacancies);
        Assert.Equal(seeded.VacancyId, row.VacancyId);
        Assert.Equal(candidates, row.CandidateCount);
        Assert.Equal(cvReview, row.CandidatesByStage.GetValueOrDefault(seeded.CvReviewStageId));
        Assert.Equal(received, row.CandidatesByStage.GetValueOrDefault(seeded.ApplicationReceivedStageId));
        Assert.Equal(hired, row.CandidatesByStage.GetValueOrDefault(seeded.HiredStageId));
    }

    [Theory]
    [InlineData("", "4")]
    [InlineData("&isInternal=true", "1")]
    [InlineData("&isInternal=false", "3")]
    public async Task Export_RecruitmentPipelineSummary_Applies_IsInternal(string filter, string expectedCandidates)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterClientAsync(companyId);
        var seeded = await SeedMixedAsync(companyId);

        var response = await client.GetAsync(ReportUrl(companyId, "recruitment-pipeline-summary/export") + "?format=Csv" + filter);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Vacancy,Position Profile,Department,Status,Date Opened,Candidates", body);
        var row = CsvRowStartingWith(body, seeded.VacancyTitle);
        Assert.Equal(expectedCandidates, row[5]);
    }


    private async Task SeedEmployeeAsync(Guid userId, Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(
            userId, companyId, "Priya", "Shah", $"priya.{Guid.NewGuid():N}@acme.example",
            new DateOnly(2024, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, now);
        employee.Activate(now);

        db.Employees.Add(employee);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedInternallyAdvertisedVacancyAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Internal Apply Role", "A description", Guid.NewGuid(), now,
            assignedRecruiterId: null, isAdvertisedInternally: true);
        vacancy.Open(now, DateOnly.FromDateTime(now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    private static MultipartFormDataContent PdfCvForm()
    {
        var bytes = new byte[2048];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        var content = new MultipartFormDataContent();
        content.Add(fileContent, "CvFile", "priya-cv.pdf");
        return content;
    }


    private sealed record DetailPayload(Guid Id, string? Source, bool IsInternal, Guid? EmployeeId);

    private sealed record ListPayload(List<ListItemPayload> Items);

    private sealed record ListItemPayload(Guid Id, Guid CandidateId, Guid CurrentStageId, bool IsInternal, Guid? EmployeeId);

    private sealed record KanbanPayload(Guid VacancyId, List<KanbanColumnPayload> Columns);

    private sealed record KanbanColumnPayload(Guid StageId, string StageName, int Count, List<KanbanCardPayload> Candidates);

    private sealed record KanbanCardPayload(Guid ApplicationId, Guid CandidateId, Guid StageId, bool IsInternal, Guid? EmployeeId);

    private sealed record SearchPayload(List<SearchItemPayload> Items, int TotalCount);

    private sealed record SearchItemPayload(
        Guid ApplicationId,
        Guid CandidateId,
        Guid CurrentStageId,
        string? CurrentStageName,
        bool IsWithdrawn,
        bool IsInternal,
        Guid? EmployeeId);

    private sealed record PipelinePayload(List<PipelineItemPayload> Items);

    private sealed record PipelineItemPayload(string GroupKey, string GroupLabel, int Vacancies, int Candidates, int Interviews, int Offers, int Hires);

    private sealed record VacancyPerformancePayload(List<VacancyPerformanceItemPayload> Items);

    private sealed record VacancyPerformanceItemPayload(Guid VacancyId, string VacancyTitle, int CandidateCount, int InterviewCount, int OfferCount, DateOnly? HireDate);

    private sealed record SummaryPayload(List<SummaryRowPayload> Vacancies);

    private sealed record SummaryRowPayload(Guid VacancyId, int CandidateCount, Dictionary<Guid, int> CandidatesByStage);

    private sealed record ApplyCreatedPayload(Guid ApplicationId, Guid CandidateId, string Source);
}
