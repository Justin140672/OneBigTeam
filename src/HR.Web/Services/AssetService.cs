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

    public async Task<List<AvailableAssetItem>?> ListAvailableAssetsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var all = await Http.GetFromJsonAsync<List<AvailableAssetItem>>(
                $"api/companies/{companyId}/assets?status=Available", cancellationToken);
            return all;
        }
        catch { return null; }
    }

    public async Task<bool> AssignAssetAsync(
        Guid companyId, Guid assetId, Guid employeeId, Guid assignedBy, string? notes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/assets/{assetId}/assignments",
                new { companyId, assetId, employeeId, assignedBy, notes },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> RequestReturnAsync(
        Guid companyId, Guid assignmentId, Guid requestedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/asset-assignments/{assignmentId}/request-return",
                new { companyId, id = assignmentId, requestedBy },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<AssetAssignmentItem>?> GetAssetAssignmentsAsync(
        Guid companyId, Guid assetId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<List<AssetAssignmentItem>>(
                $"api/companies/{companyId}/assets/{assetId}/assignments", cancellationToken);
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
