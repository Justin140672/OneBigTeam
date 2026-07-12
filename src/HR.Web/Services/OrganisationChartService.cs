using HR.Web.Models;

namespace HR.Web.Services;

public sealed class OrganisationChartService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<OrganisationChartResponse?> GetOrganisationChartAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<OrganisationChartResponse>(
                $"api/companies/{companyId}/organisation-chart", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
