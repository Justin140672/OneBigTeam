using HR.Web.Models;

namespace HR.Web.Services;

public sealed class PromotionService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<IReadOnlyList<EmployeePromotionHistoryItemModel>> GetPromotionHistoryAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<GetEmployeePromotionHistoryResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/promotions",
                HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    // RequiresBackdateConfirmation is true only for the specific 409 raised when EffectiveDate is
    // in the past and the caller didn't already set ConfirmBackdatedEffectiveDate — the dialog uses
    // this to prompt the user and let them explicitly resubmit with that flag set, rather than
    // silently retrying on their behalf.
    public async Task<(PromoteEmployeeResponse? Result, string? Error, bool RequiresBackdateConfirmation)> PromoteEmployeeAsync(
        Guid companyId, Guid employeeId, PromoteEmployeeRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/promotions", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<PromoteEmployeeResponse>(HrApiJsonOptions.Default);
            return (created, null, false);
        }

        var raw = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var businessMessage = TryDeserialize<ErrorEnvelope>(raw)?.Error;
            return (null, businessMessage ?? "This effective date is in the past. Confirm to backdate the promotion.", true);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, TryDeserialize<ErrorEnvelope>(raw)?.Error ?? "Employee not found.", false);

        if (TryDeserialize<ErrorEnvelope>(raw)?.Error is { } message)
            return (null, message, false);

        if (TryDeserialize<ValidationErrorResponse>(raw)?.Errors is { Count: > 0 } fieldErrors)
            return (null, string.Join(" ", fieldErrors.Values.SelectMany(m => m)), false);

        return (null, $"Failed to promote employee ({(int)response.StatusCode} {response.StatusCode}).", false);
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(json, HrApiJsonOptions.Default); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private sealed record ErrorEnvelope(string? Error);
    private sealed record ValidationErrorResponse(Dictionary<string, List<string>>? Errors);
}
