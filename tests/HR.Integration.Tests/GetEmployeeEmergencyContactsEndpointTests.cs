using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeeEmergencyContactsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetEmployeeEmergencyContactsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_EmployeeEmergencyContacts_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/emergency-contacts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeEmergencyContacts_Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var hrAdminId = Guid.NewGuid();
        var employeeId = await CreateEmployeeAsync(companyId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, hrAdminId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.Employee, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/emergency-contacts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmergencyContactsResponsePayload>();
        Assert.NotNull(payload);
        Assert.IsType<List<EmergencyContactItem>>(payload!.Contacts);
    }

    [Fact]
    public async Task Get_EmployeeEmergencyContacts_Returns_Forbidden_For_Manager_Accessing_Another_Employee()
    {
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var employeeId = await CreateEmployeeAsync(companyId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, managerId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Employee, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/emergency-contacts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeEmergencyContacts_Returns_Forbidden_For_Recruiter()
    {
        var companyId = Guid.NewGuid();
        var recruiterId = Guid.NewGuid();
        var employeeId = await CreateEmployeeAsync(companyId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, recruiterId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, recruiterId, SystemRoles.Recruiter, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, recruiterId, SystemRoles.Employee, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/emergency-contacts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeEmergencyContacts_Returns_Forbidden_For_Employee_Accessing_Another_Employee()
    {
        var companyId = Guid.NewGuid();
        var employeeId1 = await CreateEmployeeAsync(companyId);
        var employeeId2 = await CreateEmployeeAsync(companyId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId1, SystemRoles.Employee, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId2}/emergency-contacts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    private async Task<Guid> CreateEmployeeAsync(Guid companyId)
    {
        var hrAdminId = Guid.NewGuid();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, hrAdminId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrAdminId, SystemRoles.Employee, companyId);

        var referenceData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, referenceData, "Test", $"Employee-{Guid.NewGuid():N}",
                $"emergency-contacts-test.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EmployeeIdPayload>();
        await TestRoleSeeder.AssignRoleAsync(_factory, payload!.Id, SystemRoles.Employee, companyId);

        return payload.Id;
    }

    private sealed record EmployeeIdPayload(Guid Id);
    private sealed record EmergencyContactItem(
        Guid Id,
        string Name,
        string Relationship,
        string PhoneNumber,
        string? Email);
    private sealed record EmergencyContactsResponsePayload(List<EmergencyContactItem> Contacts);
}
