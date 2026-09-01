namespace HR.Infrastructure.Storage;

internal sealed class SupabaseOrganisationDataExportStorageOptions
{
    public string SupabaseUrl { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "organisation-exports";
}
