using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

internal sealed class SupabaseOrganisationDataExportStorageOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SupabaseOrganisationDataExportStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseOrganisationDataExportStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Infrastructure:Supabase:OrganisationExports",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName,
            allowInsecureHttp: IsInsecureHttpAllowedEnvironment(environment));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsInsecureHttpAllowedEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Test")
        || string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
}
