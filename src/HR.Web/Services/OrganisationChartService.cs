using System.Web;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class OrganisationChartService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<OrganisationChartResponse?> GetOrganisationChartAsync(
        Guid companyId,
        Guid? departmentId = null,
        Guid? locationId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (departmentId is not null) query["departmentId"] = departmentId.ToString();
        if (locationId is not null) query["locationId"] = locationId.ToString();
        if (!string.IsNullOrWhiteSpace(status)) query["status"] = status;

        try
        {
            return await Http.GetFromJsonAsync<OrganisationChartResponse>(
                $"api/companies/{companyId}/organisation-chart?{query}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
