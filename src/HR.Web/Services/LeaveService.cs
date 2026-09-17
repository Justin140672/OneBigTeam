using System.Text.Json;
using HR.SharedKernel.Http;
using HR.SharedKernel.Idempotency;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class LeaveService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<bool> CancelLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        Guid leaveRequestId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.DeleteAsync($"api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}", ct),
            cancellationToken: cancellationToken);
        return result.Success;
    }

    public async Task<LeaveRequestListResponse?> ListLeaveRequestsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<LeaveRequestListResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/employees/{employeeId}/leave-requests", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<GetRecentLeaveRequestsResponse?> GetRecentLeaveRequestsAsync(
        Guid companyId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetRecentLeaveRequestsResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/leave-requests/recent?take={take}", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    // DSH-03: non-swallowing sibling of GetRecentLeaveRequestsAsync.
    public Task<GetRecentLeaveRequestsResponse?> GetRecentLeaveRequestsOrThrowAsync(
        Guid companyId, int take = 10, CancellationToken cancellationToken = default) =>
        Http.GetFromJsonAsync<GetRecentLeaveRequestsResponse>(
            $"api/companies/{companyId}/leave-requests/recent?take={take}",
            HrApiJsonOptions.Default, cancellationToken);

    public async Task<GetLeaveRequestResponse?> GetLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        Guid leaveRequestId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<GetLeaveRequestResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<LeaveBalanceResponse?> GetEmployeeLeaveBalanceAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var result = await ApiResponseReader.ExecuteAsync<LeaveBalanceResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={year}", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }

    public async Task<(PreviewLeaveResponse? Response, string? Error)> PreviewLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        PreviewLeaveRequestModel request,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<PreviewLeaveResponse>(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests/preview",
                request, HrApiJsonOptions.Default, ct),
            HrApiJsonOptions.Default, cancellationToken);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Unable to calculate preview."));
    }

    public async Task<(SubmitLeaveResponse? Response, string? Error)> SubmitLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        SubmitLeaveRequestModel request,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<SubmitLeaveResponse>(
            ct => Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests",
                request, HrApiJsonOptions.Default, ct),
            HrApiJsonOptions.Default, cancellationToken);
        return (result.Value, result.Success ? null : (result.DisplayMessage ?? "Failed to submit leave request."));
    }

    /// <summary>
    /// Ticket 3 (P1) final gap: stateless with respect to operation identity - the caller (the
    /// component/dialog that owns this one logical submission) supplies
    /// <paramref name="idempotencyKey"/> and decides whether to reuse or rotate it. Returns a
    /// <see cref="MutationOutcome{T}"/> so the caller can distinguish a definitive outcome (Succeeded
    /// or Rejected — safe to discard the key) from an AmbiguousFailure (5xx/408/429/transport
    /// failure/timeout/cancellation-after-dispatch/malformed success body — the key and request must
    /// be retained so an unchanged retry replays rather than repeats the adjustment). This method
    /// deliberately never lets an exception escape — every ambiguous case is captured and returned
    /// as <see cref="MutationOutcomeKind.AmbiguousFailure"/> instead.
    /// </summary>
    public async Task<MutationOutcome<AdjustLeaveBalanceResponse>> AdjustLeaveBalanceAsync(
        Guid companyId,
        Guid employeeId,
        AdjustLeaveBalanceModel request,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await Http.PostAsJsonIdempotentAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
                request, idempotencyKey, HrApiJsonOptions.Default, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Client-side request timeout — the request was already dispatched, so the server may
            // have received and committed it.
            return MutationOutcome<AdjustLeaveBalanceResponse>.Ambiguous(
                "The request timed out. It's safe to try again — the balance will not be adjusted twice.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested by the caller after the request may already be in flight — we
            // cannot prove it was never sent, so this stays ambiguous rather than assumed abandoned.
            return MutationOutcome<AdjustLeaveBalanceResponse>.Ambiguous(
                "The request was cancelled before a response was received.");
        }
        catch (HttpRequestException)
        {
            return MutationOutcome<AdjustLeaveBalanceResponse>.Ambiguous(
                "A network error occurred. It's safe to try again — the balance will not be adjusted twice.");
        }

        // Bug fix (P1 follow-up to Ticket 3): sending the request and reading its response body are
        // ONE operation as far as idempotency is concerned. Previously only JsonException was
        // caught while parsing the success body, so a cancellation, response-stream I/O failure, or
        // dropped connection while READING the body (as opposed to sending) would escape uncaught,
        // bypass AmbiguousFailure classification, and propagate up into the caller/dialog —
        // abandoning the retained idempotency key/snapshot instead of preserving it for a safe
        // retry. The response is always disposed once classification/body-reading is complete.
        using var _ = httpResponse;

        var kind = MutationHttpClassifier.ClassifyStatusCode(httpResponse.StatusCode);

        if (kind == MutationOutcomeKind.Succeeded)
        {
            try
            {
                var response = await httpResponse.Content.ReadFromJsonAsync<AdjustLeaveBalanceResponse>(HrApiJsonOptions.Default, cancellationToken);
                if (response is null)
                    return MutationOutcome<AdjustLeaveBalanceResponse>.Ambiguous(
                        "The server's response could not be read. It's safe to try again.");

                return MutationOutcome<AdjustLeaveBalanceResponse>.Succeeded(response);
            }
            catch (Exception ex) when (ex is JsonException or IOException
                or OperationCanceledException or HttpRequestException)
            {
                // Invalid/truncated success response, cancellation, or a transport failure while
                // reading the body — the mutation may well have committed, but this client cannot
                // confirm it from this response body.
                return MutationOutcome<AdjustLeaveBalanceResponse>.Ambiguous(
                    "The server's response could not be read. It's safe to try again.");
            }
        }

        if (kind == MutationOutcomeKind.AmbiguousFailure)
        {
            return MutationOutcome<AdjustLeaveBalanceResponse>.Ambiguous(
                "The request could not be confirmed. It's safe to try again — the balance will not be adjusted twice.");
        }

        // Definitive rejection from here on — the status code alone already confirms that, so a
        // failure reading the (secondary) error body must not itself be treated as ambiguous;
        // fall back to a generic message instead.
        if (httpResponse.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            try
            {
                var validationBody = await httpResponse.Content.ReadFromJsonAsync<ApiValidationEnvelope>(cancellationToken);
                var first = validationBody?.Errors?.Values.SelectMany(v => v).FirstOrDefault();
                return MutationOutcome<AdjustLeaveBalanceResponse>.Rejected(first ?? "Validation failed.");
            }
            catch (Exception ex) when (ex is JsonException or IOException
                or OperationCanceledException or HttpRequestException)
            {
                return MutationOutcome<AdjustLeaveBalanceResponse>.Rejected("Validation failed.");
            }
        }

        try
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var msg = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return MutationOutcome<AdjustLeaveBalanceResponse>.Rejected(msg ?? "Failed to adjust leave balance.");
        }
        catch (Exception ex) when (ex is JsonException or IOException
            or OperationCanceledException or HttpRequestException)
        {
            return MutationOutcome<AdjustLeaveBalanceResponse>.Rejected("Failed to adjust leave balance.");
        }
    }

    public async Task<LeaveBalanceHistoryResponse?> GetLeaveBalanceHistoryAsync(
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<LeaveBalanceHistoryResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/employees/{employeeId}/leave-types/{leaveTypeId}/balance-history", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.Success ? result.Value : null;
    }
}
