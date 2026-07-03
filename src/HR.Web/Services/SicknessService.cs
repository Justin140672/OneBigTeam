using System.Net.Http.Json;
using System.Text.Json;
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

    public async Task<ListEmployeeSicknessRecordsResponseModel?> ListEmployeeSicknessRecordsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListEmployeeSicknessRecordsResponseModel>(
                $"api/companies/{companyId}/employees/{employeeId}/sickness-records", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Success, string? Error)> RecordSicknessAsync(
        Guid companyId,
        Guid employeeId,
        RecordSicknessRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/sickness-records",
                request, HrApiJsonOptions.Default, cancellationToken);

            if (response.IsSuccessStatusCode)
                return (true, null);

            return (false, await ExtractErrorAsync(response, "Failed to record sickness.", cancellationToken));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> CloseSicknessRecordAsync(
        Guid companyId,
        Guid employeeId,
        Guid recordId,
        CloseSicknessRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
                request, HrApiJsonOptions.Default, cancellationToken);

            if (response.IsSuccessStatusCode)
                return (true, null);

            return (false, await ExtractErrorAsync(response, "Failed to close sickness record.", cancellationToken));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<string> ExtractErrorAsync(
        HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            if (body.TryGetProperty("error", out var errorProp))
                return errorProp.GetString() ?? fallback;
        }
        catch { }

        return $"{fallback} ({(int)response.StatusCode})";
    }
}
