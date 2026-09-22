using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

internal sealed class SupabaseSupportAttachmentStorageOptionsValidator : IValidateOptions<SupabaseSupportAttachmentStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseSupportAttachmentStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Infrastructure:Supabase:SupportAttachments",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
