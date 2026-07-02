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
                $"api/companies/{companyId}/employees/{employeeId}/assets", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<List<AvailableAssetItem>?> ListAvailableAssetsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var all = await Http.GetFromJsonAsync<List<AvailableAssetItem>>(
                $"api/companies/{companyId}/assets?status=Available", HrApiJsonOptions.Default, cancellationToken);
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
                $"api/companies/{companyId}/assets/{assetId}/assignments", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<AssetDetailModel?> GetAssetAsync(
        Guid companyId, Guid assetId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<AssetDetailModel>(
                $"api/companies/{companyId}/assets/{assetId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    // ── Admin asset list / CRUD ────────────────────────────────────────────

    public async Task<ListAssetsAdminResponse?> ListAssetsAsync(Guid companyId)
    {
        try
        {
            var items = await Http.GetFromJsonAsync<List<AssetListItemModel>>(
                $"api/companies/{companyId}/assets", HrApiJsonOptions.Default);
            return items is null ? null : new ListAssetsAdminResponse(items);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(CreateAssetResponse? Result, string? Error)> CreateAssetAsync(
        Guid companyId, CreateAssetRequest request)
    {
        var response = await Http.PostAsJsonAsync($"api/companies/{companyId}/assets", request);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateAssetResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An asset with that number already exists.");
        }

        return (null, "Failed to create asset.");
    }

    public async Task<(UpdateAssetResponse? Result, string? Error)> UpdateAssetAsync(
        Guid companyId, Guid id, UpdateAssetRequest request)
    {
        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/assets/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateAssetResponse>();
            return (updated, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
            return (null, body?.Error ?? "An asset with that number already exists.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, "Asset not found.");

        return (null, "Failed to update asset.");
    }

    public async Task<string?> RetireAssetAsync(Guid companyId, Guid id)
    {
        var response = await Http.DeleteAsync($"api/companies/{companyId}/assets/{id}");

        if (response.IsSuccessStatusCode)
            return null;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "Asset not found.";

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        return body?.Error ?? "Failed to retire asset.";
    }

    private sealed record ErrorEnvelope(string? Error);
}
