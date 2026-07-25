using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetCompensationHistoryEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("c3c3c3c3-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("c3c3c3c3-0000-0000-0000-000000000002");

    public GetCompensationHistoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_CompensationHistory_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/compensation/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_CompensationHistory_Returns_All_Records_Ordered_By_EffectiveFrom_Descending()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation", new
            {
                companyId,
                employeeId,
                effectiveFrom = "2026-01-01",
                salaryType = "Annual",
                salary = 50000m,
                currency = "GBP",
                reason = "NewHire"
            });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation", new
            {
                companyId,
                employeeId,
                effectiveFrom = "2099-01-01",
                salaryType = "Annual",
                salary = 60000m,
                currency = "GBP",
                reason = "AnnualReview"
            });
        second.EnsureSuccessStatusCode();
        var secondPayload = await second.Content.ReadFromJsonAsync<IdPayload>();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);

        Assert.Equal(secondPayload!.Id, payload.Items[0].Id);
        Assert.True(payload.Items[0].EffectiveFrom > payload.Items[1].EffectiveFrom);

        Assert.All(payload.Items, i => Assert.NotEqual(Guid.Empty, i.CreatedBy));
        Assert.Contains(payload.Items, i => i.Reason == "NewHire");
        Assert.Contains(payload.Items, i => i.Reason == "AnnualReview");
    }

    [Fact]
    public async Task Get_CompensationHistory_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/compensation/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record HistoryItemPayload(
        Guid Id,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo,
        string SalaryType,
        decimal Salary,
        string Currency,
        decimal? HoursPerWeek,
        decimal? FTE,
        string? Notes,
        string Reason,
        Guid CreatedBy,
        DateTimeOffset CreatedAt);

    private sealed record HistoryPayload(List<HistoryItemPayload> Items);
}
