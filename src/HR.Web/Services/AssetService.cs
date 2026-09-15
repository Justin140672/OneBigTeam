using System.Net.Http.Json;
using HR.SharedKernel.Idempotency;
using HR.Web.Models;
using HR.SharedKernel;

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
                new { companyId, assetId, employeeId, assignedBy, notes = FormText.Optional(notes) },
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
    /// Ticket 3 (P1) final gap: stateless with respect to operation identity - the caller supplies
    /// <paramref name="idempotencyKey"/> and owns its lifecycle. Returns a
    /// <see cref="MutationOutcome{T}"/> so the caller can distinguish a definitive outcome (Succeeded
    /// or Rejected — safe to discard the key) from an AmbiguousFailure (5xx/408/429/transport
    /// failure/timeout/cancellation-after-dispatch/malformed success body — the key and request must
    /// be retained so an unchanged retry replays rather than repeats the mutation). This method
    /// deliberately never lets an exception escape — every ambiguous case is captured and returned
    /// as <see cref="MutationOutcomeKind.AmbiguousFailure"/> instead.
    /// </summary>
    public async Task<MutationOutcome<CreateAssetResponse>> CreateAssetAsync(
        Guid companyId, CreateAssetRequest request, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsJsonIdempotentAsync(
                $"api/companies/{companyId}/assets", request, idempotencyKey, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A client-side request timeout surfaces as OperationCanceledException even though the
            // caller's own token was never cancelled — the request was already dispatched, so the
            // server may have received and committed it.
            return MutationOutcome<CreateAssetResponse>.Ambiguous(
                "The request timed out. It's safe to try again — a duplicate asset will not be created.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested by the caller. We cannot prove the request was never sent
            // (it may already be in flight on the wire), so this must still be treated as
            // ambiguous rather than assumed abandoned pre-dispatch.
            return MutationOutcome<CreateAssetResponse>.Ambiguous(
                "The request was cancelled before a response was received.");
        }
        catch (HttpRequestException)
        {
            return MutationOutcome<CreateAssetResponse>.Ambiguous(
                "A network error occurred. It's safe to try again — a duplicate asset will not be created.");
        }

        return await ClassifyCreateResponseAsync(response, cancellationToken);
    }

    // Bug fix (P1 follow-up to Ticket 3): sending the request and reading its response body are ONE
    // operation as far as idempotency is concerned. Previously only JsonException was caught while
    // parsing the success body, so a cancellation, response-stream I/O failure, or dropped
    // connection while READING the body (as opposed to sending) would escape uncaught here, bypass
    // AmbiguousFailure classification entirely, and propagate up into the caller/UI — abandoning
    // the retained idempotency key/snapshot instead of preserving them for a safe retry. The
    // response is always disposed once classification/body-reading is complete.
    private static async Task<MutationOutcome<CreateAssetResponse>> ClassifyCreateResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var _ = response;

        var kind = MutationHttpClassifier.ClassifyStatusCode(response.StatusCode);

        if (kind == MutationOutcomeKind.Succeeded)
        {
            try
            {
                var created = await response.Content.ReadFromJsonAsync<CreateAssetResponse>(cancellationToken: cancellationToken);
                if (created is null)
                    return MutationOutcome<CreateAssetResponse>.Ambiguous(
                        "The server's response could not be read. It's safe to try again.");

                return MutationOutcome<CreateAssetResponse>.Succeeded(created);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException
                or OperationCanceledException or HttpRequestException)
            {
                // Invalid/truncated success response, cancellation, or a transport failure while
                // reading the body — the mutation may well have committed, but this client cannot
                // confirm it from this response body.
                return MutationOutcome<CreateAssetResponse>.Ambiguous(
                    "The server's response could not be read. It's safe to try again.");
            }
        }

        if (kind == MutationOutcomeKind.AmbiguousFailure)
        {
            return MutationOutcome<CreateAssetResponse>.Ambiguous(
                "The request could not be confirmed. It's safe to try again — a duplicate asset will not be created.");
        }

        // Definitive rejection (400/401/403/404/409/422/...). The status code alone already
        // confirms a definitive outcome, so a failure reading the (secondary) error body must not
        // itself be treated as ambiguous — fall back to a generic message instead.
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Either a genuine duplicate asset number, or the idempotency layer's own "key reused
            // for a different request" conflict.
            var conflictBody = await TryReadErrorEnvelopeAsync(response, cancellationToken);
            return MutationOutcome<CreateAssetResponse>.Rejected(
                conflictBody?.Error ?? "An asset with that number already exists.");
        }

        var body = await TryReadErrorEnvelopeAsync(response, cancellationToken);
        return MutationOutcome<CreateAssetResponse>.Rejected(body?.Error ?? "Failed to create asset.");
    }

    private static async Task<ErrorEnvelope?> TryReadErrorEnvelopeAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorEnvelope>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException
            or OperationCanceledException or HttpRequestException)
        {
            return null;
        }
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
            companyId, id, FormText.Required(model.AssetNumber), model.CategoryId!.Value, FormText.Required(model.Name),
            FormText.Optional(model.Manufacturer), FormText.Optional(model.Model), FormText.Optional(model.SerialNumber),
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
        Guid companyId, AssetEditModel model)
    {
        var outcome = await CreateFromModelAsync(companyId, model, Guid.NewGuid());
        return (outcome.Kind == MutationOutcomeKind.Succeeded ? model : null, outcome.Error);
    }

    async Task<MutationOutcome<AssetEditModel>> IIdempotentCreateService<AssetEditModel>.CreateAsync(
        Guid companyId, AssetEditModel model, Guid idempotencyKey)
    {
        var outcome = await CreateFromModelAsync(companyId, model, idempotencyKey);
        return outcome.Kind switch
        {
            MutationOutcomeKind.Succeeded => MutationOutcome<AssetEditModel>.Succeeded(model),
            MutationOutcomeKind.Rejected => MutationOutcome<AssetEditModel>.Rejected(outcome.Error!),
            _ => MutationOutcome<AssetEditModel>.Ambiguous(outcome.Error!),
        };
    }

    // Bug fix (P1 follow-up to Ticket 19): the single source of truth for "what does the outgoing
    // create request look like for this model" - used both to build the actual HTTP request body
    // AND (via BuildRequestSnapshot below) to fingerprint the idempotency key. Keeping one mapper
    // means the fingerprint can never drift from the real request.
    private static CreateAssetRequest BuildCreateRequest(Guid companyId, AssetEditModel model) => new(
        companyId, FormText.Required(model.AssetNumber), model.CategoryId!.Value, FormText.Required(model.Name),
        FormText.Optional(model.Manufacturer), FormText.Optional(model.Model), FormText.Optional(model.SerialNumber),
        model.PurchaseDate, model.PurchasePrice);

    // Bug fix (P1 follow-up to Ticket 19): exposes the exact normalized request DTO that will be
    // sent over HTTP so EditPageBase can fingerprint it instead of the raw (un-normalized) edit
    // model - see IIdempotentCreateService<TModel>.BuildRequestSnapshot for the full rationale.
    object IIdempotentCreateService<AssetEditModel>.BuildRequestSnapshot(Guid companyId, AssetEditModel model) =>
        BuildCreateRequest(companyId, model);

    private async Task<MutationOutcome<CreateAssetResponse>> CreateFromModelAsync(
        Guid companyId, AssetEditModel model, Guid idempotencyKey)
    {
        var request = BuildCreateRequest(companyId, model);

        return await CreateAssetAsync(companyId, request, idempotencyKey);
    }

    async Task<(AssetEditModel? Result, string? Error)> IEditService<AssetEditModel, Guid>.UpdateAsync(
        Guid companyId, Guid id, AssetEditModel model)
    {
        var request = new UpdateAssetRequest(
            companyId, id, FormText.Required(model.AssetNumber), model.CategoryId!.Value, FormText.Required(model.Name),
            FormText.Optional(model.Manufacturer), FormText.Optional(model.Model), FormText.Optional(model.SerialNumber),
            model.PurchaseDate, model.PurchasePrice);

        var (updated, error) = await UpdateAssetAsync(companyId, id, request);
        return (updated is null ? null : model, error);
    }

    private sealed record ErrorEnvelope(string? Error, string? Code = null);
}
