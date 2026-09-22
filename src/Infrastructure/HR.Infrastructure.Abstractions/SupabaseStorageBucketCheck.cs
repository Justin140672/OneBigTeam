using System.Text.Json;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Security review finding 6: every *StorageHealthCheck across the app (documents, profile photos,
/// support attachments, organisation exports, candidate documents, import files) previously reported
/// healthy the moment Supabase Storage's "list buckets" call returned any 2xx response — it never
/// looked at which buckets were actually returned. A prod instance with the wrong bucket name
/// configured (or a bucket that was renamed/deleted) would pass startup/readiness checks while every
/// real upload failed with a missing-bucket error. This is a pure, stateless parsing routine shared by
/// all six health checks so the "does my configured bucket exist" logic is written once; each health
/// check still owns its own HTTP call, options type and messaging.
/// </summary>
public static class SupabaseStorageBucketCheck
{
    /// <summary>
    /// Parses a Supabase Storage "list buckets" (GET /storage/v1/bucket) response body and returns
    /// whether <paramref name="bucketName"/> is present, matching on either the bucket "id" or "name"
    /// field (Supabase buckets are usually created with id == name, but only "id" is guaranteed to be
    /// the value used in storage object paths). Returns false — never throws — for a malformed/
    /// unexpected body, so callers can uniformly treat "bucket not confirmed present" as unhealthy.
    /// </summary>
    public static bool ContainsBucket(string responseBody, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(responseBody) || string.IsNullOrWhiteSpace(bucketName))
            return false;

        try
        {
            using var document = JsonDocument.Parse(responseBody);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                if (TryGetString(element, "id", out var id) && string.Equals(id, bucketName, StringComparison.Ordinal))
                    return true;

                if (TryGetString(element, "name", out var name) && string.Equals(name, bucketName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return value is not null;
        }

        value = null;
        return false;
    }
}
