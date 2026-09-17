using System.Net.Http.Json;
using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class OffboardingService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<OffboardingOverviewLookupResult> GetOverviewAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync(
                $"api/companies/{companyId}/employees/{employeeId}/offboarding-overview", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return OffboardingOverviewLookupResult.FailedResult();

            var overview = await response.Content.ReadFromJsonAsync<OffboardingOverviewModel>(HrApiJsonOptions.Default, cancellationToken);
            return OffboardingOverviewLookupResult.SuccessResult(overview);
        }
        catch
        {
            return OffboardingOverviewLookupResult.FailedResult();
        }
    }

    // SPEC-OFF-01 §Templates/obligations: HR-only, mandatory-reason waiver of a mandatory obligation.
    // Endpoint is offboarding-task scoped (not nested under the employee), matching
    // WaiveOffboardingTask/Endpoint.cs's actual route.
    public async Task<(WaiveOffboardingTaskResponse? Result, string? Error)> WaiveTaskAsync(
        Guid companyId, Guid offboardingTaskId, string reason, CancellationToken cancellationToken = default)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/offboarding/tasks/{offboardingTaskId}/waive",
            new WaiveOffboardingTaskRequest(companyId, reason),
            HrApiJsonOptions.Default,
            cancellationToken);
        var result = await ApiResponseReader.ReadJsonAsync<WaiveOffboardingTaskResponse>(response, HrApiJsonOptions.Default, cancellationToken);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to waive obligation."));
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
}
