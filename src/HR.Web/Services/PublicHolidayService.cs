using HR.Web.Models;

namespace HR.Web.Services;

public class PublicHolidayService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListPublicHolidaysResponse?> ListAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListPublicHolidaysResponse>(
                $"api/companies/{companyId}/public-holidays");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreatePublicHolidayResponse? Holiday, string? Error)> CreateAsync(
        Guid companyId, CreatePublicHolidayRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/public-holidays", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreatePublicHolidayResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A public holiday on that date already exists.");
        }

        return (null, "Failed to create public holiday.");
    }

    public async Task<(UpdatePublicHolidayResponse? Holiday, string? Error)> UpdateAsync(
        Guid companyId, Guid id, UpdatePublicHolidayRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/public-holidays/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdatePublicHolidayResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "A public holiday on that date already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Public holiday not found.");

        return (null, "Failed to update public holiday.");
    }

    private sealed record ErrorEnvelope(string? Error);
}
