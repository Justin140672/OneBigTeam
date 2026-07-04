using HR.Web.Models;

namespace HR.Web.Services;

public class AssetCategoryService(IHttpClientFactory httpClientFactory) : IEditService<AssetCategoryEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListAssetCategoriesResponse?> ListAssetCategoriesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/asset-categories";
            if (includeInactive) url += "?includeInactive=true";
            var items = await Http.GetFromJsonAsync<List<AssetCategoryListItemModel>>(url);
            return items is null ? null : new ListAssetCategoriesResponse(items);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateAssetCategoryResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateAssetCategoryRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/asset-categories", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateAssetCategoryResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An asset category with that name already exists.");
        }

        return (null, "Failed to create asset category.");
    }

    public async Task<(UpdateAssetCategoryResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateAssetCategoryRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/asset-categories/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateAssetCategoryResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Asset category not found.");

        return (null, "Failed to update asset category.");
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/asset-categories/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Asset category not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate asset category.";
    }

    // No dedicated backend GetById endpoint — the list already returns full item detail.
    async Task<AssetCategoryEditModel?> IEditService<AssetCategoryEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var list = await ListAssetCategoriesAsync(companyId, includeInactive: true);
        var existing = list?.Items.FirstOrDefault(e => e.Id == id);
        return existing is null ? null : new AssetCategoryEditModel
        {
            Name = existing.Name,
            Description = existing.Description,
        };
    }

    async Task<(AssetCategoryEditModel? Result, string? Error)> IEditService<AssetCategoryEditModel, Guid>.CreateAsync(
        Guid companyId, AssetCategoryEditModel model)
    {
        var request = new CreateAssetCategoryRequest(
            companyId, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim());

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(AssetCategoryEditModel? Result, string? Error)> IEditService<AssetCategoryEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, AssetCategoryEditModel model)
    {
        var request = new UpdateAssetCategoryRequest(
            companyId, id, model.Name.Trim(), string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim());

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private sealed record ErrorEnvelope(string? Error);
}
