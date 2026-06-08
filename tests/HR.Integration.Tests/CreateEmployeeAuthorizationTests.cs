using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class CreateEmployeeAuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CreateEmployeeAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Employee_Returns_Forbidden_When_User_Has_No_Roles()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employee_Returns_Forbidden_For_Employee_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Bob",
            lastName = "Jones",
            workEmail = $"bob.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employee_Returns_Forbidden_For_Manager_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Carol",
            lastName = "White",
            workEmail = $"carol.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employee_Succeeds_For_HR_Administrator_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Diana",
            lastName = "Evans",
            workEmail = $"diana.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employee_Succeeds_For_Company_Administrator_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Frank",
            lastName = "Garcia",
            workEmail = $"frank.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
