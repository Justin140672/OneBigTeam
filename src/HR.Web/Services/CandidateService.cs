using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class CandidateService(IHttpClientFactory httpClientFactory)
    : IEditService<CandidateEditModel, Guid>, IConcurrencyAwareEditService<CandidateEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListCandidatesResponse?> ListCandidatesAsync(Guid companyId, string? search = null, int pageNumber = 1, int pageSize = 20, bool includeInactive = false)
    {
        try
        {
            var url = $"api/companies/{companyId}/candidates?pageNumber={pageNumber}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
            if (includeInactive) url += "&includeInactive=true";

            return await Http.GetFromJsonAsync<ListCandidatesResponse>(url, HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetCandidateResponse?> GetCandidateAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetCandidateResponse>(
                $"api/companies/{companyId}/candidates/{id}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateCandidateResponse? Result, string? Error)> CreateCandidateAsync(Guid companyId, CreateCandidateRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/candidates", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<CreateCandidateResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to create candidate."));
    }

    public async Task<(UpdateCandidateResponse? Result, string? Error)> UpdateCandidateAsync(Guid companyId, Guid id, UpdateCandidateRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/candidates/{id}", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<UpdateCandidateResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to update candidate."));
    }

    public async Task<(DeactivateCandidateResponse? Result, string? Error)> DeactivateCandidateAsync(Guid companyId, Guid candidateId, string reason)
    {
        var request = new DeactivateCandidateRequest(companyId, candidateId, reason);
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/candidates/{candidateId}/deactivate", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<DeactivateCandidateResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to deactivate candidate."));
    }

    public async Task<(ReactivateCandidateResponse? Result, string? Error)> ReactivateCandidateAsync(Guid companyId, Guid candidateId)
    {
        var request = new ReactivateCandidateRequest(companyId, candidateId);
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/candidates/{candidateId}/reactivate", request);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<ReactivateCandidateResponse>(), null);

        return (null, await ReadErrorAsync(response, "Failed to reactivate candidate."));
    }

    // ── IEditService<CandidateEditModel, Guid> ──────────────────────────────────

    async Task<CandidateEditModel?> IEditService<CandidateEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetCandidateAsync(companyId, id);
        return response is null ? null : new CandidateEditModel
        {
            FirstName = response.FirstName,
            LastName = response.LastName,
            Email = response.Email,
            Phone = response.Phone,
            ResumeUrl = response.ResumeUrl,
            Version = response.Version,
        };
    }

    // Ticket 2: concurrency-aware update — sends the loaded version and surfaces a stale-save 409.
    // The recruitment API returns no "code" on its 409 body, so ANY 409 from this endpoint is
    // treated as a save conflict.
    public async Task<ApiSaveResult> UpdateAsync(
        Guid companyId, Guid id, CandidateEditModel model, int? expectedVersion)
    {
        var request = new UpdateCandidateRequest(
            companyId, id, model.FirstName.Trim(), model.LastName.Trim(), model.Email.Trim(),
            string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            string.IsNullOrWhiteSpace(model.ResumeUrl) ? null : model.ResumeUrl.Trim(),
            expectedVersion);

        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/candidates/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateCandidateResponse>();
            return ApiSaveResult.Ok(updated?.Version);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
            return ApiSaveResult.Fail(
                await ReadErrorAsync(response, "Someone else changed this candidate while you were editing.")
                    ?? "Someone else changed this candidate while you were editing.",
                isConcurrencyConflict: true);

        return ApiSaveResult.Fail(await ReadErrorAsync(response, "Failed to update candidate.") ?? "Failed to update candidate.");
    }

    async Task<(CandidateEditModel? Result, string? Error)> IEditService<CandidateEditModel, Guid>.CreateAsync(Guid companyId, CandidateEditModel model)
    {
        var request = new CreateCandidateRequest(
            companyId, model.FirstName.Trim(), model.LastName.Trim(), model.Email.Trim(),
            string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            string.IsNullOrWhiteSpace(model.ResumeUrl) ? null : model.ResumeUrl.Trim());

        var (created, error) = await CreateCandidateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(CandidateEditModel? Result, string? Error)> IEditService<CandidateEditModel, Guid>.UpdateAsync(Guid companyId, Guid id, CandidateEditModel model)
    {
        var request = new UpdateCandidateRequest(
            companyId, id, model.FirstName.Trim(), model.LastName.Trim(), model.Email.Trim(),
            string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            string.IsNullOrWhiteSpace(model.ResumeUrl) ? null : model.ResumeUrl.Trim());

        var (updated, error) = await UpdateCandidateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        if (response.StatusCode is HttpStatusCode.NotFound)
            return "Candidate not found.";

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
