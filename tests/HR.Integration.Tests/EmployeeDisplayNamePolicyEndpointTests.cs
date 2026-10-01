using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class EmployeeDisplayNamePolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public EmployeeDisplayNamePolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> HrClientAsync(Guid companyId)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(
        HttpClient client, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData refData,
        string first, string last, Guid? managerId = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, first, last, $"{first}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                managerId: managerId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task SetPreferredNameAsync(Guid employeeId, string preferred)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = await db.Employees.FindAsync(employeeId);
        employee!.UpdatePersonalDetails(
            preferred, employee.DateOfBirth, employee.Nationality, employee.Gender, employee.GenderOther, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListEmployees_Search_Matches_Preferred_Name_And_Manager_Name_Uses_Display_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = await HrClientAsync(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var managerId = await CreateEmployeeAsync(client, companyId, refData, "Sara", "Chen");
        await SetPreferredNameAsync(managerId, "Sarah");
        await CreateEmployeeAsync(client, companyId, refData, "Bob", "Jones", managerId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees?search=sarah");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var manager = Assert.Single(payload.GetProperty("items").EnumerateArray());
        Assert.Equal("Sarah", manager.GetProperty("preferredName").GetString());
        Assert.Equal("Sara", manager.GetProperty("firstName").GetString());

        var all = await (await client.GetAsync($"/api/companies/{companyId}/employees"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var report = all.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("firstName").GetString() == "Bob");
        Assert.Equal("Sarah Chen", report.GetProperty("managerFullName").GetString());
    }

    [Fact]
    public async Task DirectorySearch_Finds_By_Preferred_Name_And_Legal_Name()
    {
        var companyId = Guid.NewGuid();
        using var client = await HrClientAsync(companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var id = await CreateEmployeeAsync(client, companyId, refData, "Sara", "Chen");
        await SetPreferredNameAsync(id, "Sarah");

        foreach (var term in new[] { "sarah", "sara chen", "sarah chen" })
        {
            var response = await client.GetAsync($"/api/companies/{companyId}/employees/directory-search?term={Uri.EscapeDataString(term)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(payload.GetProperty("items").EnumerateArray());
        }
    }
}
