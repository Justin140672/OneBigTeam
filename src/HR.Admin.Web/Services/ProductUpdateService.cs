using System.Net.Http.Json;

using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

/// <summary>
/// Wraps the Customer Release Notifications endpoints (SendProductUpdate /
/// PreviewProductUpdateRecipients, both on HR.Modules.Notifications). Modeled on
/// PlatformSettingsService/SubscriptionPricingService: "hrapi" HttpClientFactory client, null/empty
/// result on any read/send failure rather than throwing, so the page can show a plain "couldn't be
/// loaded" / "couldn't be sent" message.
/// </summary>
public sealed class ProductUpdateService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ProductUpdateRecipientPreviewModel?> GetRecipientPreviewOrNullAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                "api/notifications/admin/product-updates/recipient-preview", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ProductUpdateRecipientPreviewModel>(
                cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<SendProductUpdateOutcome> SendAsync(
        SendProductUpdateRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                "api/notifications/admin/product-updates", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SendProductUpdateResultModel>(
                    cancellationToken: cancellationToken);
                return new SendProductUpdateOutcome(result, null);
            }

            if ((int)response.StatusCode == 422)
            {
                var body = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(
                    cancellationToken: cancellationToken);
                var errors = body?.Errors?.Values.SelectMany(v => v).ToList();
                if (errors is { Count: > 0 })
                    return new SendProductUpdateOutcome(null, errors);
            }

            return new SendProductUpdateOutcome(
                null, ["Could not send the product update. You may not be authorised to perform this action."]);
        }
        catch (HttpRequestException)
        {
            return new SendProductUpdateOutcome(
                null, ["A network error occurred while sending the product update. Please try again."]);
        }
    }

    private sealed record ValidationErrorEnvelope(Dictionary<string, string[]>? Errors);
}

public sealed record SendProductUpdateOutcome(
    SendProductUpdateResultModel? Result,
    IReadOnlyList<string>? Errors);
