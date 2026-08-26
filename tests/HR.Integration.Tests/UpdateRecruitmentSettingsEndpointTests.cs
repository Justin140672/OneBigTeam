using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// See UpdateRecruitmentSettingsHandlerTests/UpdateRecruitmentSettingsValidatorTests/
/// CompanySettingsRecruitmentSettingsTests in HR.Modules.Companies.Tests for the equivalent
/// unit-level coverage of the same behaviour.
/// </summary>
[Collection("Integration")]
public class UpdateRecruitmentSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("ce000030-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterOnlyUserId = new("ce000030-0000-0000-0000-000000000002");

    public UpdateRecruitmentSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterOnlyUserId, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterOnlyUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId);
        return client;
    }

    private static object ValidBody(int version = 1) => new
    {
        vacancyApprovalRequired = true,
        offerApprovalRequired = true,
        candidateRetentionDays = 365,
        version,
    };

    [Fact]
    public async Task Put_RecruitmentSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment-settings", ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentSettings_Succeeds_For_HrAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/recruitment-settings", ValidBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecruitmentSettingsPayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.VacancyApprovalRequired);
        Assert.True(payload.OfferApprovalRequired);
        Assert.Equal(365, payload.CandidateRetentionDays);
    }

    [Fact]
    public async Task Put_RecruitmentSettings_Returns_Forbidden_For_Recruiter_Only_Role()
    {
        // Proves "the Recruiter role alone cannot change company-wide configuration": Recruiter
        // holds recruitment:manage but not hr-settings:manage.
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(RecruiterOnlyUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/recruitment-settings", ValidBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentSettings_Returns_UnprocessableEntity_When_CandidateRetentionDays_Below_Minimum()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/recruitment-settings", new
        {
            vacancyApprovalRequired = false,
            offerApprovalRequired = false,
            candidateRetentionDays = 89,
            version = 1,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentSettings_Returns_UnprocessableEntity_When_CandidateRetentionDays_Above_Maximum()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/recruitment-settings", new
        {
            vacancyApprovalRequired = false,
            offerApprovalRequired = false,
            candidateRetentionDays = 3651,
            version = 1,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_RecruitmentSettings_Returns_Conflict_When_Version_Is_Stale()
    {
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var firstResponse = await client.PutAsJsonAsync($"/api/companies/{tenantId}/recruitment-settings", ValidBody(version: 1));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PutAsJsonAsync($"/api/companies/{tenantId}/recruitment-settings", ValidBody(version: 1));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    private sealed record RecruitmentSettingsPayload(
        Guid CompanyId,
        bool VacancyApprovalRequired,
        bool OfferApprovalRequired,
        int CandidateRetentionDays,
        int Version);
}
