using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Unlike the other platform-administrator endpoints, ListPlatformAdministrators only requires the
/// caller to be ANY enabled PlatformAdministrator (SupportStaff or PlatformOwner) — see
/// ListPlatformAdministratorsHandler.IsEnabledPlatformAdministratorAsync.
/// </summary>
[Collection("Integration")]
public class ListPlatformAdministratorsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ListPlatformAdministratorsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_PlatformAdministrators_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/platform-administrators");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PlatformAdministrators_Returns_Unauthorized_When_Caller_Is_Not_An_Administrator()
    {
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), "not-an-admin@test.example");

        var response = await client.GetAsync("/api/platform-administrators");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PlatformAdministrators_Returns_Unauthorized_When_Caller_Is_Disabled()
    {
        var (_, disabledEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, isEnabled: false);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), disabledEmail);

        var response = await client.GetAsync("/api/platform-administrators");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_PlatformAdministrators_Succeeds_For_Enabled_SupportStaff_Caller()
    {
        var (id1, email1) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        var (id2, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), email1);

        var response = await client.GetAsync("/api/platform-administrators");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Administrators, a => a.Id == id1);
        Assert.Contains(payload.Administrators, a => a.Id == id2);
    }

    private sealed record AdministratorSummaryPayload(
        Guid Id, string Email, string Role, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset? DisabledAt);

    private sealed record ListPayload(List<AdministratorSummaryPayload> Administrators);
}
