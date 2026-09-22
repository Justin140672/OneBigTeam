using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Services;

internal sealed class SupabaseCandidateDocumentStorageOptionsValidator : IValidateOptions<SupabaseCandidateDocumentStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseCandidateDocumentStorageOptions options)
    {
        var failures = SupabaseStorageOptionsValidation.Validate(
            "Recruitment:Supabase:CandidateDocuments",
            options.SupabaseUrl, options.ServiceRoleKey, options.BucketName, options.SignedUrlExpirySeconds);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
