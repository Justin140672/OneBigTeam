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

        return (null, await ExtractErrorMessageAsync(response));
    }

    // FastEndpoints' own automatic request-validation failures (FluentValidation rules that
    // fail before the handler even runs — e.g. UpdateCompanyValidator) return a different shape
    // than this app's handler-level business errors ({ "error": "..." }): a dictionary of field
    // name -> messages, e.g. { "errors": { "addresses[0].line1": ["'Line1' must not be empty."] } }.
    // Falling back to a generic "Failed to..." message when THIS shape comes back hides real,
    // actionable per-field validation failures from the user — read whichever shape the response
    // actually has instead.
    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        if (body?.Errors is { Count: > 0 })
            return string.Join(" ", body.Errors.SelectMany(kvp => kvp.Value));

        return body?.Error ?? "Failed to save company profile.";
    }

    private sealed record ErrorEnvelope(string? Error, Dictionary<string, string[]>? Errors);

    public async Task<UpdateCompanySettingsResponse?> UpdateCompanySettingsAsync(Guid id, UpdateCompanySettingsRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{id}/settings", request, HrApiJsonOptions.Default);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UpdateCompanySettingsResponse>(HrApiJsonOptions.Default);
    }

    public async Task<GetHrSettingsResponse?> GetHrSettingsAsync(Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetHrSettingsResponse>($"api/companies/{id}/hr-settings", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<UpdateHrSettingsResponse?> UpdateHrSettingsAsync(Guid id, UpdateHrSettingsRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{id}/hr-settings", request, HrApiJsonOptions.Default);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<UpdateHrSettingsResponse>(HrApiJsonOptions.Default);
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
