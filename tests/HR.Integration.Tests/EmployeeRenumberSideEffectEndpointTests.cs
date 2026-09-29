using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class EmployeeRenumberSideEffectEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("eeeeeeee-1111-0000-0000-000000000004");

    public EmployeeRenumberSideEffectEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId, ensureActiveSubscription);
        return client;
    }

    private async Task<Guid> CreateCompanyAsync(Guid tenantId)
    {
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Renumber Side Effect Test {Guid.NewGuid():N}", companyId: tenantId);
    }

    private async Task<int> EstablishAutomaticModeAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/hr-settings", FormatChangingHrSettingsBody(version: 1, prefix: null, minimumLength: 4));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.EmployeeRenumberSideEffectId);
        return payload.Version;
    }

    private static object FormatChangingHrSettingsBody(int version, string? prefix = "EMP-", int minimumLength = 6) => new
    {
        workingDays = 31,
        hoursPerDay = 7.5,
        leaveYearStartMonth = 1,
        defaultHolidayAllowance = 25,
        probationMonths = 6,
        employeeNumberMode = "Automatic",
        employeeNumberPrefix = prefix,
        nextEmployeeNumber = 1,
        employeeNumberMinimumLength = minimumLength,
        version,
    };

    [Fact]
    public async Task Put_Hr_Settings_With_FormatChange_Returns_Ok_With_Populated_SideEffect_Fields()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);
        var version = await EstablishAutomaticModeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/hr-settings", FormatChangingHrSettingsBody(version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.EmployeeRenumberSideEffectId);
        Assert.Equal("pending", payload.EmployeeRenumberSideEffectStatus);
    }

    [Fact]
    public async Task Put_Hr_Settings_With_Second_FormatChange_While_First_Still_Pending_Returns_Conflict()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);
        var baselineVersion = await EstablishAutomaticModeAsync(client, companyId);

        var firstResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/hr-settings", FormatChangingHrSettingsBody(baselineVersion));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        Assert.NotNull(firstPayload);

        var secondResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/hr-settings",
            FormatChangingHrSettingsBody(version: firstPayload!.Version, prefix: "EMP2-", minimumLength: 9));

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Get_SideEffect_Status_Returns_Ok_For_HrAdministrator()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);
        var version = await EstablishAutomaticModeAsync(client, companyId);

        var putResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/hr-settings", FormatChangingHrSettingsBody(version));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putPayload = await putResponse.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        Assert.NotNull(putPayload?.EmployeeRenumberSideEffectId);

        var getResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employee-renumber-side-effects/{putPayload!.EmployeeRenumberSideEffectId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var statusPayload = await getResponse.Content.ReadFromJsonAsync<SideEffectStatusPayload>();
        Assert.NotNull(statusPayload);
        Assert.Equal(putPayload.EmployeeRenumberSideEffectId, statusPayload!.Id);
        Assert.Equal(companyId, statusPayload.CompanyId);
        Assert.Equal("pending", statusPayload.Status);
        Assert.Equal(0, statusPayload.AttemptCount);
    }

    [Fact]
    public async Task Get_SideEffect_Status_Returns_NotFound_For_Unknown_Id()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employee-renumber-side-effects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_SideEffect_Status_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employee-renumber-side-effects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Retry_SideEffect_Returns_BadRequest_When_Status_Is_Not_Failed()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);
        var version = await EstablishAutomaticModeAsync(client, companyId);

        var putResponse = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/hr-settings", FormatChangingHrSettingsBody(version));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var putPayload = await putResponse.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        Assert.NotNull(putPayload?.EmployeeRenumberSideEffectId);

        var retryResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employee-renumber-side-effects/{putPayload!.EmployeeRenumberSideEffectId}/retry",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, retryResponse.StatusCode);
    }

    [Fact]
    public async Task Retry_SideEffect_Returns_NotFound_For_Unknown_Id()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employee-renumber-side-effects/{Guid.NewGuid()}/retry",
            new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Retry_SideEffect_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employee-renumber-side-effects/{Guid.NewGuid()}/retry",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record UpdateHrSettingsPayload(
        Guid CompanyId,
        int Version,
        Guid? EmployeeRenumberSideEffectId,
        string? EmployeeRenumberSideEffectStatus);

    private sealed record SideEffectStatusPayload(
        Guid Id,
        Guid CompanyId,
        string Status,
        int AttemptCount,
        DateTimeOffset? LastAttemptAt,
        string? ErrorMessage,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ProcessedAt,
        DateTimeOffset? FailedAt);
}
