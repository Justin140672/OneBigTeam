using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DeleteFutureCompensationRecordEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("a1a1a1a1-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("a1a1a1a1-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("a1a1a1a1-0000-0000-0000-000000000003");

    public DeleteFutureCompensationRecordEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Delete_Compensation_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Compensation_Removes_Future_Dated_Record()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

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
        var created = await createResponse.Content.ReadFromJsonAsync<IdPayload>();

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var historyResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/history");
        historyResponse.EnsureSuccessStatusCode();
        var history = await historyResponse.Content.ReadFromJsonAsync<HistoryPayload>();
        Assert.NotNull(history);
        Assert.DoesNotContain(history!.Items, i => i.Id == created.Id);
    }

    [Fact]
    public async Task Delete_Compensation_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CompensationTestHelpers.CreateEmployeeAsync(client, companyId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Compensation_Returns_Conflict_When_Record_Is_Not_Future_Dated()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

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
        var created = await createResponse.Content.ReadFromJsonAsync<IdPayload>();

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation/{created!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record HistoryItemPayload(Guid Id);

    private sealed record HistoryPayload(List<HistoryItemPayload> Items);
}
