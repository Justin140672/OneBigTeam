using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Services;

internal sealed class SupabaseImportFileStorageOptionsValidator : IValidateOptions<SupabaseImportFileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseImportFileStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "DataImport:Supabase:ImportFiles",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
