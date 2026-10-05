using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Domain;

/// <summary>
/// The canonical, normalized business content of a task-completion request. Every business-significant
/// input must be a property of this record: <see cref="Fingerprint"/> serializes the whole record, so a
/// property added here is automatically part of command equivalence and of the persisted fingerprint.
/// Normalization: values are trimmed and blank becomes null; decisions are compared case-sensitively
/// because every completion action matches decisions ordinally; reasons keep their case and inner text.
/// The command is the single source of canonical values: persistence, dispatch and fingerprints must all
/// read <see cref="Decision"/>/<see cref="Reason"/> from it, never from the raw request.
/// Command fingerprint = semantic business equivalence for a task; the HTTP request fingerprint is a
/// separate idempotent-delivery concern that incorporates this command plus request scope.
/// </summary>
internal sealed record TaskCompletionCommand(Guid TaskId, string? Decision, string? Reason)
{
    private const string FingerprintVersion = "tcc1";

    public const int MaxDecisionLength = 200;
    public const int MaxReasonLength = 2000;

    public static TaskCompletionCommand Create(Guid taskId, string? decision, string? reason) =>
        new(taskId, Normalize(decision), Normalize(reason));

    public const string InvalidCommandCode = "validation.invalid_command";

    public Error? Validate()
    {
        if (Decision is { Length: > MaxDecisionLength })
            return new Error(InvalidCommandCode, $"The decision must be {MaxDecisionLength} characters or fewer.");

        if (Reason is { Length: > MaxReasonLength })
            return new Error(InvalidCommandCode, $"The reason must be {MaxReasonLength} characters or fewer.");

        return null;
    }

    public string Fingerprint()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(this);
        return FingerprintVersion + ":" + Convert.ToHexString(SHA256.HashData(payload));
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
