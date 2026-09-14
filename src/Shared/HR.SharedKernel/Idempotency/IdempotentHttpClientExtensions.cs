using System.Net.Http.Json;
using System.Text.Json;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) follow-up items 1/2: the one place that knows how to attach a validated
/// "Idempotency-Key" header to an outgoing request, so callers (HR.Web/HR.Admin.Web/HR.Marketing
/// service methods) never hand-roll header code themselves - they only own the key's lifecycle (via
/// <see cref="IdempotencyKeyScope"/>), not its wire format.
///
/// Works from a plain <see cref="HttpContent"/> so it covers JSON today and multipart later without
/// buffering: a multipart caller builds its own streamed <c>MultipartFormDataContent</c> (file parts
/// included) exactly as it would without this header, then passes that same content through
/// <see cref="SendIdempotentAsync"/> - nothing here reads or re-wraps the content body.
/// </summary>
public static class IdempotentHttpClientExtensions
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>
    /// Sends <paramref name="content"/> (JSON, multipart, or any other <see cref="HttpContent"/>)
    /// with <paramref name="idempotencyKey"/> attached as the Idempotency-Key header.
    /// </summary>
    public static Task<HttpResponseMessage> SendIdempotentAsync(
        this HttpClient client,
        HttpMethod method,
        string requestUri,
        HttpContent? content,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, requestUri) { Content = content };
        request.Headers.Add(HeaderName, idempotencyKey.ToString());
        return client.SendAsync(request, cancellationToken);
    }

    /// <summary>JSON convenience wrapper over <see cref="SendIdempotentAsync"/> for POST.</summary>
    public static Task<HttpResponseMessage> PostAsJsonIdempotentAsync<TValue>(
        this HttpClient client,
        string requestUri,
        TValue value,
        Guid idempotencyKey,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.SendIdempotentAsync(
            HttpMethod.Post, requestUri, JsonContent.Create(value, options: options), idempotencyKey, cancellationToken);

    /// <summary>JSON convenience wrapper over <see cref="SendIdempotentAsync"/> for PUT.</summary>
    public static Task<HttpResponseMessage> PutAsJsonIdempotentAsync<TValue>(
        this HttpClient client,
        string requestUri,
        TValue value,
        Guid idempotencyKey,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.SendIdempotentAsync(
            HttpMethod.Put, requestUri, JsonContent.Create(value, options: options), idempotencyKey, cancellationToken);
}
