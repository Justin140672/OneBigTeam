using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

internal sealed class SupabaseSupportAttachmentStorageOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SupabaseSupportAttachmentStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseSupportAttachmentStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Infrastructure:Supabase:SupportAttachments",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds,
            allowInsecureHttp: IsInsecureHttpAllowedEnvironment(environment));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsInsecureHttpAllowedEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Test")
        || string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
}
