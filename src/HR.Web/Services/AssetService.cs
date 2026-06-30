using System.Net.Http.Json;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class AssetService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<List<EmployeeAssetItem>?> GetEmployeeAssignmentsAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<List<EmployeeAssetItem>>(
                $"api/companies/{companyId}/employees/{employeeId}/assets", cancellationToken);
        }
        catch { return null; }
    }

    public async Task<AssetDetailModel?> GetAssetAsync(
        Guid companyId, Guid assetId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<AssetDetailModel>(
                $"api/companies/{companyId}/assets/{assetId}", cancellationToken);
        }
        catch { return null; }
    }
}
