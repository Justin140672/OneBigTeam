using HR.Web.Models;

namespace HR.Web.Services;

public class LeaveTypeService(IHttpClientFactory httpClientFactory) : IEditService<LeaveTypeEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListLeaveTypesResponse?> ListLeaveTypesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/leave-types";
            if (!includeInactive) url += "?isActive=true";
            return await Http.GetFromJsonAsync<ListLeaveTypesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
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
        };
    }

    async Task<(LeaveTypeEditModel? Result, string? Error)> IEditService<LeaveTypeEditModel, Guid>.CreateAsync(
        Guid companyId, LeaveTypeEditModel model)
    {
        var request = new CreateLeaveTypeRequest(
            companyId, model.Name.Trim(), model.Code.Trim().ToUpperInvariant(),
            model.DefaultEntitlementDays, model.AccrualMethod, model.Behaviour, model.HasBalance);

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(LeaveTypeEditModel? Result, string? Error)> IEditService<LeaveTypeEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, LeaveTypeEditModel model)
    {
        var request = new UpdateLeaveTypeRequest(
            companyId, id, model.Name.Trim(), model.Code.Trim().ToUpperInvariant(),
            model.DefaultEntitlementDays, model.AccrualMethod, model.Behaviour, model.HasBalance);

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    public async Task<(CreateLeaveTypeResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateLeaveTypeRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/leave-types", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateLeaveTypeResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A leave type with that code already exists.");
        }

        return (null, "Failed to create leave type.");
    }

    public async Task<(UpdateLeaveTypeResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateLeaveTypeRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/leave-types/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateLeaveTypeResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A leave type with that code already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Leave type not found.");

        return (null, "Failed to update leave type.");
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/leave-types/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Leave type not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate leave type.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
