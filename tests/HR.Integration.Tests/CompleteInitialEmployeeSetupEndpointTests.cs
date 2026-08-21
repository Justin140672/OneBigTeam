using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

// The "requires initial setup" flag is only ever set by the self-service signup flow, on the
// initial company admin's own Employee record (see EmployeeProvisioningService.
// MarkAsInitialCompanyAdminAsync). Rather than reaching into the domain via reflection, these
// tests drive /api/signup to get a real employee with RequiresInitialSetup == true and a seeded
// placeholder Compensation record, matching how this state actually arises in production.
[Collection("Integration")]
public class CompleteInitialEmployeeSetupEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CompleteInitialEmployeeSetupEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static object ValidSignUpRequest(string? email = null) => new
    {
        companyName = $"Acme-{Guid.NewGuid():N}",
        adminFirstName = "Ada",
        adminLastName = "Lovelace",
        adminEmail = email ?? $"ada-{Guid.NewGuid():N}@example.com",
        password = "P@ssw0rd123",
    };

    private static object ValidSetupRequest() => new
    {
        firstName = "Ada",
        lastName = "Lovelace",
        dateOfBirth = "1990-06-15",
        nationality = "British",
        gender = "Female",
        addressLine1 = "1 Test Street",
        city = "London",
        postCode = "SW1A 1AA"
    };

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> SignUpAsync()
    {
        using var anonymousClient = _factory.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        _factory.SupabaseAuthGateway.UserIdToReturn = Guid.NewGuid();

        var response = await anonymousClient.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SignUpPayload>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, payload!.UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, payload.CompanyId.ToString());

        return (client, payload.CompanyId, payload.UserId);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/me/complete-initial-setup",
            ValidSetupRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Succeeds_And_Reflects_In_GetMyEmployee()
    {
        var (client, companyId, _) = await SignUpAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            ValidSetupRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompleteSetupPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.RequiresInitialSetup);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/employees/me");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var getPayload = await getResponse.Content.ReadFromJsonAsync<GetMyEmployeePayload>();
        Assert.NotNull(getPayload);
        Assert.False(getPayload!.RequiresInitialSetup);
        Assert.Equal("Ada", getPayload.FirstName);
        Assert.Equal("Lovelace", getPayload.LastName);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Returns_Conflict_When_Already_Completed()
    {
        var (client, companyId, _) = await SignUpAsync();

        var first = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            ValidSetupRequest());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            ValidSetupRequest());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Returns_422_When_DateOfBirth_Is_On_Or_Before_1900_01_01()
    {
        var (client, companyId, _) = await SignUpAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                dateOfBirth = "1900-01-01",
                nationality = "British",
                gender = "Female",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Returns_422_When_AddressLine1_Missing()
    {
        var (client, companyId, _) = await SignUpAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                dateOfBirth = "1990-06-15",
                nationality = "British",
                gender = "Female",
                city = "London",
                postCode = "SW1A 1AA"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Returns_422_When_City_Missing()
    {
        var (client, companyId, _) = await SignUpAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                dateOfBirth = "1990-06-15",
                nationality = "British",
                gender = "Female",
                addressLine1 = "1 Test Street",
                postCode = "SW1A 1AA"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Returns_422_When_PostCode_Missing()
    {
        var (client, companyId, _) = await SignUpAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            new
            {
                firstName = "Ada",
                lastName = "Lovelace",
                dateOfBirth = "1990-06-15",
                nationality = "British",
                gender = "Female",
                addressLine1 = "1 Test Street",
                city = "London"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_CompleteInitialSetup_Returns_400_When_Employee_Has_No_Compensation_Records()
    {
        var (client, companyId, employeeId) = await SignUpAsync();

        // Signup seeds a placeholder Compensation record alongside RequiresInitialSetup — remove it
        // to exercise the handler's explicit "at least one compensation record" guard, which never
        // arises through the production signup flow itself.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var compensations = await db.Compensations.Where(c => c.EmployeeId == employeeId).ToListAsync();
            db.Compensations.RemoveRange(compensations);
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/complete-initial-setup",
            ValidSetupRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record SignUpPayload(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);

    private sealed record CompleteSetupPayload(Guid EmployeeId, bool RequiresInitialSetup, string Status);

    private sealed record GetMyEmployeePayload(
        Guid EmployeeId, string FirstName, string LastName, string? JobTitle, bool RequiresInitialSetup);
}
