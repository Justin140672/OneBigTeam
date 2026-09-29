using System.Text.Json;

namespace HR.Infrastructure.Abstractions;

public static class SupabaseStorageBucketCheck
{
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
