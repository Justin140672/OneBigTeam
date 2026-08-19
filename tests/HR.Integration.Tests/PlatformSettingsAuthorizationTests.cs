using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// SEC-002 regression coverage: the "platform:admin" FastEndpoints policy on GetPlatformSettings /
/// UpdatePlatformSettings used to be RequireAuthenticatedUser() only, so any authenticated caller
/// of any tenant/role could read or mutate global platform settings. It is now backed by
/// PlatformAdminAuthorizationHandler, which succeeds only for a caller matching an enabled
/// identity.platform_administrators row (by SupabaseAuthUserId or, as a fallback, by email). This
/// covers the full authorization matrix for both endpoints: anonymous, authenticated-but-not-an-
/// admin (across several roles), a disabled (revoked) administrator, and an enabled administrator
/// matched via each of the two lookup paths.
/// </summary>
[Collection("Integration")]
public class PlatformSettingsAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public PlatformSettingsAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static object ValidBody() => new
    {
        trialLengthDays = 21,
        defaultMonthlyPriceGbp = 19.99m,
        supportEmail = "help@example.com",
        maintenanceModeEnabled = false,
        maintenanceModeMessage = (string?)null,
        featureFlags = new Dictionary<string, bool>(),
    };

    private HttpClient ClientFor(Guid userId, string? email = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    private async Task<HttpClient> ClientForRoleAsync(Guid roleId)
    {
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId);
        return ClientFor(userId);
    }

    public static IEnumerable<object[]> Endpoints()
    {
        yield return [HttpMethod.Get];
        yield return [HttpMethod.Put];
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method)
    {
        return method == HttpMethod.Get
            ? client.GetAsync("/api/companies/admin/platform-settings")
            : client.PutAsJsonAsync("/api/companies/admin/platform-settings", ValidBody());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns_Unauthorized_For_Anonymous_Request(HttpMethod method)
    {
        using var client = _factory.CreateClient();

        var response = await SendAsync(client, method);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns_Forbidden_For_Authenticated_Employee_With_No_PlatformAdministrator_Row(HttpMethod method)
    {
        using var client = await ClientForRoleAsync(SystemRoles.Employee);

        var response = await SendAsync(client, method);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns_Forbidden_For_Authenticated_CompanyAdministrator_With_No_PlatformAdministrator_Row(HttpMethod method)
    {
        using var client = await ClientForRoleAsync(SystemRoles.CompanyAdministrator);

        var response = await SendAsync(client, method);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns_Forbidden_For_Authenticated_HrAdministrator_With_No_PlatformAdministrator_Row(HttpMethod method)
    {
        using var client = await ClientForRoleAsync(SystemRoles.HrAdministrator);

        var response = await SendAsync(client, method);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns_Forbidden_For_Disabled_PlatformAdministrator(HttpMethod method)
    {
        var userId = Guid.NewGuid();
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory,
            PlatformAdministratorRole.SupportStaff,
            isEnabled: false,
            supabaseAuthUserId: userId);

        using var client = ClientFor(userId);

        var response = await SendAsync(client, method);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Succeeds_For_Enabled_PlatformAdministrator_Matched_By_SupabaseAuthUserId(HttpMethod method)
    {
        var userId = Guid.NewGuid();
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory,
            PlatformAdministratorRole.SupportStaff,
            isEnabled: true,
            supabaseAuthUserId: userId);

        using var client = ClientFor(userId);

        var response = await SendAsync(client, method);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Succeeds_For_Enabled_PlatformAdministrator_Matched_By_Email_Fallback(HttpMethod method)
    {
        // No SupabaseAuthUserId link on the seeded row — only the (case-insensitively matched)
        // email connects this caller to the PlatformAdministrator row, exercising
        // PlatformAdminAuthorizationHandler's fallback lookup path.
        var email = $"fallback-admin-{Guid.NewGuid():N}@test.example";
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory,
            PlatformAdministratorRole.SupportStaff,
            isEnabled: true,
            email: email);

        using var client = ClientFor(Guid.NewGuid(), email: email.ToUpperInvariant());

        var response = await SendAsync(client, method);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
