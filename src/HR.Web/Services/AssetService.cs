using System.Net.Http.Json;
using HR.SharedKernel.Idempotency;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class AssetService(HrApiHttpClientFactory httpClientFactory)
    : IEditService<AssetEditModel, Guid>, IConcurrencyAwareEditService<AssetEditModel, Guid>,
      IIdempotentCreateService<AssetEditModel>
{
    private HttpClient Http => httpClientFactory.CreateClient();

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

    /// <summary>
    /// Ticket 3 (P1) final follow-up item 1: stateless with respect to operation identity - the
    /// caller supplies <paramref name="idempotencyKey"/> and owns its lifecycle. A network
    /// failure/timeout throws before reaching any return here (this method has no catch of its
    /// own, matching its pre-existing behaviour) rather than being swallowed into a returned
    /// tuple, so the caller can tell "ambiguous - keep the key for a retry" apart from a
    /// definitive outcome without string-matching an error message.
    /// </summary>
    public async Task<(CreateAssetResponse? Result, string? Error)> CreateAssetAsync(
        Guid companyId, CreateAssetRequest request, Guid idempotencyKey)
    {
        var response = await Http.PostAsJsonIdempotentAsync(
            $"api/companies/{companyId}/assets", request, idempotencyKey);

        if (response.IsSuccessStatusCode)
        {
            var created = await response.Content.ReadFromJsonAsync<CreateAssetResponse>();
            return (created, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Either a genuine duplicate asset number, or the idempotency layer's own "key reused
            // for a different request" conflict.
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

    async Task<AssetEditModel?> IEditService<AssetEditModel, Guid>.GetByIdAsync(Guid companyId, Guid id)
    {
        var response = await GetAssetAsync(companyId, id);
        return response is null ? null : new AssetEditModel
        {
            AssetNumber = response.AssetNumber,
            CategoryId = response.CategoryId,
            Name = response.Name,
            Manufacturer = response.Manufacturer,
            Model = response.Model,
            SerialNumber = response.SerialNumber,
            PurchaseDate = response.PurchaseDate,
            PurchasePrice = response.PurchasePrice,
            Version = response.Version,
        };
    }

    // Ticket 2: concurrency-aware update — sends the loaded version and surfaces the stale-save 409.
    public async Task<ApiSaveResult> UpdateAsync(
        Guid companyId, Guid id, AssetEditModel model, int? expectedVersion)
    {
        var request = new UpdateAssetRequest(
            companyId, id, model.AssetNumber.Trim(), model.CategoryId!.Value, model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Manufacturer) ? null : model.Manufacturer.Trim(),
            string.IsNullOrWhiteSpace(model.Model) ? null : model.Model.Trim(),
            string.IsNullOrWhiteSpace(model.SerialNumber) ? null : model.SerialNumber.Trim(),
            model.PurchaseDate, model.PurchasePrice, expectedVersion);

        var response = await Http.PutAsJsonAsync($"api/companies/{companyId}/assets/{id}", request);

        if (response.IsSuccessStatusCode)
        {
            var updated = await response.Content.ReadFromJsonAsync<UpdateAssetResponse>();
            return ApiSaveResult.Ok(updated?.Version);
        }

        var body = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return ApiSaveResult.Fail(
                body?.Error ?? "An asset with that number already exists.", body?.Code == "concurrency");

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return ApiSaveResult.Fail("Asset not found.");

        return ApiSaveResult.Fail(body?.Error ?? "Failed to update asset.");
    }

    // Callers that only know IEditService<AssetEditModel, Guid> (not idempotency-aware) get a
    // fresh key generated per call - correct but with no cross-retry dedup benefit. The real UI
    // path (EditPageBase<TModel, TKey>) detects IIdempotentCreateService<AssetEditModel> below and
    // owns a real key's lifecycle across retries instead of hitting this overload.
    async Task<(AssetEditModel? Result, string? Error)> IEditService<AssetEditModel, Guid>.CreateAsync(
        Guid companyId, AssetEditModel model) =>
        await CreateFromModelAsync(companyId, model, Guid.NewGuid());

    async Task<(AssetEditModel? Result, string? Error)> IIdempotentCreateService<AssetEditModel>.CreateAsync(
        Guid companyId, AssetEditModel model, Guid idempotencyKey) =>
        await CreateFromModelAsync(companyId, model, idempotencyKey);

    private async Task<(AssetEditModel? Result, string? Error)> CreateFromModelAsync(
        Guid companyId, AssetEditModel model, Guid idempotencyKey)
    {
        var request = new CreateAssetRequest(
            companyId, model.AssetNumber.Trim(), model.CategoryId!.Value, model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Manufacturer) ? null : model.Manufacturer.Trim(),
            string.IsNullOrWhiteSpace(model.Model) ? null : model.Model.Trim(),
            string.IsNullOrWhiteSpace(model.SerialNumber) ? null : model.SerialNumber.Trim(),
            model.PurchaseDate, model.PurchasePrice);

        var (created, error) = await CreateAssetAsync(companyId, request, idempotencyKey);
        return (created is null ? null : model, error);
    }

    async Task<(AssetEditModel? Result, string? Error)> IEditService<AssetEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, AssetEditModel model)
    {
        var request = new UpdateAssetRequest(
            companyId, id, model.AssetNumber.Trim(), model.CategoryId!.Value, model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Manufacturer) ? null : model.Manufacturer.Trim(),
            string.IsNullOrWhiteSpace(model.Model) ? null : model.Model.Trim(),
            string.IsNullOrWhiteSpace(model.SerialNumber) ? null : model.SerialNumber.Trim(),
            model.PurchaseDate, model.PurchasePrice);

        var (updated, error) = await UpdateAssetAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private sealed record ErrorEnvelope(string? Error, string? Code = null);
}
