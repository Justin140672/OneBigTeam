namespace HR.Modules.Documents.Services;

/// <summary>
/// Connection settings for a clamd (ClamAV daemon) instance, spoken over the INSTREAM TCP
/// protocol. Follows the same options-pattern convention as SupabaseStorageOptions — bound from
/// configuration ("Documents:ClamAv") and only registered when that section is present, the same
/// Supabase-vs-local switch AddStorageService already uses.
/// </summary>
internal sealed class ClamAvOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 30;
}
