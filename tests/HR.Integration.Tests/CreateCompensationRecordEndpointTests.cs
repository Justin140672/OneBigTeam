using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateCompensationRecordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("eeeeeeee-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("eeeeeeee-0000-0000-0000-000000000004");

    public CreateCompensationRecordEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_Compensation_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom = "2026-01-01",
            salaryType = "Annual",
            salary = 50000m,
            currency = "GBP",
            reason = "NewHire"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Compensation_Creates_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom = "2026-01-01",
            salaryType = "Annual",
            salary = 55000m,
            currency = "GBP",
            hoursPerWeek = 37.5m,
            fte = 1.0m,
            notes = "Starting salary.",
            reason = "NewHire"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompensationPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal("Annual", payload.SalaryType);
        Assert.Equal(55000m, payload.Salary);
        Assert.Equal("GBP", payload.Currency);
        Assert.Equal("NewHire", payload.Reason);
        Assert.NotEqual(Guid.Empty, payload.CreatedBy);
    }

    [Fact]
    public async Task Post_Compensation_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom = "2026-01-01",
            salaryType = "Annual",
            salary = 50000m,
            currency = "GBP",
            reason = "NewHire"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Compensation_Returns_Conflict_When_EffectiveFrom_Overlaps_Existing_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom = "2099-01-01",
            salaryType = "Annual",
            salary = 50000m,
            currency = "GBP",
            reason = "NewHire"
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId,
            effectiveFrom = "2099-01-01",
            salaryType = "Annual",
            salary = 60000m,
            currency = "GBP",
            reason = "Correction"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Compensation_Returns_UnprocessableEntity_For_Missing_Fields()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User4, SystemRoles.HrAdministrator, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees/{employeeId}/compensation", new
        {
            companyId,
            employeeId
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    internal sealed record CompensationPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
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
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
