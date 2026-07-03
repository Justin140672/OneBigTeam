using HR.Web.Models;

namespace HR.Web.Services;

public sealed class SicknessService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ReturnToWorkReviewDetailModel?> GetReturnToWorkReviewAsync(
        Guid companyId,
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ReturnToWorkReviewDetailModel>(
                $"api/companies/{companyId}/return-to-work-reviews/{reviewId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
