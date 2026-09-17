using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HR.SharedKernel.Http;

/// <summary>
/// Shared, typed HTTP response reader for HR.Web and HR.Admin.Web service classes. Centralises
/// status-code interpretation, error-envelope parsing and network/cancellation handling so
/// individual services stop reimplementing (and disagreeing about) this logic.
///
/// Callers are still responsible for issuing the HttpClient call itself (GET/POST/PUT/DELETE) —
/// this type only interprets the resulting HttpResponseMessage/exception.
/// </summary>
public static class ApiResponseReader
{
    /// <summary>
    /// Reads a response expected to carry a JSON body on success. On failure, classifies the
    /// response using the shared error/validation envelopes. Does not catch network or
    /// cancellation exceptions raised while sending the request — wrap the send itself with
    /// <see cref="ExecuteAsync{T}"/> if that protection is also needed.
    /// </summary>
    public static async Task<ApiResult<T>> ReadJsonAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var value = jsonOptions is not null
                    ? await response.Content.ReadFromJsonAsync<T>(jsonOptions, cancellationToken)
                    : await response.Content.ReadFromJsonAsync<T>(cancellationToken);
                return ApiResult<T>.Ok(value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException)
            {
                return ApiResult<T>.Fail(ApiFailureKind.InvalidResponse, "The server returned an unreadable response.");
            }
        }

        return await ReadFailureAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Reads a response expected to carry no meaningful body on success (e.g. 204, or a 2xx whose
    /// body isn't needed). On failure, classifies the response the same way as <see cref="ReadJsonAsync{T}"/>.
    /// </summary>
    public static async Task<ApiResult<Unit>> ReadNoContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
            return ApiResult<Unit>.Ok(Unit.Value);

        return await ReadFailureAsync<Unit>(response, cancellationToken);
    }

    private static async Task<ApiResult<T>> ReadFailureAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // Caller-driven cancellation takes priority over failure classification — a cancelled
        // operation is never reported as a failed API call. Checked explicitly because content
        // already buffered in memory (e.g. in tests) may not itself observe the token.
        cancellationToken.ThrowIfCancellationRequested();

        var status = response.StatusCode;

        if (status == HttpStatusCode.Unauthorized)
            return ApiResult<T>.Fail(ApiFailureKind.Unauthenticated, "Your session has expired. Please sign in again.");

        if (status == HttpStatusCode.Forbidden)
            return ApiResult<T>.Fail(ApiFailureKind.Forbidden, "You do not have permission to perform this action.");

        string raw;
        try
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        var errorEnvelope = TryDeserialize<ApiErrorEnvelope>(raw);
        var validationEnvelope = TryDeserialize<ApiValidationEnvelope>(raw);
        var validationErrors = validationEnvelope?.Errors is { Count: > 0 } errs
            ? (IReadOnlyDictionary<string, string[]>)errs
            : null;

        if (status == HttpStatusCode.NotFound)
            return ApiResult<T>.Fail(ApiFailureKind.NotFound, errorEnvelope?.Error ?? "The requested item could not be found.");

        if (status == HttpStatusCode.Conflict)
        {
            var isConcurrency = errorEnvelope?.Code == "concurrency";
            return ApiResult<T>.Fail(
                isConcurrency ? ApiFailureKind.Concurrency : ApiFailureKind.Conflict,
                errorEnvelope?.Error ?? "A conflict occurred.",
                errorEnvelope?.Code);
        }

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            if (validationErrors is not null)
                return ApiResult<T>.Fail(ApiFailureKind.Validation, null, null, validationErrors);

            if (errorEnvelope?.Error is not null)
                return ApiResult<T>.Fail(ApiFailureKind.Validation, errorEnvelope.Error);

            // Neither envelope shape actually carried a recognisable error/validation payload —
            // the body parsed (or failed to parse) into something we cannot act on.
            return ApiResult<T>.Fail(ApiFailureKind.InvalidResponse, "The server returned an unreadable response.");
        }

        if ((int)status >= 500)
            return ApiResult<T>.Fail(ApiFailureKind.Server, errorEnvelope?.Error ?? $"The server encountered an error ({(int)status}).");

        // Any other unmapped non-success status: still surface as a controlled failure, never null.
        return ApiResult<T>.Fail(
            ApiFailureKind.Server,
            errorEnvelope?.Error ?? $"Request failed ({(int)status} {status}).",
            errorEnvelope?.Code);
    }

    /// <summary>
    /// Executes an HTTP call and interprets its result end-to-end, including network failures
    /// (HttpRequestException, TaskCanceledException caused by a client-side *timeout* rather than
    /// caller cancellation). Preserves <see cref="OperationCanceledException"/> when the supplied
    /// token was actually cancelled by the caller — that is not reported as a failed API call.
    /// </summary>
    public static async Task<ApiResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await send(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-driven cancellation (e.g. component disposal/navigation) is not an API failure.
            throw;
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Network, "Unable to reach the server. Please check your connection and try again.");
        }
        catch (OperationCanceledException)
        {
            // A timeout that did not originate from the caller's own token — treat as network failure.
            return ApiResult<T>.Fail(ApiFailureKind.Network, "The request timed out. Please try again.");
        }

        using (response)
        {
            return await ReadJsonAsync<T>(response, jsonOptions, cancellationToken);
        }
    }

    /// <summary>
    /// Executes an HTTP call expected to carry no meaningful success body (e.g. 204) and interprets
    /// its result end-to-end, including network failures — the no-content sibling of
    /// <see cref="ExecuteAsync{T}"/>. Use this instead of <c>ExecuteAsync&lt;Unit&gt;</c>, which would
    /// incorrectly try to JSON-deserialize an empty 204 body and misclassify it as InvalidResponse.
    /// </summary>
    public static async Task<ApiResult<Unit>> ExecuteNoContentAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await send(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return ApiResult<Unit>.Fail(ApiFailureKind.Network, "Unable to reach the server. Please check your connection and try again.");
        }
        catch (OperationCanceledException)
        {
            return ApiResult<Unit>.Fail(ApiFailureKind.Network, "The request timed out. Please try again.");
        }

        using (response)
        {
            return await ReadNoContentAsync(response, cancellationToken);
        }
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, DefaultJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>Marker type for <see cref="ApiResult{T}"/> when there is no meaningful success value.</summary>
public readonly struct Unit
{
    public static readonly Unit Value = default;
}
