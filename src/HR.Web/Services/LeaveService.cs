using System.Text.Json;
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
        try
        {
            var response = await Http.DeleteAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LeaveRequestListResponse?> ListLeaveRequestsAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<LeaveRequestListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GetRecentLeaveRequestsResponse?> GetRecentLeaveRequestsAsync(
        Guid companyId,
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<GetRecentLeaveRequestsResponse>(
                $"api/companies/{companyId}/leave-requests/recent?take={take}",
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
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
        try
        {
            return await Http.GetFromJsonAsync<GetLeaveRequestResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests/{leaveRequestId}",
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LeaveBalanceResponse?> GetEmployeeLeaveBalanceAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var year = DateTime.UtcNow.Year;
            return await Http.GetFromJsonAsync<LeaveBalanceResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leave-balances?policyYear={year}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(PreviewLeaveResponse? Response, string? Error)> PreviewLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        PreviewLeaveRequestModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpResponse = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests/preview",
                request, HrApiJsonOptions.Default, cancellationToken);

            if (httpResponse.IsSuccessStatusCode)
                return (await httpResponse.Content.ReadFromJsonAsync<PreviewLeaveResponse>(HrApiJsonOptions.Default, cancellationToken), null);

            return (null, "Unable to calculate preview.");
        }
        catch (TaskCanceledException)
        {
            return (null, null);
        }
        catch
        {
            return (null, "Unable to calculate preview.");
        }
    }

    public async Task<(SubmitLeaveResponse? Response, string? Error)> SubmitLeaveRequestAsync(
        Guid companyId,
        Guid employeeId,
        SubmitLeaveRequestModel request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpResponse = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/leave-requests",
                request, HrApiJsonOptions.Default, cancellationToken);

            if (httpResponse.IsSuccessStatusCode)
                return (await httpResponse.Content.ReadFromJsonAsync<SubmitLeaveResponse>(HrApiJsonOptions.Default, cancellationToken), null);

            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(body);
                var msg = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                return (null, msg ?? "Failed to submit leave request.");
            }
            catch
            {
                return (null, "Failed to submit leave request.");
            }
        }
        catch
        {
            return (null, "Failed to submit leave request.");
        }
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
                var validationBody = await httpResponse.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken);
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
        try
        {
            return await Http.GetFromJsonAsync<LeaveBalanceHistoryResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/leave-types/{leaveTypeId}/balance-history",
                HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private sealed record ValidationErrorEnvelope(Dictionary<string, string[]>? Errors);
}
