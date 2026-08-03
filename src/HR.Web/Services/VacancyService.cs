using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class VacancyService(IHttpClientFactory httpClientFactory) : IEditService<VacancyEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListVacanciesResponse?> ListVacanciesAsync(
        Guid companyId, string? status = null, Guid? positionProfileId = null, Guid? departmentId = null)
    {
        try
        {
            var url = $"api/companies/{companyId}/vacancies";
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={status}");
            if (positionProfileId is not null) query.Add($"positionProfileId={positionProfileId}");
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

    public async Task<(PublishVacancyResponse? Result, string? Error)> PublishVacancyAsync(Guid companyId, Guid id, DateOnly? openedAt = null)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{id}/publish",
            new PublishVacancyRequest(companyId, id, openedAt));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<PublishVacancyResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to publish vacancy."));
    }

    // ── IEditService<VacancyEditModel, Guid> ────────────────────────────────────

    async Task<VacancyEditModel?> IEditService<VacancyEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetVacancyAsync(companyId, id);
        return response is null ? null : new VacancyEditModel
        {
            AdvertTitle = response.AdvertTitle,
            AdvertDescription = response.AdvertDescription,
            PositionProfileId = response.PositionProfileId,
            HiringManagerId = response.HiringManagerId,
            AssignedRecruiterId = response.AssignedRecruiterId ?? Guid.Empty,
        };
    }

    async Task<(VacancyEditModel? Result, string? Error)> IEditService<VacancyEditModel, Guid>.CreateAsync(Guid companyId, VacancyEditModel model)
    {
        var request = new CreateVacancyRequest(
            companyId, model.PositionProfileId!.Value,
            string.IsNullOrWhiteSpace(model.AdvertTitle) ? null : model.AdvertTitle.Trim(),
            string.IsNullOrWhiteSpace(model.AdvertDescription) ? null : model.AdvertDescription.Trim(),
            model.HiringManagerId!.Value,
            model.AssignedRecruiterId == Guid.Empty ? null : model.AssignedRecruiterId);

        var (created, error) = await CreateVacancyAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(VacancyEditModel? Result, string? Error)> IEditService<VacancyEditModel, Guid>.UpdateAsync(Guid companyId, Guid id, VacancyEditModel model)
    {
        var request = new UpdateVacancyRequest(
            companyId, id,
            model.PositionProfileId,
            string.IsNullOrWhiteSpace(model.AdvertTitle) ? null : model.AdvertTitle.Trim(),
            string.IsNullOrWhiteSpace(model.AdvertDescription) ? null : model.AdvertDescription.Trim(),
            model.HiringManagerId!.Value,
            AssignedRecruiterId: model.AssignedRecruiterId == Guid.Empty ? null : model.AssignedRecruiterId,
            IsAuthorisedCorrection: model.IsAuthorisedCorrection,
            CorrectionReason: string.IsNullOrWhiteSpace(model.CorrectionReason) ? null : model.CorrectionReason.Trim());

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
