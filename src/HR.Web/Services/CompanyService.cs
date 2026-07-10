using HR.Web.Models;

namespace HR.Web.Services;

public class CompanyService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetCompanySettingsResponse?> GetCompanySettingsAsync(Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanySettingsResponse>($"api/companies/{id}/settings", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetCompanyResponse?> GetCompanyAsync(Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanyResponse>($"api/companies/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(UpdateCompanyResponse? Response, string? Error)> UpdateCompanyAsync(Guid id, UpdateCompanyRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{id}", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<UpdateCompanyResponse>(), null);

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return (null, body?.Error ?? "Failed to save company profile.");
    }

    private sealed record ErrorEnvelope(string? Error);

    public async Task<UpdateCompanySettingsResponse?> UpdateCompanySettingsAsync(Guid id, UpdateCompanySettingsRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{id}/settings", request, HrApiJsonOptions.Default);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UpdateCompanySettingsResponse>(HrApiJsonOptions.Default);
    }

    public async Task<UploadCompanyLogoResponse?> UploadCompanyLogoAsync(
        Guid id, string assetType, Stream fileStream, string fileName, string contentType)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);

        var response = await Http.PostAsync($"api/companies/{id}/branding/logos/{assetType}", content);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UploadCompanyLogoResponse>();
    }
}
