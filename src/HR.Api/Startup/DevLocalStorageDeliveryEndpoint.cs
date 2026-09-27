using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.StaticFiles;

namespace HR.Api.Startup;

/// <summary>
/// Development-only delivery route for the <c>Local*StorageService</c> temp-directory fallbacks
/// (<c>GET /api/dev/local-storage/{bucket}/{*key}?exp=…&amp;sig=…</c>).
///
/// Previously this streamed any file from bucket + key alone, anonymously, bypassing the normal
/// document / candidate-CV / profile-photo download handlers (no entitlement, tenant, record or
/// malware-scan checks). It now makes the same decisions as production:
/// <list type="number">
/// <item>The URL must carry a valid, unexpired HMAC signature over bucket + exact key + expiry
///   (<see cref="ILocalStorageUrlSigner"/>). Signatures are only minted by a storage service's
///   <c>GetDownloadUrlAsync</c>, which is only reached after the authenticated handler has authorised
///   the caller (tenant + permission) and checked the file is Clean, the local equivalent of a
///   Supabase signed URL. A raw or copied storage key without that signature is never served, and a
///   signature cannot be moved onto another key, bucket or expiry.</item>
/// <item>The owning module's <see cref="ILocalStorageObjectResolver"/> re-confirms against the
///   database, at fetch time, that the key still belongs to a live record that is Clean. Orphaned
///   files and Pending/Scanning/Infected/Failed files are refused even when the file exists on disk.
///   A bucket with no resolver is always refused (fail closed).</item>
/// <item>Path-traversal defences are kept: strict segment validation (including double-encoded
///   forms) plus a canonical, ordinal containment check against the bucket root.</item>
/// </list>
/// Every refusal is an indistinguishable 404, so the route never confirms whether a key exists.
/// Mapped only in Development; production Supabase storage never produces these URLs.
/// </summary>
internal static class DevLocalStorageDeliveryEndpoint
{
    private const int MaxKeyLength = 1024;

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static void MapDevLocalStorageDelivery(this WebApplication app)
    {
        app.MapGet($"{LocalStorageBuckets.RoutePrefix}/{{bucket}}/{{*key}}", HandleAsync).AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        string bucket,
        string? key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!LocalStorageBuckets.TryGetRootPath(bucket, out var rootPath))
            return Results.NotFound();

        if (key is null || !IsWellFormedKey(key))
            return Results.NotFound();

        var query = httpContext.Request.Query;
        if (query["exp"].Count != 1 || query["sig"].Count != 1)
            return Results.NotFound();

        var signer = httpContext.RequestServices.GetService<ILocalStorageUrlSigner>();
        if (signer is null || !signer.IsValid(bucket, key, query["exp"].ToString(), query["sig"].ToString()))
            return Results.NotFound();

        var resolver = httpContext.RequestServices.GetServices<ILocalStorageObjectResolver>()
            .FirstOrDefault(r => string.Equals(r.Bucket, bucket, StringComparison.Ordinal));
        if (resolver is null || !await resolver.IsServableAsync(key, cancellationToken))
            return Results.NotFound();

        var fullPath = ResolveContainedPath(rootPath, key);
        if (fullPath is null || !File.Exists(fullPath))
            return Results.NotFound();

        if (!ContentTypeProvider.TryGetContentType(fullPath, out var contentType))
            contentType = "application/octet-stream";

        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(fullPath, contentType);
    }

    /// <summary>
    /// The route value has already been URL-decoded once by routing (except %2F), and generated
    /// storage keys round-trip through that exactly, so the key is used verbatim. Each segment is
    /// also checked after one further decode so double-encoded traversal (%252e%252e) and encoded
    /// separators are rejected rather than treated as literal names.
    /// </summary>
    internal static bool IsWellFormedKey(string key)
    {
        if (key.Length == 0 || key.Length > MaxKeyLength || Path.IsPathRooted(key))
            return false;

        foreach (var segment in key.Split('/'))
        {
            if (!IsSafeSegment(segment))
                return false;

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(segment);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!IsSafeSegment(decoded) || decoded.Contains('/'))
                return false;
        }

        return true;
    }

    private static bool IsSafeSegment(string segment) =>
        segment.Length > 0
        && segment is not ("." or "..")
        && !segment.Contains('\\')
        && !segment.Contains(':')
        && !segment.Any(char.IsControl);

    private static string? ResolveContainedPath(string rootPath, string key)
    {
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, string.Join(Path.DirectorySeparatorChar, key.Split('/'))));

        var rootWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        // Ordinal (not OrdinalIgnoreCase): on a case-sensitive filesystem (Linux) a case-insensitive
        // prefix check would let a differently-cased sibling directory pass as "contained".
        return fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? fullPath : null;
    }
}
