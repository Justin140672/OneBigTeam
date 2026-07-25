using HR.Web.Models;

namespace HR.Web.Services;

public sealed class EmployeeNoteService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<IReadOnlyList<EmployeeNoteItemModel>> GetEmployeeNotesAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<GetEmployeeNotesResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/notes",
                HrApiJsonOptions.Default, cancellationToken);
            return response?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<(CreateEmployeeNoteResponse? Result, string? Error)> CreateEmployeeNoteAsync(
        Guid companyId, Guid employeeId, CreateEmployeeNoteRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/notes", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateEmployeeNoteResponse>();
            return (created, null);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Failed to create employee note.");
        }

        return (null, "Failed to create employee note.");
    }

    public async Task<(SupersedeEmployeeNoteResponse? Result, string? Error)> SupersedeEmployeeNoteAsync(
        Guid companyId, Guid employeeId, Guid originalNoteId, SupersedeEmployeeNoteRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees/{employeeId}/notes/{originalNoteId}/supersede", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<SupersedeEmployeeNoteResponse>();
            return (created, null);
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "Failed to supersede employee note.");
        }

        return (null, "Failed to supersede employee note.");
    }

    // Important notes first (newest-first within that group), then all other notes (also
    // newest-first) — pulled out as a standalone method so it's unit-testable independent of the
    // component that renders it.
    public static IReadOnlyList<EmployeeNoteItemModel> GroupAndSort(IReadOnlyList<EmployeeNoteItemModel> notes) =>
        notes
            .OrderByDescending(n => n.IsImportant)
            .ThenByDescending(n => n.CreatedDate)
            .ToList();

    private sealed record ErrorEnvelope(string? Error);
}
