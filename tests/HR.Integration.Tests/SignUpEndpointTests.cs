using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SignUpEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public SignUpEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static object ValidSignUpRequest(string? email = null) => new
    {
        companyName = $"Acme-{Guid.NewGuid():N}",
        adminFirstName = "Ada",
        adminLastName = "Lovelace",
        adminEmail = email ?? $"ada-{Guid.NewGuid():N}@example.com",
        password = "P@ssw0rd123",
    };

    [Fact]
    public async Task Post_SignUp_Does_Not_Return_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest());

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Creates_Company_And_Admin_User_On_Happy_Path()
    {
        using var client = _factory.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SignUpPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.UserId);
        Assert.NotEqual(Guid.Empty, payload.CompanyId);
        Assert.Equal(email, payload.Email);
        Assert.Equal("Ada", payload.FirstName);
        Assert.Equal("Lovelace", payload.LastName);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == payload.UserId);
        Assert.NotNull(user);
        Assert.False(user!.IsEmailConfirmed);

        // Also granted SystemRoles.Employee alongside CompanyAdministrator — it's the floor role
        // required by "role:employee", which gates core session endpoints (GetMe, GetCompany,
        // etc.) that every seeded persona also carries (see SignUpHandler remarks).
        var roleIds = await db.UserRoles.Where(r => r.UserId == payload.UserId).Select(r => r.RoleId).ToListAsync();
        Assert.Contains(SystemRoles.CompanyAdministrator, roleIds);
        Assert.Contains(SystemRoles.Employee, roleIds);
        Assert.Equal(2, roleIds.Count);
    }

    [Fact]
    public async Task Post_SignUp_Returns_Conflict_For_Duplicate_Email()
    {
        using var client = _factory.CreateClient();
        var email = $"dup-{Guid.NewGuid():N}@example.com";

        var first = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Returns_BadRequest_When_CompanyName_Is_Empty()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", new
        {
            companyName = string.Empty,
            adminFirstName = "Ada",
            adminLastName = "Lovelace",
            adminEmail = $"ada-{Guid.NewGuid():N}@example.com",
            password = "P@ssw0rd123",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Returns_BadRequest_When_Email_Is_Invalid()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", new
        {
            companyName = $"Acme-{Guid.NewGuid():N}",
            adminFirstName = "Ada",
            adminLastName = "Lovelace",
            adminEmail = "not-an-email",
            password = "P@ssw0rd123",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_SignUp_Returns_BadRequest_When_Password_Is_Too_Short()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/signup", new
        {
            companyName = $"Acme-{Guid.NewGuid():N}",
            adminFirstName = "Ada",
            adminLastName = "Lovelace",
            adminEmail = $"ada-{Guid.NewGuid():N}@example.com",
            password = "short1",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record SignUpPayload(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);
}
