using HR.Web.Models;

namespace HR.Web.Services;

public class OnboardingTemplateService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListOnboardingTemplatesResponse?> ListOnboardingTemplatesAsync(
        Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/onboarding-templates";
            if (includeInactive) url += "?includeInactive=true";
            return await Http.GetFromJsonAsync<ListOnboardingTemplatesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetOnboardingTemplateResponse?> GetOnboardingTemplateAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetOnboardingTemplateResponse>(
                $"api/companies/{companyId}/onboarding-templates/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateOnboardingTemplateResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateOnboardingTemplateRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/onboarding-templates", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateOnboardingTemplateResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An onboarding template with that name already exists.");
        }

        return (null, "Failed to create onboarding template.");
    }

    public async Task<(UpdateOnboardingTemplateResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateOnboardingTemplateRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/onboarding-templates/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateOnboardingTemplateResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Onboarding template not found.");

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An onboarding template with that name already exists.");
        }

        return (null, "Failed to update onboarding template.");
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/onboarding-templates/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Onboarding template not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate onboarding template.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
