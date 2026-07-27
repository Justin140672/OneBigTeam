using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class RecruitmentKanbanService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<GetRecruitmentKanbanResponse?> GetKanbanAsync(
        Guid companyId, Guid vacancyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetRecruitmentKanbanResponse>(
                $"api/companies/{companyId}/vacancies/{vacancyId}/kanban", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(MoveApplicationStageResponse? Result, string? Error)> MoveApplicationStageAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, string newStatus, string? notes = null)
    {
        var request = new MoveApplicationStageRequest(companyId, vacancyId, applicationId, newStatus, notes);
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/move-stage",
            request, HrApiJsonOptions.Default);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<MoveApplicationStageResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to move applicant to the new stage."));
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        if (response.StatusCode is HttpStatusCode.NotFound)
            return "Application not found.";

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
