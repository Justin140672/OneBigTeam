using HR.SharedKernel;
using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class RecruitmentStageService(HrApiHttpClientFactory httpClientFactory)
    : IEditService<RecruitmentStageEditModel, Guid>, IConcurrencyAwareEditService<RecruitmentStageEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ListRecruitmentStagesResponse?> ListStagesAsync(Guid companyId)
    {
        var result = await ApiResponseReader.ExecuteAsync<ListRecruitmentStagesResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/recruitment-stages", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    // DSH-03: non-swallowing sibling of ListStagesAsync.
    public Task<ListRecruitmentStagesResponse?> ListStagesOrThrowAsync(Guid companyId) =>
        Http.GetFromJsonAsync<ListRecruitmentStagesResponse>(
            $"api/companies/{companyId}/recruitment-stages", HrApiJsonOptions.Default);

    public async Task<(CreateRecruitmentStageResponse? Result, string? Error)> CreateAsync(
        Guid companyId, CreateRecruitmentStageRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages", request, HrApiJsonOptions.Default);
        var result = await ApiResponseReader.ReadJsonAsync<CreateRecruitmentStageResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to create recruitment stage."));
    }

    public async Task<(UpdateRecruitmentStageResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid recruitmentStageId, UpdateRecruitmentStageRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages/{recruitmentStageId}", request, HrApiJsonOptions.Default);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateRecruitmentStageResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to update recruitment stage."));
    }

    public async Task<(ReorderRecruitmentStagesResponse? Result, string? Error)> ReorderAsync(
        Guid companyId, IReadOnlyList<Guid> orderedStageIds)
    {
        var request = new ReorderRecruitmentStagesRequest(companyId, orderedStageIds);
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages/reorder", request, HrApiJsonOptions.Default);
        var result = await ApiResponseReader.ReadJsonAsync<ReorderRecruitmentStagesResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to reorder recruitment stages."));
    }

    public async Task<(SetRecruitmentStageActiveStatusResponse? Result, string? Error)> SetActiveStatusAsync(
        Guid companyId, Guid recruitmentStageId, bool isActive)
    {
        var request = new SetRecruitmentStageActiveStatusRequest(companyId, recruitmentStageId, isActive);
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages/{recruitmentStageId}/active-status", request, HrApiJsonOptions.Default);
        var result = await ApiResponseReader.ReadJsonAsync<SetRecruitmentStageActiveStatusResponse>(response, HrApiJsonOptions.Default);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to update active status."));
    }

    public async Task<GetRecruitmentStageUsageResponse?> GetUsageAsync(Guid companyId, Guid recruitmentStageId)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetRecruitmentStageUsageResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/recruitment-stages/{recruitmentStageId}/usage", ct),
            HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    // ── IEditService<RecruitmentStageEditModel, Guid> ────────────────────────
    // No dedicated backend GetById endpoint — the list already returns full item detail.

    async Task<RecruitmentStageEditModel?> IEditService<RecruitmentStageEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var list = await ListStagesAsync(companyId);
        var existing = list?.Items.FirstOrDefault(s => s.Id == id);
        return existing is null ? null : new RecruitmentStageEditModel
        {
            Name = existing.Name,
            TerminalOutcome = existing.TerminalOutcome,
            Purpose = existing.Purpose,
            Version = existing.Version,
        };
    }

    // Ticket 2: concurrency-aware update — sends the loaded version and surfaces a stale-save 409.
    // The recruitment API returns no "code" on its 409 body, so ANY 409 is treated as a save conflict.
    public async Task<ApiSaveResult> UpdateAsync(
        Guid companyId, Guid id, RecruitmentStageEditModel model, int? expectedVersion)
    {
        var request = new UpdateRecruitmentStageRequest(
            companyId, id, FormText.Required(model.Name), model.IsTerminal, model.TerminalOutcome,
            model.IsTerminal ? null : model.Purpose, expectedVersion);

        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages/{id}", request, HrApiJsonOptions.Default);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateRecruitmentStageResponse>(response, HrApiJsonOptions.Default);

        if (result.Success)
            return ApiSaveResult.Ok(result.Value?.Version);

        // The recruitment API returns no "code" on its 409 body, so ANY 409 is treated as a save conflict.
        var isConflict = result.FailureKind is ApiFailureKind.Concurrency or ApiFailureKind.Conflict;
        return ApiSaveResult.Fail(
            result.DisplayMessage ?? (isConflict
                ? "Someone else changed this recruitment stage while you were editing."
                : "Failed to update recruitment stage."),
            isConflict);
    }

    async Task<(RecruitmentStageEditModel? Result, string? Error)> IEditService<RecruitmentStageEditModel, Guid>.CreateAsync(
        Guid companyId, RecruitmentStageEditModel model)
    {
        // DisplayOrder for a new stage: append to the end of the current list (server assigns the
        // authoritative sequence anyway via reorder; this is just a sane initial slot).
        var existingCount = (await ListStagesAsync(companyId))?.Items.Count ?? 0;

        var request = new CreateRecruitmentStageRequest(
            companyId, FormText.Required(model.Name), existingCount + 1, model.IsTerminal, model.TerminalOutcome,
            model.IsTerminal ? null : model.Purpose);

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(RecruitmentStageEditModel? Result, string? Error)> IEditService<RecruitmentStageEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, RecruitmentStageEditModel model)
    {
        var request = new UpdateRecruitmentStageRequest(
            companyId, id, FormText.Required(model.Name), model.IsTerminal, model.TerminalOutcome,
            model.IsTerminal ? null : model.Purpose);

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }
}
