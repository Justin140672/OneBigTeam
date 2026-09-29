using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HR.SharedKernel.Http;

public static class ApiResponseReader
{
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

            return ApiResult<T>.Fail(ApiFailureKind.InvalidResponse, "The server returned an unreadable response.");
        }

        if ((int)status >= 500)
            return ApiResult<T>.Fail(ApiFailureKind.Server, errorEnvelope?.Error ?? $"The server encountered an error ({(int)status}).");

        return ApiResult<T>.Fail(
            ApiFailureKind.Server,
            errorEnvelope?.Error ?? $"Request failed ({(int)status} {status}).",
            errorEnvelope?.Code);
    }

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
            throw;
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Network, "Unable to reach the server. Please check your connection and try again.");
        }
        catch (OperationCanceledException)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Network, "The request timed out. Please try again.");
        }

        using (response)
        {
            return await ReadJsonAsync<T>(response, jsonOptions, cancellationToken);
        }
    }

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

public readonly struct Unit
{
    public static readonly Unit Value = default;
}
