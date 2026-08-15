using System.Net.Http.Json;
using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

/// <summary>
/// Wraps the Platform Settings endpoints (final Admin Portal story). Modeled on AdminUsersService:
/// HttpClientFactory "hrapi" client, GetXxxOrNullAsync returning null on any failure. The PUT call
/// additionally surfaces FastEndpoints/FluentValidation 422 field errors (shape:
/// {"Errors": {"Field": ["message"]}}, mirrored from HR.Web's EmployeeService pattern) rather than
/// swallowing them, per the story's acceptance criteria.
/// </summary>
public sealed class PlatformSettingsService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<PlatformSettingsModel?> GetSettingsOrNullAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync("api/companies/admin/platform-settings", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<PlatformSettingsModel>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<UpdatePlatformSettingsResult> UpdateSettingsAsync(
        UpdatePlatformSettingsRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync("api/companies/admin/platform-settings", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var settings = await response.Content.ReadFromJsonAsync<PlatformSettingsModel>(cancellationToken: cancellationToken);
                return new UpdatePlatformSettingsResult(settings, null);
            }

            if ((int)response.StatusCode == 422)
            {
                var body = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken: cancellationToken);
                var errors = body?.Errors?.Values.SelectMany(v => v).ToList();
                if (errors is { Count: > 0 })
                    return new UpdatePlatformSettingsResult(null, errors);
            }

            return new UpdatePlatformSettingsResult(null, ["Could not save platform settings. You may not be authorised to perform this action."]);
        }
        catch (HttpRequestException)
        {
            return new UpdatePlatformSettingsResult(null, ["A network error occurred while saving platform settings. Please try again."]);
        }
    }

    private sealed record ValidationErrorEnvelope(Dictionary<string, string[]>? Errors);
}
