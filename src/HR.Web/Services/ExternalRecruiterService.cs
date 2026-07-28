using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class ExternalRecruiterService(IHttpClientFactory httpClientFactory) : IEditService<ExternalRecruiterEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    // ── EXTERNAL RECRUITERS (#75) ────────────────────────────────────────────

    public async Task<ListExternalRecruitersResponse?> ListExternalRecruitersAsync(
        Guid companyId, string? search = null, bool? isActive = null, int pageNumber = 1, int pageSize = 20)
    {
        try
        {
            var url = $"api/companies/{companyId}/external-recruiters";
            var query = new List<string> { $"pageNumber={pageNumber}", $"pageSize={pageSize}" };
            if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
            if (isActive is not null) query.Add($"isActive={isActive.Value}");
            url += "?" + string.Join("&", query);

            return await Http.GetFromJsonAsync<ListExternalRecruitersResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetExternalRecruiterResponse?> GetExternalRecruiterAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetExternalRecruiterResponse>(
                $"api/companies/{companyId}/external-recruiters/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateExternalRecruiterResponse? Result, string? Error)> CreateExternalRecruiterAsync(
        Guid companyId, CreateExternalRecruiterRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/external-recruiters", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CreateExternalRecruiterResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to create external recruiter."));
    }

    public async Task<(UpdateExternalRecruiterResponse? Result, string? Error)> UpdateExternalRecruiterAsync(
        Guid companyId, Guid id, UpdateExternalRecruiterRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/external-recruiters/{id}", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<UpdateExternalRecruiterResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to update external recruiter."));
    }

    public async Task<(SetExternalRecruiterActiveStatusResponse? Result, string? Error)> SetActiveStatusAsync(
        Guid companyId, Guid id, bool isActive)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/external-recruiters/{id}/active-status",
            new SetExternalRecruiterActiveStatusRequest(companyId, id, isActive));

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<SetExternalRecruiterActiveStatusResponse>(HrApiJsonOptions.Default), null);

        return (null, await ReadErrorAsync(response, "Failed to update active status."));
    }

    public async Task<GetExternalRecruiterActivitySummaryResponse?> GetActivitySummaryAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetExternalRecruiterActivitySummaryResponse>(
                $"api/companies/{companyId}/external-recruiters/{id}/activity-summary", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // ── IEditService<ExternalRecruiterEditModel, Guid> ───────────────────────

    async Task<ExternalRecruiterEditModel?> IEditService<ExternalRecruiterEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetExternalRecruiterAsync(companyId, id);
        return response is null ? null : new ExternalRecruiterEditModel
        {
            AgencyName = response.AgencyName,
            ContactName = response.ContactName,
            ContactEmail = response.ContactEmail,
            ContactTelephone = response.ContactTelephone,
            Website = response.Website,
            Notes = response.Notes,
        };
    }

    async Task<(ExternalRecruiterEditModel? Result, string? Error)> IEditService<ExternalRecruiterEditModel, Guid>.CreateAsync(
        Guid companyId, ExternalRecruiterEditModel model)
    {
        var request = new CreateExternalRecruiterRequest(
            companyId, model.AgencyName.Trim(),
            string.IsNullOrWhiteSpace(model.ContactName) ? null : model.ContactName.Trim(),
            string.IsNullOrWhiteSpace(model.ContactEmail) ? null : model.ContactEmail.Trim(),
            string.IsNullOrWhiteSpace(model.ContactTelephone) ? null : model.ContactTelephone.Trim(),
            string.IsNullOrWhiteSpace(model.Website) ? null : model.Website.Trim(),
            string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim());

        var (created, error) = await CreateExternalRecruiterAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(ExternalRecruiterEditModel? Result, string? Error)> IEditService<ExternalRecruiterEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, ExternalRecruiterEditModel model)
    {
        var request = new UpdateExternalRecruiterRequest(
            companyId, id, model.AgencyName.Trim(),
            string.IsNullOrWhiteSpace(model.ContactName) ? null : model.ContactName.Trim(),
            string.IsNullOrWhiteSpace(model.ContactEmail) ? null : model.ContactEmail.Trim(),
            string.IsNullOrWhiteSpace(model.ContactTelephone) ? null : model.ContactTelephone.Trim(),
            string.IsNullOrWhiteSpace(model.Website) ? null : model.Website.Trim(),
            string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim());

        var (updated, error) = await UpdateExternalRecruiterAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        if (response.StatusCode is HttpStatusCode.NotFound)
            return "External recruiter not found.";

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
