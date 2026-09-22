using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Services;

internal sealed class SupabaseImportFileStorageOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SupabaseImportFileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseImportFileStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "DataImport:Supabase:ImportFiles",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds,
            allowInsecureHttp: IsInsecureHttpAllowedEnvironment(environment));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsInsecureHttpAllowedEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Test")
        || string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
}
