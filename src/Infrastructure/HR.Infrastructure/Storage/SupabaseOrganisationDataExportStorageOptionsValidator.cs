using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

internal sealed class SupabaseOrganisationDataExportStorageOptionsValidator : IValidateOptions<SupabaseOrganisationDataExportStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseOrganisationDataExportStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Infrastructure:Supabase:OrganisationExports",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
