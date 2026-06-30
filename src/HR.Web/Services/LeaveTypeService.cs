using HR.Web.Models;

namespace HR.Web.Services;

public class LeaveTypeService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListLeaveTypesResponse?> ListLeaveTypesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/leave-types";
            if (!includeInactive) url += "?isActive=true";
            return await Http.GetFromJsonAsync<ListLeaveTypesResponse>(url);
        }
        catch (HttpRequestException)
        {
            return null;
        }
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
