using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// See GetPlatformSettingsEndpointTests remarks: the "platform:admin" policy on this endpoint only
/// requires an authenticated caller (RequireAuthenticatedUser), no additional handler-level
/// allow-list/PlatformAdministrator gate.
/// </summary>
[Collection("Integration")]
public class UpdatePlatformSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdatePlatformSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        return client;
    }

    private static object ValidBody() => new
    {
        trialLengthDays = 30,
        defaultMonthlyPriceGbp = 29.99m,
        supportEmail = "help@example.com",
        maintenanceModeEnabled = true,
        maintenanceModeMessage = "Undergoing maintenance",
        featureFlags = new Dictionary<string, bool> { ["beta"] = true },
    };

    [Fact]
    public async Task Put_PlatformSettings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/companies/admin/platform-settings", ValidBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_PlatformSettings_Persists_Values_And_Reflects_On_Subsequent_Get()
    {
        using var client = AuthenticatedClient();

        var putResponse = await client.PutAsJsonAsync("/api/companies/admin/platform-settings", ValidBody());

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var putPayload = await putResponse.Content.ReadFromJsonAsync<PlatformSettingsPayload>();
        Assert.NotNull(putPayload);
        Assert.Equal(30, putPayload!.TrialLengthDays);
        Assert.Equal(29.99m, putPayload.DefaultMonthlyPriceGbp);
        Assert.Equal("help@example.com", putPayload.SupportEmail);
        Assert.True(putPayload.MaintenanceModeEnabled);
        Assert.Equal("Undergoing maintenance", putPayload.MaintenanceModeMessage);
        Assert.True(putPayload.FeatureFlags["beta"]);

        var getResponse = await client.GetAsync("/api/companies/admin/platform-settings");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var getPayload = await getResponse.Content.ReadFromJsonAsync<PlatformSettingsPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(30, getPayload!.TrialLengthDays);
        Assert.Equal("help@example.com", getPayload.SupportEmail);
        Assert.True(getPayload.FeatureFlags["beta"]);
    }

    [Fact]
    public async Task Put_PlatformSettings_Returns_BadRequest_When_TrialLengthDays_Is_Zero()
    {
        using var client = AuthenticatedClient();

        var body = new
        {
            trialLengthDays = 0,
            defaultMonthlyPriceGbp = 9.99m,
            supportEmail = "help@example.com",
            maintenanceModeEnabled = false,
            maintenanceModeMessage = (string?)null,
            featureFlags = new Dictionary<string, bool>(),
        };

        var response = await client.PutAsJsonAsync("/api/companies/admin/platform-settings", body);

        // FluentValidation rejects TrialLengthDays <= 0 before the handler runs, so this is the
        // FastEndpoints validation-failure response (422), not the handler's Result.Failure ->
        // BadRequest (400) mapping — see UpdatePlatformSettingsValidator and mirror
        // CreateAssetCategoryEndpointTests' "missing required field" case for this same 422 pattern.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record PlatformSettingsPayload(
        int TrialLengthDays,
        decimal DefaultMonthlyPriceGbp,
        string SupportEmail,
        bool MaintenanceModeEnabled,
        string? MaintenanceModeMessage,
        Dictionary<string, bool> FeatureFlags,
        DateTimeOffset UpdatedAt,
        Guid? UpdatedByUserId);
}
