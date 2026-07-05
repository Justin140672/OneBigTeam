using HR.Web.Models;

namespace HR.Web.Services;

public sealed class CompensationService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<CurrentCompensationModel?> GetCurrentCompensationAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<CurrentCompensationModel>(
                $"api/companies/{companyId}/employees/{employeeId}/compensation/current",
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
