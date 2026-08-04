using HR.Web.Models;

namespace HR.Web.Services;

public class CompanyOnboardingService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetCompanyOnboardingChecklistResponse?> GetChecklistAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanyOnboardingChecklistResponse>(
                "api/company-onboarding/checklist", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<DismissCompanyOnboardingChecklistResponse?> DismissChecklistAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(
                "api/company-onboarding/checklist/dismiss", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<DismissCompanyOnboardingChecklistResponse>(
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetCompanyOnboardingExploreCardsResponse?> GetExploreCardsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanyOnboardingExploreCardsResponse>(
                "api/company-onboarding/explore-cards", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
