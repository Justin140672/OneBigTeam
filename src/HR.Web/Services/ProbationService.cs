using HR.Web.Models;

namespace HR.Web.Services;

public sealed class ProbationService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ProbationRecordModel?> GetProbationRecordByEmployeeAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ProbationRecordModel>(
                $"api/companies/{companyId}/employees/{employeeId}/probation-record",
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ProbationReviewModel>> GetProbationReviewsAsync(
        Guid companyId,
        Guid probationRecordId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<ProbationReviewsResponse>(
                $"api/companies/{companyId}/probation-records/{probationRecordId}/reviews",
                cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }
}
