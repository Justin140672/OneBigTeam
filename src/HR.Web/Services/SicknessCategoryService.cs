using HR.Web.Models;

namespace HR.Web.Services;

public class SicknessCategoryService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListSicknessCategoriesResponse?> ListSicknessCategoriesAsync(Guid companyId)
    {
        try
        {
            var items = await Http.GetFromJsonAsync<List<SicknessCategoryListItemModel>>(
                $"api/companies/{companyId}/sickness-categories");
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

    private sealed record ErrorEnvelope(string? Error);
}
