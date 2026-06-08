using HR.Web.Models;
using System.Net.Http.Json;
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
        catch (HttpRequestException ex)
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
        catch (HttpRequestException)
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
