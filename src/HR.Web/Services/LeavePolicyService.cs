using HR.Web.Models;

namespace HR.Web.Services;

public class LeavePolicyService(IHttpClientFactory httpClientFactory) : IEditService<LeavePolicyEditModel, Guid>
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

    public async Task<GetLeavePolicyResponse?> GetLeavePolicyAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetLeavePolicyResponse>(
                $"api/companies/{companyId}/leave-policies/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    async Task<LeavePolicyEditModel?> IEditService<LeavePolicyEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetLeavePolicyAsync(companyId, id);
        return response is null ? null : new LeavePolicyEditModel
        {
            Name = response.Name,
            Description = response.Description,
            CarryOverDays = response.CarryOverDays,
            AllowNegativeBalance = response.AllowNegativeBalance,
        };
    }

    async Task<(LeavePolicyEditModel? Result, string? Error)> IEditService<LeavePolicyEditModel, Guid>.CreateAsync(
        Guid companyId, LeavePolicyEditModel model)
    {
        var request = new CreateLeavePolicyRequest(
            companyId, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            model.CarryOverDays, model.AllowNegativeBalance);

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(LeavePolicyEditModel? Result, string? Error)> IEditService<LeavePolicyEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, LeavePolicyEditModel model)
    {
        var request = new UpdateLeavePolicyRequest(
            companyId, id, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            model.CarryOverDays, model.AllowNegativeBalance);

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
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
