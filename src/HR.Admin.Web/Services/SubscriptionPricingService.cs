using System.Net.Http.Json;

using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

/// <summary>
/// Wraps the configurable subscription pricing endpoints (Story 4). Modeled on
/// PlatformSettingsService: "hrapi" HttpClientFactory client, null on any read failure. The PUT
/// surfaces both the handler's structural-validation failure (400 <c>{"error": "..."}</c> from
/// SubscriptionPricingConfig.Validate) and FluentValidation 422 field errors.
/// </summary>
public sealed class SubscriptionPricingService(IHttpClientFactory httpClientFactory)
{
    private const string Route = "api/companies/admin/subscription-pricing-config";

    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<SubscriptionPricingConfigModel?> GetConfigOrNullAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(Route, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<SubscriptionPricingConfigModel>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<UpdateSubscriptionPricingConfigResult> UpdateConfigAsync(
        UpdateSubscriptionPricingConfigRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(Route, request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var config = await response.Content.ReadFromJsonAsync<SubscriptionPricingConfigModel>(cancellationToken: cancellationToken);
                return new UpdateSubscriptionPricingConfigResult(config, null);
            }

            if ((int)response.StatusCode == 400)
            {
                var body = await response.Content.ReadFromJsonAsync<BusinessErrorEnvelope>(cancellationToken: cancellationToken);
                if (!string.IsNullOrWhiteSpace(body?.Error))
                    return new UpdateSubscriptionPricingConfigResult(null, [body!.Error!]);
            }

            if ((int)response.StatusCode == 422)
            {
                var body = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken: cancellationToken);
                var errors = body?.Errors?.Values.SelectMany(v => v).ToList();
                if (errors is { Count: > 0 })
                    return new UpdateSubscriptionPricingConfigResult(null, errors);
            }

            return new UpdateSubscriptionPricingConfigResult(
                null,
                ["Could not save subscription pricing. You may not be authorised to perform this action."]);
        }
        catch (HttpRequestException)
        {
            return new UpdateSubscriptionPricingConfigResult(
                null,
                ["A network error occurred while saving subscription pricing. Please try again."]);
        }
    }

    private sealed record BusinessErrorEnvelope(string? Error);

    private sealed record ValidationErrorEnvelope(Dictionary<string, string[]>? Errors);
}
