using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class VacancyService(IHttpClientFactory httpClientFactory) : IEditService<VacancyEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListVacanciesResponse?> ListVacanciesAsync(Guid companyId, string? status = null, Guid? departmentId = null)
    {
        try
        {
            var url = $"api/companies/{companyId}/vacancies";
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={status}");
            if (departmentId is not null) query.Add($"departmentId={departmentId}");
            if (query.Count > 0) url += "?" + string.Join("&", query);

            return await Http.GetFromJsonAsync<ListVacanciesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetStaleVacanciesResponse?> GetStaleVacanciesAsync(
        Guid companyId, int? staleAfterDays = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"api/companies/{companyId}/vacancies/stale";
            if (staleAfterDays is not null) url += $"?staleAfterDays={staleAfterDays}";

            return await Http.GetFromJsonAsync<GetStaleVacanciesResponse>(url, HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetPipelineSummaryResponse?> GetPipelineSummaryAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetPipelineSummaryResponse>(
                $"api/companies/{companyId}/recruitment/pipeline-summary", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetVacancyResponse?> GetVacancyAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetVacancyResponse>(
                $"api/companies/{companyId}/vacancies/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateVacancyResponse? Result, string? Error)> CreateVacancyAsync(Guid companyId, CreateVacancyRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/vacancies", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CreateVacancyResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to create vacancy."));
    }

    public async Task<(UpdateVacancyResponse? Result, string? Error)> UpdateVacancyAsync(Guid companyId, Guid id, UpdateVacancyRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/vacancies/{id}", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<UpdateVacancyResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to update vacancy."));
    }

    public async Task<(CloseVacancyResponse? Result, string? Error)> CloseVacancyAsync(Guid companyId, Guid id, DateOnly? closedAt = null)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{id}/close",
            new CloseVacancyRequest(companyId, id, closedAt));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CloseVacancyResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to close vacancy."));
    }

    // ── IEditService<VacancyEditModel, Guid> ────────────────────────────────────

    async Task<VacancyEditModel?> IEditService<VacancyEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetVacancyAsync(companyId, id);
        return response is null ? null : new VacancyEditModel
        {
            Title = response.Title,
            Description = response.Description,
            Location = response.Location,
            DepartmentId = response.DepartmentId,
            HiringManagerId = response.HiringManagerId,
        };
    }

    async Task<(VacancyEditModel? Result, string? Error)> IEditService<VacancyEditModel, Guid>.CreateAsync(Guid companyId, VacancyEditModel model)
    {
        var request = new CreateVacancyRequest(
            companyId, model.DepartmentId, model.Title.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            string.IsNullOrWhiteSpace(model.Location) ? null : model.Location.Trim(),
            model.HiringManagerId!.Value);

        var (created, error) = await CreateVacancyAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(VacancyEditModel? Result, string? Error)> IEditService<VacancyEditModel, Guid>.UpdateAsync(Guid companyId, Guid id, VacancyEditModel model)
    {
        var request = new UpdateVacancyRequest(
            companyId, id, model.DepartmentId, model.Title.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            string.IsNullOrWhiteSpace(model.Location) ? null : model.Location.Trim(),
            model.HiringManagerId!.Value);

        var (updated, error) = await UpdateVacancyAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        if (response.StatusCode is HttpStatusCode.NotFound)
            return "Vacancy not found.";

        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return body?.Error ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record ErrorEnvelope(string? Error);
}
