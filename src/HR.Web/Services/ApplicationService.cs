using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class ApplicationService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListApplicationsForVacancyResponse?> ListApplicationsForVacancyAsync(Guid companyId, Guid vacancyId, Guid? stageId = null)
    {
        try
        {
            var url = $"api/companies/{companyId}/vacancies/{vacancyId}/applications";
            if (stageId is not null) url += $"?stageId={stageId}";

            return await Http.GetFromJsonAsync<ListApplicationsForVacancyResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetApplicationsByStatusResponse?> GetApplicationsByStatusAsync(
        Guid companyId, Guid stageId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetApplicationsByStatusResponse>(
                $"api/companies/{companyId}/recruitment/applications?stageId={stageId}",
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    // DSH-03: non-swallowing sibling of GetApplicationsByStatusAsync.
    public Task<GetApplicationsByStatusResponse?> GetApplicationsByStatusOrThrowAsync(
        Guid companyId, Guid stageId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<GetApplicationsByStatusResponse>(
            $"api/companies/{companyId}/recruitment/applications?stageId={stageId}",
            HrApiJsonOptions.Default, cancellationToken);

    public async Task<(CreateApplicationResponse? Result, string? Error)> CreateApplicationAsync(
        Guid companyId, Guid vacancyId, Guid candidateId, string? notes,
        string? source = null, Guid? sourceExternalRecruiterId = null)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new CreateApplicationRequest(companyId, vacancyId, candidateId, notes, source, sourceExternalRecruiterId));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to create application."));
    }

    public async Task<GetApplicationResponse?> GetApplicationAsync(Guid companyId, Guid vacancyId, Guid applicationId)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetApplicationResponse>(
                $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(WithdrawApplicationResponse? Result, string? Error)> WithdrawApplicationAsync(Guid companyId, Guid vacancyId, Guid applicationId)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}");

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<WithdrawApplicationResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to withdraw application."));
    }

    public async Task<(OfferCandidateResponse? Result, string? Error)> OfferCandidateAsync(Guid companyId, Guid vacancyId, Guid applicationId)
    {
        // A truly bodyless POST (no Content-Type header at all) gets rejected by FastEndpoints
        // with 415 Unsupported Media Type, even though OfferCandidateRequest's properties are
        // all route-bound — post an empty JSON object instead of a null body (see the identical
        // fix in DataImportService.ValidateSessionAsync/ConfirmSessionAsync).
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", new { });

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<OfferCandidateResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to make offer."));
    }

    public async Task<(RejectCandidateResponse? Result, string? Error)> RejectCandidateAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, string? rejectionReason)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/reject",
            new RejectCandidateRequest(companyId, vacancyId, applicationId, rejectionReason));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<RejectCandidateResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to reject candidate."));
    }

    public async Task<(HireCandidateResponse? Result, string? Error)> HireCandidateAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, HireCandidateRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/hire", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<HireCandidateResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to hire candidate."));
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
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
