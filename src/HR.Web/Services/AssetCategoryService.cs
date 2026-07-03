using HR.Web.Models;

namespace HR.Web.Services;

public class AssetCategoryService(IHttpClientFactory httpClientFactory)
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

    private sealed record ErrorEnvelope(string? Error);
}
