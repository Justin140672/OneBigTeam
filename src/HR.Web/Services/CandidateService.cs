using HR.SharedKernel;
using HR.SharedKernel.Http;
using System.Net.Http.Headers;
using HR.Web.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public sealed class CandidateService(HrApiHttpClientFactory httpClientFactory)
    : IEditService<CandidateEditModel, Guid>, IConcurrencyAwareEditService<CandidateEditModel, Guid>
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ListCandidatesResponse?> ListCandidatesAsync(Guid companyId, string? search = null, int pageNumber = 1, int pageSize = 20, bool includeInactive = false)
    {
        search = FormText.OptionalSearch(search);
        var url = $"api/companies/{companyId}/candidates?pageNumber={pageNumber}&pageSize={pageSize}";
        if (search is not null) url += $"&search={Uri.EscapeDataString(search)}";
        if (includeInactive) url += "&includeInactive=true";

        var result = await ApiResponseReader.ExecuteAsync<ListCandidatesResponse>(
            ct => Http.GetAsync(url, ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<IReadOnlyList<CandidateListItemModel>?> ListAllCandidatesAsync(
        Guid companyId, string? search = null, bool includeInactive = false)
    {
        const int maxPageSize = 100;
        const int maxPages = 1000;

        var first = await ListCandidatesAsync(companyId, search, 1, maxPageSize, includeInactive);
        if (first is null)
            return null;

        var items = new List<CandidateListItemModel>(first.Items);
        for (var pageNumber = 2; pageNumber <= Math.Min(first.TotalPages, maxPages); pageNumber++)
        {
            var next = await ListCandidatesAsync(companyId, search, pageNumber, maxPageSize, includeInactive);
            if (next is null || next.Items.Count == 0)
                break;
            items.AddRange(next.Items);
        }

        return items;
    }

    public async Task<GetCandidateResponse?> GetCandidateAsync(Guid companyId, Guid id)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetCandidateResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/candidates/{id}", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public async Task<(CreateCandidateResponse? Result, string? Error)> CreateCandidateAsync(Guid companyId, CreateCandidateRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/candidates", request);
        var result = await ApiResponseReader.ReadJsonAsync<CreateCandidateResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to create candidate."));
    }

    public async Task<(UpdateCandidateResponse? Result, string? Error)> UpdateCandidateAsync(Guid companyId, Guid id, UpdateCandidateRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/candidates/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateCandidateResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to update candidate."));
    }

    public async Task<(DeactivateCandidateResponse? Result, string? Error)> DeactivateCandidateAsync(Guid companyId, Guid candidateId, string reason)
    {
        var request = new DeactivateCandidateRequest(companyId, candidateId, reason);
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/candidates/{candidateId}/deactivate", request);
        var result = await ApiResponseReader.ReadJsonAsync<DeactivateCandidateResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to deactivate candidate."));
    }

    public async Task<(ReactivateCandidateResponse? Result, string? Error)> ReactivateCandidateAsync(Guid companyId, Guid candidateId)
    {
        var request = new ReactivateCandidateRequest(companyId, candidateId);
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/candidates/{candidateId}/reactivate", request);
        var result = await ApiResponseReader.ReadJsonAsync<ReactivateCandidateResponse>(response);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to reactivate candidate."));
    }


    public async Task<ListCandidateDocumentsResponse?> ListCandidateDocumentsAsync(Guid companyId, Guid candidateId)
    {
        var result = await ApiResponseReader.ExecuteAsync<ListCandidateDocumentsResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/candidates/{candidateId}/documents", ct), HrApiJsonOptions.Default);
        return result.Success ? result.Value : null;
    }

    public static string GetCandidateDocumentProxyUrl(Guid companyId, Guid candidateId, Guid documentId) =>
        $"/companies/{companyId}/candidates/{candidateId}/cv/{documentId}";

    public const long MaxCandidateDocumentBytes = 20 * 1024 * 1024;

    public async Task<(UploadedCandidateDocumentModel? Document, string? Error)> UploadCandidateDocumentAsync(
        Guid companyId, Guid candidateId, string title, string kind, IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Size > MaxCandidateDocumentBytes)
            return (null, "The file is larger than the 20 MB limit.");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(title), "Title");
        content.Add(new StringContent(kind), "Kind");

        await using var stream = file.OpenReadStream(maxAllowedSize: MaxCandidateDocumentBytes, cancellationToken);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(fileContent, "File", file.Name);

        var result = await ApiResponseReader.ExecuteAsync<UploadedCandidateDocumentModel>(
            ct => Http.PostAsync($"api/companies/{companyId}/candidates/{candidateId}/documents", content, ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success && result.Value is not null
            ? (result.Value, null)
            : (null, result.DisplayMessage ?? "Upload failed.");
    }


    async Task<CandidateEditModel?> IEditService<CandidateEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetCandidateAsync(companyId, id);
        return response is null ? null : new CandidateEditModel
        {
            FirstName = response.FirstName,
            LastName = response.LastName,
            Email = response.Email,
            Phone = response.Phone,
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
            companyId, id, FormText.Required(model.FirstName), FormText.Required(model.LastName), FormText.Required(model.Email),
            FormText.Optional(model.Phone),
            expectedVersion);

        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/candidates/{id}", request);
        var result = await ApiResponseReader.ReadJsonAsync<UpdateCandidateResponse>(response);

        if (result.Success)
            return ApiSaveResult.Ok(result.Value?.Version);

        var isConflict = result.FailureKind is ApiFailureKind.Concurrency or ApiFailureKind.Conflict;
        return ApiSaveResult.Fail(
            result.DisplayMessage ?? (isConflict
                ? "Someone else changed this candidate while you were editing."
                : "Failed to update candidate."),
            isConflict);
    }

    async Task<(CandidateEditModel? Result, string? Error)> IEditService<CandidateEditModel, Guid>.CreateAsync(Guid companyId, CandidateEditModel model)
    {
        var request = new CreateCandidateRequest(
            companyId, FormText.Required(model.FirstName), FormText.Required(model.LastName), FormText.Required(model.Email),
            FormText.Optional(model.Phone));

        var (created, error) = await CreateCandidateAsync(companyId, request);
        return (created is null ? null : model, error);
    }

    async Task<(CandidateEditModel? Result, string? Error)> IEditService<CandidateEditModel, Guid>.UpdateAsync(Guid companyId, Guid id, CandidateEditModel model)
    {
        var request = new UpdateCandidateRequest(
            companyId, id, FormText.Required(model.FirstName), FormText.Required(model.LastName), FormText.Required(model.Email),
            FormText.Optional(model.Phone));

        var (updated, error) = await UpdateCandidateAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }
}
