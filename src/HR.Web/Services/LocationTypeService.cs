using HR.Web.Models;

namespace HR.Web.Services;

public class LocationTypeService(IHttpClientFactory httpClientFactory) : IEditService<LocationTypeEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListLocationTypesResponse?> ListLocationTypesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/location-types";
            if (!includeInactive) url += "?isActive=true";
            return await Http.GetFromJsonAsync<ListLocationTypesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // No dedicated backend GetById endpoint — the list already returns full item detail.
    async Task<LocationTypeEditModel?> IEditService<LocationTypeEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var list = await ListLocationTypesAsync(companyId, includeInactive: true);
        var existing = list?.Items.FirstOrDefault(e => e.Id == id);
        return existing is null ? null : new LocationTypeEditModel
        {
            Name = existing.Name,
            Description = existing.Description,
        };
    }

    async Task<(LocationTypeEditModel? Result, string? Error)> IEditService<LocationTypeEditModel, Guid>.CreateAsync(
        Guid companyId, LocationTypeEditModel model)
    {
        var request = new CreateLocationTypeRequest(
            companyId, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim());

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(LocationTypeEditModel? Result, string? Error)> IEditService<LocationTypeEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, LocationTypeEditModel model)
    {
        var request = new UpdateLocationTypeRequest(
            companyId, id, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim());

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    public async Task<(CreateLocationTypeResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateLocationTypeRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/location-types", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateLocationTypeResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A location type with that name already exists.");
        }

        return (null, "Failed to create location type.");
    }

    public async Task<(UpdateLocationTypeResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateLocationTypeRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/location-types/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateLocationTypeResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A location type with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Location type not found.");

        return (null, "Failed to update location type.");
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/location-types/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Location type not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate location type.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
