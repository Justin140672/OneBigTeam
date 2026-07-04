using HR.Web.Models;

namespace HR.Web.Services;

public class PublicHolidayService(IHttpClientFactory httpClientFactory) : IEditService<PublicHolidayEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListPublicHolidaysResponse?> ListAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListPublicHolidaysResponse>(
                $"api/companies/{companyId}/public-holidays", HrApiJsonOptions.Default);
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

    // No dedicated backend GetById endpoint — the list already returns full item detail.
    async Task<PublicHolidayEditModel?> IEditService<PublicHolidayEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var list = await ListAsync(companyId);
        var existing = list?.Items.FirstOrDefault(h => h.Id == id);
        return existing is null ? null : new PublicHolidayEditModel
        {
            Date = existing.Date.ToDateTime(TimeOnly.MinValue),
            Name = existing.Name,
            CountryCode = existing.CountryCode,
        };
    }

    async Task<(PublicHolidayEditModel? Result, string? Error)> IEditService<PublicHolidayEditModel, Guid>.CreateAsync(
        Guid companyId, PublicHolidayEditModel model)
    {
        var request = new CreatePublicHolidayRequest(
            companyId, DateOnly.FromDateTime(model.Date!.Value), model.Name.Trim(), model.CountryCode.Trim().ToUpperInvariant());

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(PublicHolidayEditModel? Result, string? Error)> IEditService<PublicHolidayEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, PublicHolidayEditModel model)
    {
        var request = new UpdatePublicHolidayRequest(
            companyId, id, DateOnly.FromDateTime(model.Date!.Value), model.Name.Trim(), model.CountryCode.Trim().ToUpperInvariant());

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private sealed record ErrorEnvelope(string? Error);
}
