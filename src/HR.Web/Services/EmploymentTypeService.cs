using HR.Web.Models;

namespace HR.Web.Services;

public class EmploymentTypeService(IHttpClientFactory httpClientFactory) : IEditService<EmploymentTypeEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListEmploymentTypesResponse?> ListEmploymentTypesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/employment-types";
            if (!includeInactive) url += "?isActive=true";
            return await Http.GetFromJsonAsync<ListEmploymentTypesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // No dedicated backend GetById endpoint — the list already returns full item detail.
    async Task<EmploymentTypeEditModel?> IEditService<EmploymentTypeEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var list = await ListEmploymentTypesAsync(companyId, includeInactive: true);
        var existing = list?.Items.FirstOrDefault(e => e.Id == id);
        return existing is null ? null : new EmploymentTypeEditModel
        {
            Name = existing.Name,
            Description = existing.Description,
        };
    }

    async Task<(EmploymentTypeEditModel? Result, string? Error)> IEditService<EmploymentTypeEditModel, Guid>.CreateAsync(
        Guid companyId, EmploymentTypeEditModel model)
    {
        var request = new CreateEmploymentTypeRequest(
            companyId, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim());

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(EmploymentTypeEditModel? Result, string? Error)> IEditService<EmploymentTypeEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, EmploymentTypeEditModel model)
    {
        var request = new UpdateEmploymentTypeRequest(
            companyId, id, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim());

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    public async Task<(CreateEmploymentTypeResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateEmploymentTypeRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/employment-types", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateEmploymentTypeResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An employment type with that name already exists.");
        }

        return (null, "Failed to create employment type.");
    }

    public async Task<(UpdateEmploymentTypeResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateEmploymentTypeRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/employment-types/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateEmploymentTypeResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An employment type with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Employment type not found.");

        return (null, "Failed to update employment type.");
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/employment-types/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Employment type not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate employment type.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
