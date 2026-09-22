using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

internal sealed class SupabaseProfilePhotoStorageOptionsValidator : IValidateOptions<SupabaseProfilePhotoStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseProfilePhotoStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Infrastructure:Supabase:ProfilePhotos",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
