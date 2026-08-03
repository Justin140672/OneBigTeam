using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateLeaveTypeEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000003-0000-0000-0000-000000000001");

    public UpdateLeaveTypeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Put_LeaveType_Updates_Successfully()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var created = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId,
            name = "Annual Leave",
            code = "ANNUAL",
            defaultEntitlementDays = 25,
            accrualMethod = "Monthly",
            behaviour = "Standard"
        });
        created.EnsureSuccessStatusCode();
        var createdPayload = await created.Content.ReadFromJsonAsync<LeaveTypePayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-types/{createdPayload!.Id}", new
            {
                companyId,
                id = createdPayload.Id,
                name = "Annual Holiday",
                code = "ANNUAL",
                defaultEntitlementDays = 28,
                accrualMethod = "Monthly",
                behaviour = "Standard"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveTypePayload>();
        Assert.Equal("Annual Holiday", payload!.Name);
        Assert.Equal(28, payload.DefaultEntitlementDays);
    }

    [Fact]
    public async Task Put_LeaveType_Returns_NotFound_For_Unknown_Id()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-types/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                name = "Annual Leave",
                code = "ANNUAL",
                defaultEntitlementDays = 25,
                accrualMethod = "Monthly",
                behaviour = "Standard"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_LeaveType_Returns_Conflict_For_Duplicate_Code()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId, name = "Annual Leave", code = "ANNUAL",
            defaultEntitlementDays = 25, accrualMethod = "Monthly", behaviour = "Standard"
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-types", new
        {
            companyId, name = "Sick Leave", code = "SICK",
            defaultEntitlementDays = 10, accrualMethod = "None", behaviour = "Sickness"
        });
        second.EnsureSuccessStatusCode();
        var secondPayload = await second.Content.ReadFromJsonAsync<LeaveTypePayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/leave-types/{secondPayload!.Id}", new
            {
                companyId,
                id = secondPayload.Id,
                name = "Sick Leave",
                code = "ANNUAL",
                defaultEntitlementDays = 10,
                accrualMethod = "None",
                behaviour = "Sickness"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record LeaveTypePayload(
        Guid Id, Guid CompanyId, string Name, string Code,
        int DefaultEntitlementDays, string AccrualMethod, string Behaviour,
        bool IsActive, DateTimeOffset UpdatedAt);
}
