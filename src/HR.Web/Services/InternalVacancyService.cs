using HR.Web.Models;
using System.Web;

namespace HR.Web.Services;

/// <summary>
/// Employee-facing internal vacancies. Backed by the read-only "internal-vacancies" endpoints which
/// only require an authenticated employee (any authenticated employee of the company).
/// </summary>
public class InternalVacancyService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<InternalVacancyListResponse?> ListAsync(Guid companyId, string? search)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        var qs = query.Count > 0 ? $"?{query}" : string.Empty;

        try
        {
            return await Http.GetFromJsonAsync<InternalVacancyListResponse>(
                $"api/companies/{companyId}/internal-vacancies{qs}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<InternalVacancyDetail?> GetAsync(Guid companyId, Guid vacancyId)
    {
        try
        {
            return await Http.GetFromJsonAsync<InternalVacancyDetail>(
                $"api/companies/{companyId}/internal-vacancies/{vacancyId}", HrApiJsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
