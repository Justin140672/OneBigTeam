using HR.Web.Models;

namespace HR.Web.Services;

public class CompanyService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetCompanyResponse?> GetCompanyAsync(Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCompanyResponse>($"api/companies/{id}");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<UpdateCompanyResponse?> UpdateCompanyAsync(Guid id, UpdateCompanyRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{id}", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UpdateCompanyResponse>();
    }

    public async Task<UpdateCompanySettingsResponse?> UpdateCompanySettingsAsync(Guid id, UpdateCompanySettingsRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{id}/settings", request);
        var responseContent = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UpdateCompanySettingsResponse>();
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
