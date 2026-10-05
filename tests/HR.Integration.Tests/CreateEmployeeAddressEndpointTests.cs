using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateEmployeeAddressEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrUser = new("aaaaaaaa-0000-0000-0000-0000000000a1");

    public CreateEmployeeAddressEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid CompanyId, EmployeeReferenceDataSeeder.ReferenceData Reference)> SetupAsync()
    {
        var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, HrUser, SystemRoles.HrAdministrator, companyId);
        var reference = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        return (client, companyId, reference);
    }

    private async Task<int> CountEmployeesAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EmployeesDbContext>()
            .Employees.CountAsync(e => e.CompanyId == companyId);
    }

    [Fact]
    public async Task Post_Employees_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/employees", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("addressLine1", null)]
    [InlineData("addressLine1", "")]
    [InlineData("addressLine1", "   ")]
    [InlineData("city", null)]
    [InlineData("city", "")]
    [InlineData("city", "   ")]
    [InlineData("postCode", null)]
    [InlineData("postCode", "")]
    [InlineData("postCode", "   ")]
    public async Task Post_Employees_Rejects_Missing_Or_Whitespace_Required_Address_Fields(string field, string? value)
    {
        var (client, companyId, reference) = await SetupAsync();
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, reference, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        body[field] = value;

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountEmployeesAsync(companyId));
    }

    [Fact]
    public async Task Post_Employees_Cannot_Bypass_The_Address_Requirement_By_Claiming_To_Be_The_Initial_Admin()
    {
        var (client, companyId, reference) = await SetupAsync();
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, reference, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        body["addressLine1"] = null;
        body["isInitialCompanyAdmin"] = true;

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.True(response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest);
        Assert.Equal(0, await CountEmployeesAsync(companyId));
    }

    [Fact]
    public async Task Post_Employees_Rejects_A_Postcode_That_Does_Not_Match_The_Company_Rule()
    {
        var (client, companyId, reference) = await SetupAsync();
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, reference, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        body["postCode"] = "not a postcode";

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountEmployeesAsync(companyId));
    }

    [Fact]
    public async Task Post_Employees_Persists_The_Normalized_Address_And_Allows_Optional_Fields_To_Be_Blank()
    {
        var (client, companyId, reference) = await SetupAsync();
        using var _ = client;
        var body = ToBody(EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
            companyId, reference, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com"));
        body["addressLine1"] = "  1 Test Street  ";
        body["addressLine2"] = "  ";
        body["city"] = " London ";
        body["county"] = null;
        body["postCode"] = " SW1A 1AA ";

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<IdPayload>())!;
        using var scope = _factory.Services.CreateScope();
        var employee = await scope.ServiceProvider.GetRequiredService<EmployeesDbContext>()
            .Employees.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("1 Test Street", employee.AddressLine1);
        Assert.Null(employee.AddressLine2);
        Assert.Equal("London", employee.City);
        Assert.Null(employee.County);
        Assert.Equal("SW1A 1AA", employee.PostCode);
    }

    private static Dictionary<string, object?> ToBody(object request)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(request);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    }

    private sealed record IdPayload(Guid Id);
}
