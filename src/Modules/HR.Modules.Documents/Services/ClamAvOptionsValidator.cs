using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

internal sealed class ClamAvOptionsValidator : IValidateOptions<ClamAvOptions>
{
    public ValidateOptionsResult Validate(string? name, ClamAvOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
            failures.Add("Documents:ClamAv:Host is required.");

        if (options.Port is < 1 or > 65535)
            failures.Add("Documents:ClamAv:Port must be between 1 and 65535.");

        if (options.TimeoutSeconds <= 0)
            failures.Add("Documents:ClamAv:TimeoutSeconds must be a positive, bounded number of seconds.");
        else if (options.TimeoutSeconds > 600)
            failures.Add("Documents:ClamAv:TimeoutSeconds must not exceed 600 seconds.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
