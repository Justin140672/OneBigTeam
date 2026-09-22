namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Reliability review issue 3 (P1): shared validation rules for every Supabase-backed storage
/// options class across the app (documents, profile photos, support attachments, organisation
/// exports, candidate documents, import files). Startup previously only checked that
/// <c>SupabaseUrl</c> was non-empty — <c>ServiceRoleKey</c>/<c>BucketName</c>/URL-syntax/expiry
/// were never validated, so a broken config (e.g. a malformed URL, or a blank service-role key)
/// could pass registration and only fail on the first real upload/download. This is a pure,
/// stateless validation routine (not a business service) shared to avoid duplicating the same six
/// checks across every module that owns a Supabase storage options class; each option class still
/// keeps its own dedicated <c>IValidateOptions&lt;T&gt;</c> implementation and its own
/// module-owned options type — this helper only centralises the rule logic.
///
/// Error messages name the configuration key that is wrong but never include the value itself,
/// so a malformed or leaked-looking secret is never echoed into logs/console output.
/// </summary>
public static class SupabaseStorageOptionsValidation
{
    /// <summary>
    /// Security review finding 6: the Supabase storage base URL carries the highly-privileged
    /// service-role key (sent as the "apikey"/"Authorization" header on every request). Plain HTTP
    /// would put that key on the wire in cleartext. HTTP is only tolerated when
    /// <paramref name="allowInsecureHttp"/> is true — callers must only pass true for a local
    /// Development or automated-test environment (mirroring the existing
    /// IsLocalStorageAllowedEnvironment dev/test-only convention used for the local storage
    /// fallback). Staging/Production must always pass false, so a plaintext URL fails startup
    /// validation instead of silently shipping a credential leak.
    /// </summary>
    public static List<string> Validate(
        string sectionName,
        string? supabaseUrl,
        string? serviceRoleKey,
        string? bucketName,
        int? signedUrlExpirySeconds = null,
        bool allowInsecureHttp = false)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(supabaseUrl))
        {
            failures.Add($"{sectionName}:SupabaseUrl is required.");
        }
        else if (!Uri.TryCreate(supabaseUrl, UriKind.Absolute, out var uri))
        {
            failures.Add($"{sectionName}:SupabaseUrl must be an absolute URL.");
        }
        else if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{sectionName}:SupabaseUrl must use the http or https scheme.");
        }
        else if (uri.Scheme == Uri.UriSchemeHttp && !allowInsecureHttp)
        {
            failures.Add(
                $"{sectionName}:SupabaseUrl must use https. Plain http would send the Supabase "
                + "service-role key in cleartext; http is only permitted in Development or an "
                + "explicit automated-test environment.");
        }

        if (string.IsNullOrWhiteSpace(serviceRoleKey))
            failures.Add($"{sectionName}:ServiceRoleKey is required.");

        if (string.IsNullOrWhiteSpace(bucketName))
            failures.Add($"{sectionName}:BucketName is required.");
        else if (bucketName.Any(char.IsWhiteSpace))
            failures.Add($"{sectionName}:BucketName must not contain whitespace.");

        if (signedUrlExpirySeconds is <= 0)
            failures.Add($"{sectionName}:SignedUrlExpirySeconds must be a positive number of seconds.");

        return failures;
    }
}
