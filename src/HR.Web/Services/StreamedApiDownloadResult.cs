using System.Net.Http.Headers;

namespace HR.Web.Services;

/// <summary>
/// Streams an upstream API download straight through to the browser: headers are copied, the body
/// is forwarded in bounded chunks, and the upstream response is disposed when the copy finishes,
/// fails or the browser disconnects, so memory use is independent of the file size.
/// </summary>
public sealed class StreamedApiDownloadResult(HttpResponseMessage upstream) : IResult
{
    private const int CopyBufferSize = 81920;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        using var response = upstream;
        var target = httpContext.Response;

        target.StatusCode = StatusCodes.Status200OK;
        target.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        target.Headers.ContentDisposition = response.Content.Headers.ContentDisposition?.ToString()
            ?? new ContentDispositionHeaderValue("attachment").ToString();
        target.Headers.CacheControl = "private, no-store";
        if (response.Content.Headers.ContentLength is { } length)
        {
            target.ContentLength = length;
        }

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(httpContext.RequestAborted);
            await target.StartAsync(httpContext.RequestAborted);
            await source.CopyToAsync(target.Body, CopyBufferSize, httpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }
    }
}
