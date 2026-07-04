using HR.Web.Models;

namespace HR.Web.Services;

public class SicknessCategoryService(IHttpClientFactory httpClientFactory) : IEditService<SicknessCategoryEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    // Defaults to true (no filtering) so existing callers that resolve category names for
    // historical records keep seeing deactivated categories. The list page explicitly
    // passes false to filter to active-only by default.
    public async Task<ListSicknessCategoriesResponse?> ListSicknessCategoriesAsync(Guid companyId, bool includeInactive = true)
    {
        try
        {
            var url = $"api/companies/{companyId}/sickness-categories?includeInactive={(includeInactive ? "true" : "false")}";
            var items = await Http.GetFromJsonAsync<List<SicknessCategoryListItemModel>>(url);
            return items is null ? null : new ListSicknessCategoriesResponse(items);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateSicknessCategoryResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateSicknessCategoryRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/sickness-categories", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateSicknessCategoryResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A sickness category with that name already exists.");
        }

        return (null, "Failed to create sickness category.");
    }

    public async Task<(UpdateSicknessCategoryResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateSicknessCategoryRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/sickness-categories/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateSicknessCategoryResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Sickness category not found.");

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A sickness category with that name already exists.");
        }

        return (null, "Failed to update sickness category.");
    }

    public async Task<string?> DeleteAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/sickness-categories/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Sickness category not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to delete sickness category.";
    }

    // No dedicated backend GetById endpoint — the list already returns full item detail.
    async Task<SicknessCategoryEditModel?> IEditService<SicknessCategoryEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var list = await ListSicknessCategoriesAsync(companyId);
        var existing = list?.Items.FirstOrDefault(e => e.Id == id);
        return existing is null ? null : new SicknessCategoryEditModel
        {
            Name = existing.Name,
            DisplayOrder = existing.DisplayOrder,
        };
    }

    async Task<(SicknessCategoryEditModel? Result, string? Error)> IEditService<SicknessCategoryEditModel, Guid>.CreateAsync(
        Guid companyId, SicknessCategoryEditModel model)
    {
        var request = new CreateSicknessCategoryRequest(companyId, model.Name.Trim(), model.DisplayOrder);
        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(SicknessCategoryEditModel? Result, string? Error)> IEditService<SicknessCategoryEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, SicknessCategoryEditModel model)
    {
        var request = new UpdateSicknessCategoryRequest(companyId, id, model.Name.Trim(), model.DisplayOrder);
        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private sealed record ErrorEnvelope(string? Error);
}
