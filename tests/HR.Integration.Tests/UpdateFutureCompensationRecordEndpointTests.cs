using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateFutureCompensationRecordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("ffffffff-0000-0000-0000-000000000004");

    public UpdateFutureCompensationRecordEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Put_Compensation_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{Guid.NewGuid()}", new
            {
                companyId,
                employeeId,
                id = Guid.NewGuid(),
                salaryType = "Annual",
                salary = 60000m,
                currency = "GBP",
                reason = "AnnualReview"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Compensation_Updates_Future_Dated_Record()
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
                effectiveFrom = "2099-01-01",
                salaryType = "Annual",
                salary = 50000m,
                currency = "GBP",
                reason = "NewHire"
            });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CompensationPayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{created!.Id}", new
            {
                companyId,
                employeeId,
                id = created.Id,
                salaryType = "Annual",
                salary = 65000m,
                currency = "GBP",
                notes = "Updated via PUT.",
                reason = "AnnualReview"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompensationPayload>();
        Assert.NotNull(payload);
        Assert.Equal(65000m, payload!.Salary);
        Assert.Equal("Updated via PUT.", payload.Notes);
        Assert.Equal("AnnualReview", payload.Reason);
        Assert.NotEqual(Guid.Empty, payload.CreatedBy);
    }

    [Fact]
    public async Task Put_Compensation_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{Guid.NewGuid()}", new
            {
                companyId,
                employeeId,
                id = Guid.NewGuid(),
                salaryType = "Annual",
                salary = 60000m,
                currency = "GBP",
                reason = "AnnualReview"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Compensation_Returns_Conflict_When_Record_Is_Not_Future_Dated()
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
                effectiveFrom = "2026-01-01",
                salaryType = "Annual",
                salary = 50000m,
                currency = "GBP",
                reason = "NewHire"
            });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CompensationPayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{created!.Id}", new
            {
                companyId,
                employeeId,
                id = created.Id,
                salaryType = "Annual",
                salary = 65000m,
                currency = "GBP",
                reason = "AnnualReview"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Compensation_Returns_UnprocessableEntity_For_Missing_Fields()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation", new
            {
                companyId,
                employeeId,
                effectiveFrom = "2099-01-01",
                salaryType = "Annual",
                salary = 50000m,
                currency = "GBP",
                reason = "NewHire"
            });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CompensationPayload>();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{created!.Id}", new
            {
                companyId,
                employeeId,
                id = created.Id,
                salaryType = "Annual",
                salary = 0m,
                currency = "GBP"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record CompensationPayload(
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
