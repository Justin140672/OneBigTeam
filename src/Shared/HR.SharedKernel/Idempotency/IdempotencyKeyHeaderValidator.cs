using System.Text;
using FastEndpoints;
using FluentValidation.Results;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) follow-up item 4: one shared validator for the "Idempotency-Key" header, applied to
/// every endpoint as a FastEndpoints global pre-processor (see <c>app.UseFastEndpoints</c> in
/// Program.cs) - so an endpoint never has to (and never accidentally forgets to) validate the header
/// itself before reading it.
///
/// The header is OPTIONAL - an endpoint that doesn't support idempotency, or a caller not requesting
/// it, simply omits it. When present, it must be a single, non-blank, control-character-free value
/// within the column length limit. A caller sending multiple header instances gets rejected rather
/// than having them silently comma-joined into a synthetic key.
///
/// A failure here is reported as a normal FluentValidation failure, so it rides the app's existing
/// validation-error pipeline (422, per this API's <c>Errors.StatusCode</c> configuration) rather than
/// introducing a second, inconsistent error shape.
/// </summary>
public sealed class IdempotencyKeyHeaderValidator : IGlobalPreProcessor
{
    public const string HeaderName = "Idempotency-Key";

    // Matches the column length in IdempotencyRecordConfiguration - a key that can't fit can never
    // be looked up or stored, so reject it here rather than failing later inside the handler.
    private const int MaxLengthBytes = 200;

    public Task PreProcessAsync(IPreProcessorContext context, CancellationToken cancellationToken)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
            return Task.CompletedTask;

        if (values.Count > 1)
        {
            Fail(context, "Multiple Idempotency-Key header values were supplied. Send exactly one.");
            return Task.CompletedTask;
        }

        var value = values[0];

        if (string.IsNullOrWhiteSpace(value))
        {
            Fail(context, "Idempotency-Key must not be blank.");
            return Task.CompletedTask;
        }

        if (Encoding.UTF8.GetByteCount(value) > MaxLengthBytes)
        {
            Fail(context, $"Idempotency-Key must not exceed {MaxLengthBytes} bytes.");
            return Task.CompletedTask;
        }

        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                Fail(context, "Idempotency-Key must not contain control characters.");
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }

    private static void Fail(IPreProcessorContext context, string message) =>
        context.ValidationFailures.Add(new ValidationFailure(HeaderName, message));
}
