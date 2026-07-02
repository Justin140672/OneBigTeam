using HR.Web.Models;

namespace HR.Web.Services;

public class LeavePolicyService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListLeavePoliciesResponse?> ListLeavePoliciesAsync(Guid companyId, bool activeOnly = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/leave-policies";
            if (activeOnly) url += "?activeOnly=true";
            var result = await Http.GetFromJsonAsync<ListLeavePoliciesResponse>(url, HrApiJsonOptions.Default);
            return result;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateLeavePolicyResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateLeavePolicyRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/leave-policies", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateLeavePolicyResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A leave policy with that name already exists.");
        }

        return (null, "Failed to create leave policy.");
    }

    public async Task<(UpdateLeavePolicyResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid policyId, UpdateLeavePolicyRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/leave-policies/{policyId}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateLeavePolicyResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A leave policy with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Leave policy not found.");

        return (null, "Failed to update leave policy.");
    }

    private sealed record ErrorEnvelope(string? Error);
}
