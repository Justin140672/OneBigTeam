using HR.Admin.Web.Models;
using HR.SharedKernel.Http;

namespace HR.Admin.Web.Services;

public sealed class ProductUpdateService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ProductUpdateRecipientPreviewModel?> GetRecipientPreviewOrNullAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<ProductUpdateRecipientPreviewModel>(
            ct => Http.GetAsync("api/notifications/admin/product-updates/recipient-preview", ct),
            cancellationToken: cancellationToken);

        return result.Value;
    }

    public async Task<SendProductUpdateOutcome> SendAsync(
        SendProductUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<SendProductUpdateResultModel>(
            ct => Http.PostAsJsonAsync("api/notifications/admin/product-updates", request, ct),
            cancellationToken: cancellationToken);

        if (result.Success)
            return new SendProductUpdateOutcome(result.Value, null);

        if (result.ValidationErrors is { Count: > 0 } errors)
            return new SendProductUpdateOutcome(null, errors.Values.SelectMany(v => v).ToList());

        return new SendProductUpdateOutcome(
            null,
            [result.Error ?? "Could not send the product update. You may not be authorised to perform this action."]);
    }
}

public sealed record SendProductUpdateOutcome(
    SendProductUpdateResultModel? Result,
    IReadOnlyList<string>? Errors);
