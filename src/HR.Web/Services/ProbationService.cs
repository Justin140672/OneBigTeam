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
                $"api/companies/{companyId}/employees/{employeeId}/probation-record", HrApiJsonOptions.Default, cancellationToken);
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

    public async Task<MyProbationStatusModel?> GetMyProbationStatusAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<MyProbationStatusModel>(
                $"api/companies/{companyId}/employees/me/probation-status", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProbationStatusModel?> GetStatusAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ProbationStatusModel>(
                $"api/companies/{companyId}/employees/{employeeId}/probation-status", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProbationReviewDetailModel?> GetProbationReviewAsync(
        Guid companyId,
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ProbationReviewDetailModel>(
                $"api/companies/{companyId}/probation-reviews/{reviewId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CompleteReviewAsync(
        Guid companyId,
        Guid probationRecordId,
        Guid reviewId,
        Guid completedByEmployeeId,
        string? notes,
        string? outcome = null,
        DateOnly? decisionDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/probation-records/{probationRecordId}/reviews/{reviewId}/complete",
                new { CompletedByEmployeeId = completedByEmployeeId, Notes = notes, Outcome = outcome, DecisionDate = decisionDate },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<UpcomingProbationReviewItem>> GetUpcomingReviewsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<UpcomingProbationReviewsResponse>(
                $"api/companies/{companyId}/probation-reviews/upcoming", HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
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
                $"api/companies/{companyId}/probation-records/{probationRecordId}/reviews", HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }
}
