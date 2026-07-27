using System.Net;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class EmployeeTimelineService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<(TimelineBackfillResponse? Result, string? Error)> CommitBackfillAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var response = await Http.PostAsync(
            $"api/companies/{companyId}/employees/timeline/backfill", null, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<TimelineBackfillResponse>(
                HrApiJsonOptions.Default, cancellationToken);
            return (result, null);
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken: cancellationToken);
            return (null, body?.Error ?? "Unable to complete the employee timeline backfill.");
        }

        return (null, "Unable to complete the employee timeline backfill.");
    }

    private sealed record ErrorEnvelope(string? Error);

    public async Task<GetEmployeeTimelineResponse?> GetTimelineAsync(
        Guid companyId,
        Guid employeeId,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/companies/{companyId}/employees/{employeeId}/timeline?pageNumber={pageNumber}&pageSize={pageSize}";

        try
        {
            return await Http.GetFromJsonAsync<GetEmployeeTimelineResponse>(
                url, HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
