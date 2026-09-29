using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Web.Models;
using HR.SharedKernel;
using HR.SharedKernel.Http;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public sealed class ApplicationService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    // isInternal (internal recruitment Ticket 6): null = all applications (query param omitted);
    // true = only internal (Source == Internal); false = everything else.
    public async Task<ListApplicationsForVacancyResponse?> ListApplicationsForVacancyAsync(
        Guid companyId, Guid vacancyId, Guid? stageId = null, bool? isInternal = null)
    {
        try
        {
            var query = new List<string>();
            if (stageId is not null) query.Add($"stageId={stageId}");
            if (isInternal is not null) query.Add($"isInternal={(isInternal.Value ? "true" : "false")}");

            var url = $"api/companies/{companyId}/vacancies/{vacancyId}/applications";
            if (query.Count > 0) url += "?" + string.Join("&", query);

            return await Http.GetFromJsonAsync<ListApplicationsForVacancyResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Internal recruitment Ticket 6: a single candidate's applications across every vacancy (the
    // Candidate Detail "Applications" history). pageSize is capped at 200 server-side. Returns null
    // on failure so the caller can show an error rather than a misleading empty state.
    public async Task<SearchApplicationsResponse?> SearchApplicationsForCandidateAsync(
        Guid companyId, Guid candidateId, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<SearchApplicationsResponse>(
                $"api/companies/{companyId}/recruitment/applications/search?candidateId={candidateId}&pageSize={pageSize}",
                HrApiJsonOptions.Default, cancellationToken);
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

    public Task<GetApplicationsByStatusResponse?> GetApplicationsByStatusOrThrowAsync(
        Guid companyId, Guid stageId, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<GetApplicationsByStatusResponse>(
            $"api/companies/{companyId}/recruitment/applications?stageId={stageId}",
            HrApiJsonOptions.Default, cancellationToken);

    public async Task<(CreateApplicationResponse? Result, string? Error)> CreateApplicationAsync(
        Guid companyId, Guid vacancyId, Guid candidateId, string? notes,
        string? source = null, Guid? sourceExternalRecruiterId = null, Guid? cvDocumentId = null)
    {
        // cvDocumentId (internal recruitment Ticket 3): an existing Kind=Cv document of this same
        // candidate, recorded as the CV submitted with the application. Null = none.
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new CreateApplicationRequest(companyId, vacancyId, candidateId, FormText.Optional(notes), source, sourceExternalRecruiterId, cvDocumentId));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CreateApplicationResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to create application."));
    }

    public const string CandidateEmailExistsCode = "candidate_email_exists";

    // Internal recruitment Ticket 3: create a brand-new candidate and their application to the vacancy
    // in one multipart call, optionally with a CV file. Optional fields are omitted (never sent as
    // empty strings). Distinguishes success, a duplicate-email 409 (carrying the existing candidate)
    // and any other failure (400/404 {error}, 422 validation problem details, network).
    public async Task<CreateCandidateApplicationResult> CreateCandidateApplicationAsync(
        Guid companyId, Guid vacancyId, CreateCandidateApplicationRequest request, IBrowserFile? cvFile,
        CancellationToken cancellationToken = default)
    {
        if (cvFile is not null && cvFile.Size > CandidateService.MaxCandidateDocumentBytes)
            return CreateCandidateApplicationResult.Failure("The CV file is larger than the 20 MB limit.");

        using var content = new MultipartFormDataContent();
        AddFormField(content, "FirstName", FormText.Optional(request.FirstName));
        AddFormField(content, "LastName", FormText.Optional(request.LastName));
        AddFormField(content, "Email", FormText.Optional(request.Email));
        AddFormField(content, "Phone", FormText.Optional(request.Phone));
        AddFormField(content, "ResumeUrl", FormText.Optional(request.ResumeUrl));
        AddFormField(content, "Notes", FormText.Optional(request.Notes));
        AddFormField(content, "Source", FormText.Optional(request.Source));
        AddFormField(content, "SourceExternalRecruiterId", request.SourceExternalRecruiterId?.ToString());

        Stream? cvStream = null;
        try
        {
            if (cvFile is not null)
            {
                cvStream = cvFile.OpenReadStream(maxAllowedSize: CandidateService.MaxCandidateDocumentBytes, cancellationToken);
                var fileContent = new StreamContent(cvStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(cvFile.ContentType) ? "application/octet-stream" : cvFile.ContentType);
                content.Add(fileContent, "CvFile", cvFile.Name);
            }

            HttpResponseMessage response;
            try
            {
                response = await Http.PostAsync(
                    $"api/companies/{companyId}/vacancies/{vacancyId}/applications/new-candidate", content, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                return CreateCandidateApplicationResult.Failure("Unable to reach the server. Please check your connection and try again.");
            }
            catch (OperationCanceledException)
            {
                return CreateCandidateApplicationResult.Failure("The request timed out. Please try again.");
            }
            catch (IOException)
            {
                return CreateCandidateApplicationResult.Failure("The selected CV file could not be read. Please choose it again.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                    var conflict = TryDeserialize<CandidateEmailExistsEnvelope>(raw);
                    if (conflict is { Code: CandidateEmailExistsCode, ExistingCandidateId: Guid existingId })
                    {
                        return CreateCandidateApplicationResult.DuplicateEmail(new DuplicateCandidateModel(
                            existingId,
                            conflict.ExistingCandidateFirstName ?? string.Empty,
                            conflict.ExistingCandidateLastName ?? string.Empty,
                            conflict.ExistingCandidateEmail ?? request.Email,
                            conflict.ExistingCandidateIsActive ?? true));
                    }

                    return CreateCandidateApplicationResult.Failure(conflict?.Error ?? "A conflict occurred.");
                }

                var result = await ApiResponseReader.ReadJsonAsync<CreateCandidateApplicationResponse>(
                    response, HrApiJsonOptions.Default, cancellationToken);
                return result.Success && result.Value is not null
                    ? CreateCandidateApplicationResult.Success(result.Value)
                    : CreateCandidateApplicationResult.Failure(result.DisplayMessage ?? "Failed to add the candidate.");
            }
        }
        finally
        {
            if (cvStream is not null)
                await cvStream.DisposeAsync();
        }
    }

    private static void AddFormField(MultipartFormDataContent content, string name, string? value)
    {
        if (value is not null)
            content.Add(new StringContent(value), name);
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, HrApiJsonOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CandidateEmailExistsEnvelope(
        string? Error,
        string? Code,
        Guid? ExistingCandidateId,
        string? ExistingCandidateFirstName,
        string? ExistingCandidateLastName,
        string? ExistingCandidateEmail,
        bool? ExistingCandidateIsActive);

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

    public async Task<(OfferCandidateResponse? Result, string? Error)> OfferCandidateAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, OfferCandidateRequest? request = null)
    {
        // A truly bodyless POST (no Content-Type header at all) gets rejected by FastEndpoints
        // with 415 Unsupported Media Type — post a JSON body. Ticket #2 adds optional offer terms;
        // when the caller passes no request we still send the route-bound identifiers as an object.
        var body = request ?? new OfferCandidateRequest(companyId, vacancyId, applicationId);
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer", body, HrApiJsonOptions.Default);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<OfferCandidateResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to make offer."));
    }

    public async Task<(RespondToOfferResponse? Result, string? Error)> RespondToOfferAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, string status)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer/response",
            new RespondToOfferRequest(companyId, vacancyId, applicationId, status), HrApiJsonOptions.Default);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<RespondToOfferResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to record the offer response."));
    }

    public async Task<(RejectCandidateResponse? Result, string? Error)> RejectCandidateAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, string? rejectionReason)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/reject",
            new RejectCandidateRequest(companyId, vacancyId, applicationId, FormText.Optional(rejectionReason)));

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

    // Internal recruitment Ticket 7: complete an INTERNAL application by updating the existing
    // employee's role (Hire is rejected server-side for internal applications). A past effective date
    // returns 409 unless ConfirmBackdatedEffectiveDate is true.
    public async Task<(AppointInternalCandidateResponse? Result, string? Error)> AppointInternalCandidateAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, AppointInternalCandidateRequest request)
    {
        var result = await ApiResponseReader.ExecuteAsync<AppointInternalCandidateResponse>(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/appoint",
                request, HrApiJsonOptions.Default, ct),
            HrApiJsonOptions.Default);

        return result.Success
            ? (result.Value, null)
            : (null, result.DisplayMessage ?? "Failed to complete the internal appointment.");
    }

    public async Task<(SaveCvReviewNotesResponse? Result, string? Error)> SaveCvReviewNotesAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, string? cvReviewNotes)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/cv-review-notes",
            new SaveCvReviewNotesRequest(companyId, vacancyId, applicationId, FormText.Optional(cvReviewNotes)));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SaveCvReviewNotesResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to save CV review notes."));
    }

    public async Task<(MoveApplicationForwardResponse? Result, string? Error)> MoveApplicationForwardAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, string? cvReviewNotes)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/move-forward",
            new MoveApplicationForwardRequest(companyId, vacancyId, applicationId, FormText.Optional(cvReviewNotes)));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<MoveApplicationForwardResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to move the application forward."));
    }

    // Internal recruitment Ticket 1: set (or, with null, remove) the CV submitted with this application.
    // 409 {error, code:"concurrency"} on a stale expectedVersion; 400 {error, code:"validation"} for a
    // document that isn't a CV belonging to this application's candidate.
    public async Task<(SetApplicationCvResponse? Result, string? Error)> SetApplicationCvAsync(
        Guid companyId, Guid vacancyId, Guid applicationId, Guid? cvDocumentId, int expectedVersion)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/cv",
            new SetApplicationCvRequest(cvDocumentId, expectedVersion), HrApiJsonOptions.Default);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SetApplicationCvResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to update the application's CV."));
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
