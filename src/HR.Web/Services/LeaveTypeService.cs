using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public class LeaveTypeService(HrApiHttpClientFactory httpClientFactory)
    : IEditService<LeaveTypeEditModel, Guid>, IConcurrencyAwareEditService<LeaveTypeEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ListLeaveTypesResponse?> ListLeaveTypesAsync(Guid companyId, bool includeInactive = false)
    {
        var url = $"api/companies/{companyId}/leave-types";
        if (!includeInactive) url += "?isActive=true";
        var result = await ApiResponseReader.ExecuteAsync<ListLeaveTypesResponse>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    // No dedicated backend GetById endpoint — the list already returns full item detail.
    async Task<LeaveTypeEditModel?> IEditService<LeaveTypeEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var list = await ListLeaveTypesAsync(companyId, includeInactive: true);
        var existing = list?.Items.FirstOrDefault(t => t.Id == id);
        return existing is null ? null : new LeaveTypeEditModel
        {
            Name = existing.Name,
            Code = existing.Code,
            DefaultEntitlementDays = existing.DefaultEntitlementDays,
            AccrualMethod = existing.AccrualMethod,
            Behaviour = existing.Behaviour,
            HasBalance = existing.HasBalance,
            IsSystem = existing.IsSystem,
            Version = existing.Version,
        };
    }

    // Ticket 2: concurrency-aware update — sends the loaded version and surfaces the stale-save 409.
    public async Task<ApiSaveResult> UpdateAsync(
        Guid companyId, Guid id, LeaveTypeEditModel model, int? expectedVersion)
    {
        var request = new UpdateLeaveTypeRequest(
            companyId, id, FormText.Required(model.Name), FormText.Required(model.Code).ToUpperInvariant(),
            model.DefaultEntitlementDays, model.AccrualMethod, model.Behaviour, model.HasBalance, expectedVersion);

        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/leave-types/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateLeaveTypeResponse>(response);

        return result.Success
            ? ApiSaveResult.Ok(result.Value?.Version)
            : ApiSaveResult.Fail(result.DisplayMessage ?? "Failed to update leave type.", result.IsConcurrencyConflict);
    }

    async Task<(LeaveTypeEditModel? Result, string? Error)> IEditService<LeaveTypeEditModel, Guid>.CreateAsync(
        Guid companyId, LeaveTypeEditModel model)
    {
        var request = new CreateLeaveTypeRequest(
            companyId, FormText.Required(model.Name), FormText.Required(model.Code).ToUpperInvariant(),
            model.DefaultEntitlementDays, model.AccrualMethod, model.Behaviour, model.HasBalance);

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(LeaveTypeEditModel? Result, string? Error)> IEditService<LeaveTypeEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, LeaveTypeEditModel model)
    {
        var request = new UpdateLeaveTypeRequest(
            companyId, id, FormText.Required(model.Name), FormText.Required(model.Code).ToUpperInvariant(),
            model.DefaultEntitlementDays, model.AccrualMethod, model.Behaviour, model.HasBalance);

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    public async Task<(CreateLeaveTypeResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateLeaveTypeRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/leave-types", request);
        var result = await ApiResponseReader.ReadJsonAsync<CreateLeaveTypeResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to create leave type."));
    }

    public async Task<(UpdateLeaveTypeResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateLeaveTypeRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/leave-types/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateLeaveTypeResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to update leave type."));
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/leave-types/{id}");
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return result.Success ? null : (result.DisplayMessage ?? "Failed to deactivate leave type.");
    }
}
