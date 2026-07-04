using HR.Web.Models;

namespace HR.Web.Services;

public class DepartmentService(IHttpClientFactory httpClientFactory) : IEditService<DepartmentEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListDepartmentsResponse?> ListDepartmentsAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/departments";
            if (includeInactive) url += "?includeInactive=true";
            return await Http.GetFromJsonAsync<ListDepartmentsResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetDepartmentResponse?> GetDepartmentAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetDepartmentResponse>(
                $"api/companies/{companyId}/departments/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // ── IEditService<DepartmentEditModel, Guid> ─────────────────────────────────

    async Task<DepartmentEditModel?> IEditService<DepartmentEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetDepartmentAsync(companyId, id);
        return response is null ? null : new DepartmentEditModel
        {
            Name = response.Name,
            Description = response.Description,
            ParentDepartmentId = response.ParentDepartmentId,
            ManagerEmployeeId = response.ManagerEmployeeId,
        };
    }

    async Task<(DepartmentEditModel? Result, string? Error)> IEditService<DepartmentEditModel, Guid>.CreateAsync(
        Guid companyId, DepartmentEditModel model)
    {
        var request = new CreateDepartmentRequest(
            companyId,
            model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            model.ParentDepartmentId);

        var (created, error) = await CreateDepartmentAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(DepartmentEditModel? Result, string? Error)> IEditService<DepartmentEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, DepartmentEditModel model)
    {
        var request = new UpdateDepartmentRequest(
            companyId,
            id,
            model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            model.ParentDepartmentId,
            model.ManagerEmployeeId);

        var (updated, error) = await UpdateDepartmentAsync(companyId, id, request);
        return (updated is null ? null : model, error);
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
