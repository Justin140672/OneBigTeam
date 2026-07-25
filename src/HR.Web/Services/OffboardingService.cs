using System.Net.Http.Json;
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
}
