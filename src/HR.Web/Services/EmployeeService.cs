using HR.Web.Models;
using System.Web;

namespace HR.Web.Services;

public class EmployeeService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<ListEmployeesResponse?> ListEmployeesAsync(
        Guid companyId,
        string? search = null,
        int pageNumber = 1,
        int pageSize = 20)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        query["pageNumber"] = pageNumber.ToString();
        query["pageSize"] = pageSize.ToString();

        try
        {
            return await Http.GetFromJsonAsync<ListEmployeesResponse>(
                $"api/companies/{companyId}/employees?{query}");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<GetEmployeeResponse?> GetEmployeeAsync(Guid companyId, Guid id)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetEmployeeResponse>(
                $"api/companies/{companyId}/employees/{id}");
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool Success, string? ConflictMessage)> UpdateEmployeeProfileAsync(
        Guid companyId, Guid id, UpdateEmployeeProfileRequest request)
    {
        var response = await Http.PutAsJsonAsync(
            $"api/companies/{companyId}/employees/{id}/profile", request);

        if (response.IsSuccessStatusCode)
            return (true, null);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (false, body?.Error ?? "A conflict occurred.");
        }

        return (false, "Failed to save profile.");
    }

    public async Task<GetMyPersonalDetailsResponse?> GetMyPersonalDetailsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetMyPersonalDetailsResponse>(
                $"api/companies/{companyId}/employees/me/personal-details", cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(Guid? TaskId, string? Error)> RequestPersonalDetailsChangeAsync(
        Guid companyId,
        Guid employeeId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/personal-details-change-requests",
                new RequestPersonalDetailsChangeRequest(notes),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, "Unable to submit your request. Please try again.");

            var result = await response.Content
                .ReadFromJsonAsync<RequestPersonalDetailsChangeResponse>(cancellationToken);
            return (result?.TaskId, null);
        }
        catch { return (null, "An unexpected error occurred."); }
    }

    public async Task<GetMyContactDetailsResponse?> GetMyContactDetailsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetMyContactDetailsResponse>(
                $"api/companies/{companyId}/employees/me/contact-details", cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(bool Success, string? Error)> UpdateMyContactDetailsAsync(
        Guid companyId,
        UpdateMyContactDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/employees/me/contact-details", request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return (true, null);

            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var body = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken);
                var first = body?.Errors?.Values.SelectMany(v => v).FirstOrDefault();
                return (false, first ?? "Validation failed.");
            }

            return (false, "Failed to save contact details.");
        }
        catch { return (false, "An unexpected error occurred."); }
    }

    private sealed record ValidationErrorEnvelope(Dictionary<string, string[]>? Errors);

    public async Task<ListNationalitiesResponse?> ListNationalitiesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<ListNationalitiesResponse>(
                "api/nationalities", cancellationToken);
        }
        catch { return null; }
    }

    public async Task<(CreateEmployeeResponse? Employee, string? Error)> CreateEmployeeAsync(
        Guid companyId, CreateEmployeeRequest request)
    {
        var response = await Http.PostAsJsonAsync(
            $"api/companies/{companyId}/employees", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateEmployeeResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An employee with that email already exists.");
        }

        return (null, "Failed to create employee.");
    }

    private sealed record ErrorEnvelope(string? Error);
}
