using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

internal sealed class SupabaseStorageOptionsValidator : IValidateOptions<SupabaseStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Documents:Supabase",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
