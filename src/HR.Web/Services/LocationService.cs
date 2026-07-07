using HR.Web.Models;

namespace HR.Web.Services;

public class LocationService(IHttpClientFactory httpClientFactory) : IEditService<LocationEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListLocationsResponse?> ListLocationsAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/locations";
            if (includeInactive) url += "?includeInactive=true";
            return await Http.GetFromJsonAsync<ListLocationsResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetLocationResponse?> GetLocationAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetLocationResponse>(
                $"api/companies/{companyId}/locations/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // ── IEditService<LocationEditModel, Guid> ───────────────────────────────────

    async Task<LocationEditModel?> IEditService<LocationEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetLocationAsync(companyId, id);
        return response is null ? null : new LocationEditModel
        {
            Name = response.Name,
            Description = response.Description,
            LocationTypeId = response.LocationTypeId,
        };
    }

    async Task<(LocationEditModel? Result, string? Error)> IEditService<LocationEditModel, Guid>.CreateAsync(
        Guid companyId, LocationEditModel model)
    {
        var request = new CreateLocationRequest(
            companyId,
            model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            model.LocationTypeId);

        var (created, error) = await CreateLocationAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(LocationEditModel? Result, string? Error)> IEditService<LocationEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, LocationEditModel model)
    {
        var request = new UpdateLocationRequest(
            companyId,
            id,
            model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            model.LocationTypeId);

        var (updated, error) = await UpdateLocationAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    public async Task<(CreateLocationResponse? Location, string? Error)> CreateLocationAsync(
        Guid companyId, CreateLocationRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/locations", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateLocationResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A location with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Location type not found.");
        }

        return (null, "Failed to create location.");
    }

    public async Task<(UpdateLocationResponse? Location, string? Error)> UpdateLocationAsync(
        Guid companyId, Guid id, UpdateLocationRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/locations/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateLocationResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A location with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Location not found.");
        }

        return (null, "Failed to update location.");
    }

    public async Task<string?> DeactivateLocationAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/locations/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Location not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate location.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
