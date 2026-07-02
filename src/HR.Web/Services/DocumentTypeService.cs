using HR.Web.Models;

namespace HR.Web.Services;

public class DocumentTypeService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListDocumentTypesAdminResponse?> ListDocumentTypesAsync(Guid companyId, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/document-types";
            if (includeInactive) url += "?includeInactive=true";
            var result = await Http.GetFromJsonAsync<ListDocumentTypesAdminResponse>(url, HrApiJsonOptions.Default);
            return result;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateDocumentTypeResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateDocumentTypeRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/document-types", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateDocumentTypeResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A document type with that name already exists.");
        }

        return (null, "Failed to create document type.");
    }

    public async Task<(UpdateDocumentTypeResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdateDocumentTypeRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/document-types/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateDocumentTypeResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A document type with that name already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Document type not found.");

        return (null, "Failed to update document type.");
    }

    public async Task<string?> DeactivateAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/document-types/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Document type not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to deactivate document type.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
