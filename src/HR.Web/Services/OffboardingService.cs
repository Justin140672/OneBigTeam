using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class OffboardingService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<OffboardingOverviewModel?> GetOverviewAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<OffboardingOverviewModel>(
                $"api/companies/{companyId}/employees/{employeeId}/offboarding-overview", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<OffboardingStatusModel?> GetStatusAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<OffboardingStatusModel>(
                $"api/companies/{companyId}/employees/{employeeId}/offboarding-status", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    // Returns null on success, or an error message string on failure.
    public async Task<string?> StartOffboardingAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { lastWorkingDay = lastWorkingDay.ToString("yyyy-MM-dd"), notes };
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/offboarding/start",
                body, cancellationToken);

            if (response.IsSuccessStatusCode)
                return null;

            try
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                if (json.TryGetProperty("error", out var errorProp))
                    return errorProp.GetString();
            }
            catch { }

            return $"Failed to start offboarding ({(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
