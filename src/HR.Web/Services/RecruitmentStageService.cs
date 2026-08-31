using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class RecruitmentStageService(IHttpClientFactory httpClientFactory) : IEditService<RecruitmentStageEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListRecruitmentStagesResponse?> ListStagesAsync(Guid companyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListRecruitmentStagesResponse>(
                $"api/companies/{companyId}/recruitment-stages", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
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

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CreateRecruitmentStageResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to create recruitment stage."));
    }

    public async Task<(UpdateRecruitmentStageResponse? Result, string? Error)> UpdateAsync(
        Guid companyId, Guid recruitmentStageId, UpdateRecruitmentStageRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages/{recruitmentStageId}", request, HrApiJsonOptions.Default);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<UpdateRecruitmentStageResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to update recruitment stage."));
    }

    public async Task<(ReorderRecruitmentStagesResponse? Result, string? Error)> ReorderAsync(
        Guid companyId, IReadOnlyList<Guid> orderedStageIds)
    {
        var request = new ReorderRecruitmentStagesRequest(companyId, orderedStageIds);
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages/reorder", request, HrApiJsonOptions.Default);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ReorderRecruitmentStagesResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to reorder recruitment stages."));
    }

    public async Task<(SetRecruitmentStageActiveStatusResponse? Result, string? Error)> SetActiveStatusAsync(
        Guid companyId, Guid recruitmentStageId, bool isActive)
    {
        var request = new SetRecruitmentStageActiveStatusRequest(companyId, recruitmentStageId, isActive);
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/recruitment-stages/{recruitmentStageId}/active-status", request, HrApiJsonOptions.Default);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SetRecruitmentStageActiveStatusResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to update active status."));
    }

    public async Task<GetRecruitmentStageUsageResponse?> GetUsageAsync(Guid companyId, Guid recruitmentStageId)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetRecruitmentStageUsageResponse>(
                $"api/companies/{companyId}/recruitment-stages/{recruitmentStageId}/usage", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
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
        };
    }

    async Task<(RecruitmentStageEditModel? Result, string? Error)> IEditService<RecruitmentStageEditModel, Guid>.CreateAsync(
        Guid companyId, RecruitmentStageEditModel model)
    {
        // DisplayOrder for a new stage: append to the end of the current list (server assigns the
        // authoritative sequence anyway via reorder; this is just a sane initial slot).
        var existingCount = (await ListStagesAsync(companyId))?.Items.Count ?? 0;

        var request = new CreateRecruitmentStageRequest(
            companyId, model.Name.Trim(), existingCount + 1, model.IsTerminal, model.TerminalOutcome);

        var (created, error) = await CreateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(RecruitmentStageEditModel? Result, string? Error)> IEditService<RecruitmentStageEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, RecruitmentStageEditModel model)
    {
        var request = new UpdateRecruitmentStageRequest(
            companyId, id, model.Name.Trim(), model.IsTerminal, model.TerminalOutcome);

        var (updated, error) = await UpdateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        if (response.StatusCode is HttpStatusCode.NotFound)
            return "Recruitment stage not found.";

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
