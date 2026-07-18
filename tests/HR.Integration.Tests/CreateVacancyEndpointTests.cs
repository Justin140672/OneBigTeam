using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class CreateVacancyEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00000a-0000-0000-0000-000000000001");

    public CreateVacancyEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Post_Vacancies_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/vacancies", new
        {
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Vacancies_Creates_Vacancy_With_Valid_PositionProfile()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle = "Senior Software Engineer",
            advertDescription = "Own the payments platform",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(referenceData.PositionProfileId, payload.PositionProfileId);
        Assert.Equal("Senior Software Engineer", payload.AdvertTitle);
        Assert.Equal("Own the payments platform", payload.AdvertDescription);
    }

    [Fact]
    public async Task Post_Vacancies_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = Guid.NewGuid(),
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Vacancies_Returns_NotFound_When_PositionProfile_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        // Position profile exists, but for a different company than the one making the request.
        var otherCompanyReferenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, otherCompanyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = otherCompanyReferenceData.PositionProfileId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Vacancies_Returns_BadRequest_When_PositionProfileId_Is_Empty()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = Guid.Empty,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Vacancies_Returns_BadRequest_When_PositionProfileId_Is_Omitted()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_Vacancies_Returns_Forbidden_When_Tenant_Claim_Does_Not_Match_Route()
    {
        var companyId        = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();

        // Same recruiter, but the tenant header on this client claims a different company than
        // the one in the route — must be rejected before the position-profile lookup even runs.
        using var mismatchedClient = AuthenticatedClient(differentCompany);

        var response = await mismatchedClient.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = Guid.NewGuid(),
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Vacancies_Succeeds_Without_AdvertTitle_And_GetVacancy_Falls_Back_To_PositionProfile_Title()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createPayload = await createResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(createPayload);
        Assert.Null(createPayload!.AdvertTitle);

        var positionProfileResponse = await client.GetAsync($"/api/companies/{companyId}/position-profiles/{referenceData.PositionProfileId}");
        Assert.Equal(HttpStatusCode.OK, positionProfileResponse.StatusCode);
        var positionProfileTitle = (await positionProfileResponse.Content.ReadFromJsonAsync<PositionProfileTitlePayload>())!.Title;

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/vacancies/{createPayload.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getPayload = await getResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(positionProfileTitle, getPayload!.EffectiveTitle);
    }

    [Fact]
    public async Task Post_Vacancies_Response_Does_Not_Include_DepartmentId_On_The_Wire()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Parse the raw JSON rather than a typed DTO to prove "departmentId" is genuinely absent from
        // the wire contract, not merely unused by our own payload record.
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var propertyNames = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.DoesNotContain(propertyNames, name => string.Equals(name, "departmentId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_Vacancies_Request_Ignores_Client_Supplied_DepartmentId()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        // DepartmentId is no longer a bound property of CreateVacancyRequest at all, so supplying one
        // must simply be ignored (not rejected) rather than causing any DepartmentId to be derived or
        // persisted on the vacancy.
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            departmentId = Guid.NewGuid(),
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_Vacancies_Persists_AdvertTitle_And_AdvertDescription_Exactly_As_Supplied()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        const string advertTitle = "Staff Platform Engineer";
        const string advertDescription = "Own reliability and platform tooling company-wide.";

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle,
            advertDescription,
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createPayload = await createResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(createPayload);

        // Re-fetch through GetVacancy — a persistence round-trip, proving the values written by
        // CreateVacancy survive being read back rather than only ever being echoed from the create
        // response itself.
        var getResponse = await client.GetAsync($"/api/companies/{companyId}/vacancies/{createPayload!.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getPayload = await getResponse.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(advertTitle, getPayload!.AdvertTitle);
        Assert.Equal(advertDescription, getPayload.AdvertDescription);
    }

    private sealed record VacancyPayload(
        Guid Id,
        Guid CompanyId,
        Guid PositionProfileId,
        string? AdvertTitle,
        string? AdvertDescription,
        string? EffectiveTitle);

    private sealed record PositionProfileTitlePayload(string Title);
}
