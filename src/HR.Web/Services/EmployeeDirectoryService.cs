using HR.Web.Models;
using System.Web;

namespace HR.Web.Services;

/// <summary>
/// Employee-facing directory. Backed by the "employees/directory" endpoints which only require an
/// authenticated employee (policy role:employee); company is resolved server-side from AppSession.
/// </summary>
public class EmployeeDirectoryService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<EmployeeDirectoryListResponse?> ListAsync(
        Guid companyId,
        string? search,
        Guid? departmentId,
        Guid? locationId,
        int pageNumber,
        int pageSize)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        if (departmentId is not null) query["departmentId"] = departmentId.ToString();
        if (locationId is not null) query["locationId"] = locationId.ToString();
        query["pageNumber"] = pageNumber.ToString();
        query["pageSize"] = pageSize.ToString();

        try
        {
            return await Http.GetFromJsonAsync<EmployeeDirectoryListResponse>(
                $"api/companies/{companyId}/employees/directory?{query}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<EmployeeDirectoryDetail?> GetAsync(Guid companyId, Guid employeeId)
    {
        try
        {
            return await Http.GetFromJsonAsync<EmployeeDirectoryDetail>(
                $"api/companies/{companyId}/employees/directory/{employeeId}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
