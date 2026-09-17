using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public class LeavePolicyService(HrApiHttpClientFactory httpClientFactory)
    : IEditService<LeavePolicyEditModel, Guid>, IConcurrencyAwareEditService<LeavePolicyEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ListLeavePoliciesResponse?> ListLeavePoliciesAsync(Guid companyId, bool activeOnly = false)
    {
        var url = $"api/companies/{companyId}/leave-policies";
        if (activeOnly) url += "?activeOnly=true";
        var result = await ApiResponseReader.ExecuteAsync<ListLeavePoliciesResponse>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<GetLeavePolicyResponse?> GetLeavePolicyAsync(Guid companyId, Guid id)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetLeavePolicyResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/leave-policies/{id}", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
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
            IsDefault = response.IsDefault,
            Version = response.Version,
        };
    }

    // Ticket 2: concurrency-aware update — sends the loaded version and surfaces the stale-save 409.
    public async Task<ApiSaveResult> UpdateAsync(
        Guid companyId, Guid id, LeavePolicyEditModel model, int? expectedVersion)
    {
        var request = new UpdateLeavePolicyRequest(
            companyId, id, FormText.Required(model.Name),
            FormText.Optional(model.Description),
            model.CarryOverDays, model.AllowNegativeBalance, model.IsDefault, expectedVersion);

        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/leave-policies/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateLeavePolicyResponse>(response);

        return result.Success
            ? ApiSaveResult.Ok(result.Value?.Version)
            : ApiSaveResult.Fail(result.DisplayMessage ?? "Failed to update leave policy.", result.IsConcurrencyConflict);
    }

    async Task<(LeavePolicyEditModel? Result, string? Error)> IEditService<LeavePolicyEditModel, Guid>.CreateAsync(
        Guid companyId, LeavePolicyEditModel model)
    {
        var request = new CreateLeavePolicyRequest(
            companyId, FormText.Required(model.Name), FormText.Optional(model.Description),
            model.CarryOverDays, model.AllowNegativeBalance, model.IsDefault);

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(LeavePolicyEditModel? Result, string? Error)> IEditService<LeavePolicyEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, LeavePolicyEditModel model)
    {
        var request = new UpdateLeavePolicyRequest(
            companyId, id, FormText.Required(model.Name), FormText.Optional(model.Description),
            model.CarryOverDays, model.AllowNegativeBalance, model.IsDefault);

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    public async Task<(CreateLeavePolicyResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateLeavePolicyRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/leave-policies", request);
        var result = await ApiResponseReader.ReadJsonAsync<CreateLeavePolicyResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to create leave policy."));
    }

    public async Task<(UpdateLeavePolicyResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid policyId, UpdateLeavePolicyRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/leave-policies/{policyId}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateLeavePolicyResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to update leave policy."));
    }

    public async Task<string?> SetDefaultLeavePolicyAsync(Guid companyId, Guid id)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/leave-policies/{id}/set-default", new { });
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return result.Success ? null : (result.DisplayMessage ?? "Failed to set default leave policy.");
    }
}
