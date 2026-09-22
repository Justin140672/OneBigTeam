using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Services;

internal sealed class SupabaseCandidateDocumentStorageOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SupabaseCandidateDocumentStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseCandidateDocumentStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Recruitment:Supabase:CandidateDocuments",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds,
            allowInsecureHttp: IsInsecureHttpAllowedEnvironment(environment));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsInsecureHttpAllowedEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Test")
        || string.Equals(Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
}
