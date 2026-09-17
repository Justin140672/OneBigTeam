using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public class AssetCategoryService(HrApiHttpClientFactory httpClientFactory)
    : IEditService<AssetCategoryEditModel, Guid>, IConcurrencyAwareEditService<AssetCategoryEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ListAssetCategoriesResponse?> ListAssetCategoriesAsync(Guid companyId, bool includeInactive = false)
    {
        var url = $"api/companies/{companyId}/asset-categories";
        if (includeInactive) url += "?includeInactive=true";

        var result = await ApiResponseReader.ExecuteAsync<List<AssetCategoryListItemModel>>(
            ct => Http.GetAsync(url, ct));

        return result.Success ? new ListAssetCategoriesResponse(result.Value ?? []) : null;
    }

    public async Task<(CreateAssetCategoryResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateAssetCategoryRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/asset-categories", request);
        var result = await ApiResponseReader.ReadJsonAsync<CreateAssetCategoryResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to create asset category."));
    }

    public async Task<(UpdateAssetCategoryResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateAssetCategoryRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/asset-categories/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateAssetCategoryResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to update asset category."));
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/asset-categories/{id}");
        var result = await ApiResponseReader.ReadNoContentAsync(response);
        return result.Success ? null : (result.DisplayMessage ?? "Failed to deactivate asset category.");
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
            Version = existing.Version,
        };
    }

    // Ticket 2: concurrency-aware update — sends the loaded version and surfaces the stale-save 409.
    public async Task<ApiSaveResult> UpdateAsync(
        Guid companyId, Guid id, AssetCategoryEditModel model, int? expectedVersion)
    {
        var request = new UpdateAssetCategoryRequest(
            companyId, id, FormText.Required(model.Name),
            FormText.Optional(model.Description),
            expectedVersion);

        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/asset-categories/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateAssetCategoryResponse>(response);

        return result.Success
            ? ApiSaveResult.Ok(result.Value?.Version)
            : ApiSaveResult.Fail(result.DisplayMessage ?? "Failed to update asset category.", result.IsConcurrencyConflict);
    }

    async Task<(AssetCategoryEditModel? Result, string? Error)> IEditService<AssetCategoryEditModel, Guid>.CreateAsync(
        Guid companyId, AssetCategoryEditModel model)
    {
        var request = new CreateAssetCategoryRequest(
            companyId, FormText.Required(model.Name), FormText.Optional(model.Description));

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(AssetCategoryEditModel? Result, string? Error)> IEditService<AssetCategoryEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, AssetCategoryEditModel model)
    {
        var request = new UpdateAssetCategoryRequest(
            companyId, id, FormText.Required(model.Name), FormText.Optional(model.Description));

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }
}
