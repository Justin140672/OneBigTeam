using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetCurrentCompensationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("b2b2b2b2-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("b2b2b2b2-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("b2b2b2b2-0000-0000-0000-000000000003");

    public GetCurrentCompensationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_CurrentCompensation_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/compensation/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_CurrentCompensation_Returns_Record_Effective_Today()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation", new
            {
                companyId,
                employeeId,
                effectiveFrom = "2026-01-01",
                salaryType = "Annual",
                salary = 55000m,
                currency = "GBP",
                reason = "NewHire"
            });
        createResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CurrentCompensationPayload>();
        Assert.NotNull(payload);
        Assert.Equal(employeeId, payload!.EmployeeId);
        Assert.Equal(55000m, payload.Salary);
        Assert.Equal("GBP", payload.Currency);
        Assert.Equal("NewHire", payload.Reason);
        Assert.NotEqual(Guid.Empty, payload.CreatedBy);
    }

    [Fact]
    public async Task Get_CurrentCompensation_Returns_NotFound_When_Employee_Has_No_Compensation_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/current");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_CurrentCompensation_Returns_NotFound_When_Only_Future_Dated_Record_Exists()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation", new
            {
                companyId,
                employeeId,
                effectiveFrom = "2099-01-01",
                salaryType = "Annual",
                salary = 55000m,
                currency = "GBP",
                reason = "NewHire"
            });
        createResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/current");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CurrentCompensationPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        DateOnly EffectiveFrom,
        DateOnly? EffectiveTo,
        string SalaryType,
        decimal Salary,
        decimal? AnnualisedSalary,
        string Currency,
        decimal? HoursPerWeek,
        decimal? FTE,
        string? Notes,
        string Reason,
        Guid CreatedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
