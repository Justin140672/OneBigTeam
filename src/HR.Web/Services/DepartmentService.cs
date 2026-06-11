using HR.Web.Models;

namespace HR.Web.Services;

public class DepartmentService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListDepartmentsResponse?> ListDepartmentsAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/departments";
            if (includeInactive) url += "?includeInactive=true";
            return await Http.GetFromJsonAsync<ListDepartmentsResponse>(url);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateDepartmentResponse? Department, string? Error)> CreateDepartmentAsync(
        Guid companyId, CreateDepartmentRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/departments", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateDepartmentResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A department with that name already exists.");
        }

        return (null, "Failed to create department.");
    }

    public async Task<(UpdateDepartmentResponse? Department, string? Error)> UpdateDepartmentAsync(
        Guid companyId, Guid id, UpdateDepartmentRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/departments/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateDepartmentResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A department with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Department not found.");

        return (null, "Failed to update department.");
    }

    public async Task<string?> DeactivateDepartmentAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/departments/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Department not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate department.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
