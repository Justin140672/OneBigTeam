using System.Net;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class EmployeeNumberBackfillService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<(PreviewBackfillEmployeeNumbersResponse? Result, string? Error)> PreviewAsync(Guid companyId)
    {
        var response = await Http.GetAsync($"api/companies/{companyId}/employees/backfill-employee-numbers/preview");

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<PreviewBackfillEmployeeNumbersResponse>(
                HrApiJsonOptions.Default);
            return (result, null);
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Unable to preview the employee number backfill.");
        }

        return (null, "Unable to preview the employee number backfill.");
    }

    public async Task<(CommitBackfillEmployeeNumbersResponse? Result, string? Error)> CommitAsync(Guid companyId)
    {
        var response = await Http.PostAsync(
            $"api/companies/{companyId}/employees/backfill-employee-numbers/commit", null);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<CommitBackfillEmployeeNumbersResponse>(
                HrApiJsonOptions.Default);
            return (result, null);
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Unable to complete the employee number backfill.");
        }

        return (null, "Unable to complete the employee number backfill.");
    }

    private sealed record ErrorEnvelope(string? Error);
}
