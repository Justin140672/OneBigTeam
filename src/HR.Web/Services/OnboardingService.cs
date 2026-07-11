using HR.Web.Models;

namespace HR.Web.Services;

public sealed class OnboardingService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<OnboardingOverviewModel?> GetOverviewAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<OnboardingOverviewModel>(
                $"api/companies/{companyId}/employees/{employeeId}/onboarding-overview", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TeamOnboardingListModel?> GetTeamOnboardingAsync(Guid companyId, Guid managerId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<TeamOnboardingListModel>(
                $"api/companies/{companyId}/employees/{managerId}/team-onboarding", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
