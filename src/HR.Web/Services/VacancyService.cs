using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class VacancyService(HrApiHttpClientFactory httpClientFactory)
    : IEditService<VacancyEditModel, Guid>, IConcurrencyAwareEditService<VacancyEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ListVacanciesResponse?> ListVacanciesAsync(
        Guid companyId,
        string? status = null,
        Guid? positionProfileId = null,
        Guid? departmentId = null,
        bool excludeClosed = false,
        string? search = null,
        int? pageSize = null)
    {
        var url = $"api/companies/{companyId}/vacancies";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={status}");
        if (positionProfileId is not null) query.Add($"positionProfileId={positionProfileId}");
        if (departmentId is not null) query.Add($"departmentId={departmentId}");
        if (excludeClosed) query.Add("excludeClosed=true");
        search = FormText.OptionalSearch(search);
        if (search is not null) query.Add($"search={Uri.EscapeDataString(search)}");
        if (pageSize is > 0) query.Add($"pageSize={pageSize.Value}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var result = await ApiResponseReader.ExecuteAsync<ListVacanciesResponse>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<GetStaleVacanciesResponse?> GetStaleVacanciesAsync(
        Guid companyId, int? staleAfterDays = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/companies/{companyId}/vacancies/stale";
        if (staleAfterDays is not null) url += $"?staleAfterDays={staleAfterDays}";

        var result = await ApiResponseReader.ExecuteAsync<GetStaleVacanciesResponse>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<GetPipelineSummaryResponse?> GetPipelineSummaryAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetPipelineSummaryResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/recruitment/pipeline-summary", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    // ── DSH-03 non-swallowing siblings ──────────────────────────────────────
    public Task<ListVacanciesResponse?> ListVacanciesOrThrowAsync(
        Guid companyId, string? status = null, Guid? positionProfileId = null, Guid? departmentId = null,
        bool excludeClosed = false, string? search = null, int? pageSize = null)
    {
        var url = $"api/companies/{companyId}/vacancies";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={status}");
        if (positionProfileId is not null) query.Add($"positionProfileId={positionProfileId}");
        if (departmentId is not null) query.Add($"departmentId={departmentId}");
        if (excludeClosed) query.Add("excludeClosed=true");
        search = FormText.OptionalSearch(search);
        if (search is not null) query.Add($"search={Uri.EscapeDataString(search)}");
        if (pageSize is > 0) query.Add($"pageSize={pageSize.Value}");
        if (query.Count > 0) url += "?" + string.Join("&", query);
        return Http.GetFromJsonAsync<ListVacanciesResponse>(url, HrApiJsonOptions.Default);
    }

    public Task<GetStaleVacanciesResponse?> GetStaleVacanciesOrThrowAsync(
        Guid companyId, int? staleAfterDays = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/companies/{companyId}/vacancies/stale";
        if (staleAfterDays is not null) url += $"?staleAfterDays={staleAfterDays}";
        return Http.GetFromJsonAsync<GetStaleVacanciesResponse>(url, HrApiJsonOptions.Default, cancellationToken);
    }

    public Task<GetPipelineSummaryResponse?> GetPipelineSummaryOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<GetPipelineSummaryResponse>(
            $"api/companies/{companyId}/recruitment/pipeline-summary", HrApiJsonOptions.Default, cancellationToken);

    // ── DSH-04 authoritative recruitment dashboard metrics ──────────────────
    // Non-swallowing (DSH-03 style): let HttpRequestException surface so WidgetSourceLoader records
    // a failed source rather than a misleading 0.
    public Task<NewApplicationsMetricResponse?> GetNewApplicationsMetricOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<NewApplicationsMetricResponse>(
            $"api/companies/{companyId}/recruitment/metrics/new-applications", HrApiJsonOptions.Default, cancellationToken);

    public Task<CandidatesInProgressMetricResponse?> GetCandidatesInProgressMetricOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<CandidatesInProgressMetricResponse>(
            $"api/companies/{companyId}/recruitment/metrics/candidates-in-progress", HrApiJsonOptions.Default, cancellationToken);

    // Swallowing sibling for non-DSH-03 consumers (OffersAwaitingResponseWidget).
    public async Task<OffersAwaitingResponseMetricResponse?> GetOffersAwaitingResponseMetricAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<OffersAwaitingResponseMetricResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/recruitment/metrics/offers-awaiting-response", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    // Non-swallowing sibling of GetOffersAwaitingResponseMetricAsync, for callers (e.g.
    // OffersAwaitingResponseWidget) that use WidgetSourceLoader to distinguish a failed load from a
    // genuine empty result rather than collapsing both into null.
    public async Task<OffersAwaitingResponseMetricResponse?> GetOffersAwaitingResponseMetricOrThrowAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        await Http.GetFromJsonAsync<OffersAwaitingResponseMetricResponse>(
            $"api/companies/{companyId}/recruitment/metrics/offers-awaiting-response",
            HrApiJsonOptions.Default, cancellationToken);

    public async Task<GetVacancyResponse?> GetVacancyAsync(Guid companyId, Guid id)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetVacancyResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/vacancies/{id}", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<(CreateVacancyResponse? Result, string? Error)> CreateVacancyAsync(Guid companyId, CreateVacancyRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/vacancies", request);
        var result = await ApiResponseReader.ReadJsonAsync<CreateVacancyResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to create vacancy."));
    }

    public async Task<(UpdateVacancyResponse? Result, string? Error)> UpdateVacancyAsync(Guid companyId, Guid id, UpdateVacancyRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/vacancies/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateVacancyResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to update vacancy."));
    }

    public async Task<(CloseVacancyResponse? Result, string? Error)> CloseVacancyAsync(Guid companyId, Guid id, DateOnly? closedAt = null)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{id}/close",
            new CloseVacancyRequest(companyId, id, closedAt));
        var result = await ApiResponseReader.ReadJsonAsync<CloseVacancyResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to close vacancy."));
    }

    public async Task<(PublishVacancyResponse? Result, string? Error)> PublishVacancyAsync(Guid companyId, Guid id, DateOnly? openedAt = null)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{id}/publish",
            new PublishVacancyRequest(companyId, id, openedAt));
        var result = await ApiResponseReader.ReadJsonAsync<PublishVacancyResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to publish vacancy."));
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
            IsAdvertisedInternally = response.IsAdvertisedInternally,
            Version = response.Version,
        };
    }

    // Ticket 2: concurrency-aware update — sends the loaded version and surfaces a stale-save 409.
    // The recruitment API returns no "code" on its 409 body, so ANY 409 is treated as a save conflict.
    public async Task<ApiSaveResult> UpdateAsync(
        Guid companyId, Guid id, VacancyEditModel model, int? expectedVersion)
    {
        var request = new UpdateVacancyRequest(
            companyId, id,
            model.PositionProfileId,
            FormText.Optional(model.AdvertTitle),
            FormText.Optional(model.AdvertDescription),
            model.HiringManagerId!.Value,
            AssignedRecruiterId: model.AssignedRecruiterId == Guid.Empty ? null : model.AssignedRecruiterId,
            IsAuthorisedCorrection: model.IsAuthorisedCorrection,
            CorrectionReason: FormText.Optional(model.CorrectionReason),
            IsAdvertisedInternally: model.IsAdvertisedInternally,
            ExpectedVersion: expectedVersion);

        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/vacancies/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateVacancyResponse>(response);

        if (result.Success)
            return ApiSaveResult.Ok(result.Value?.Version);

        // The recruitment API returns no "code" on its 409 body, so ANY 409 is treated as a save conflict.
        var isConflict = result.FailureKind is ApiFailureKind.Concurrency or ApiFailureKind.Conflict;
        return ApiSaveResult.Fail(
            result.DisplayMessage ?? (isConflict
                ? "Someone else changed this vacancy while you were editing."
                : "Failed to update vacancy."),
            isConflict);
    }

    async Task<(VacancyEditModel? Result, string? Error)> IEditService<VacancyEditModel, Guid>.CreateAsync(Guid companyId, VacancyEditModel model)
    {
        var request = new CreateVacancyRequest(
            companyId, model.PositionProfileId!.Value,
            FormText.Optional(model.AdvertTitle),
            FormText.Optional(model.AdvertDescription),
            model.HiringManagerId!.Value,
            model.AssignedRecruiterId == Guid.Empty ? null : model.AssignedRecruiterId,
            IsAdvertisedInternally: model.IsAdvertisedInternally);

        var (created, error) = await CreateVacancyAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(VacancyEditModel? Result, string? Error)> IEditService<VacancyEditModel, Guid>.UpdateAsync(Guid companyId, Guid id, VacancyEditModel model)
    {
        var request = new UpdateVacancyRequest(
            companyId, id,
            model.PositionProfileId,
            FormText.Optional(model.AdvertTitle),
            FormText.Optional(model.AdvertDescription),
            model.HiringManagerId!.Value,
            AssignedRecruiterId: model.AssignedRecruiterId == Guid.Empty ? null : model.AssignedRecruiterId,
            IsAuthorisedCorrection: model.IsAuthorisedCorrection,
            CorrectionReason: FormText.Optional(model.CorrectionReason),
            IsAdvertisedInternally: model.IsAdvertisedInternally);

        var (updated, error) = await UpdateVacancyAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }
}
