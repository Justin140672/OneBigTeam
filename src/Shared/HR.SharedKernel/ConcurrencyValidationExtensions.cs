using FluentValidation;

namespace HR.SharedKernel;

/// <summary>
/// Ticket 2 item 3: a single shared rule so every protected update request enforces that a loaded
/// concurrency version was round-tripped. An omitted version must be rejected (422) rather than
/// falling through to a last-writer-wins save.
/// </summary>
public static class ConcurrencyValidationExtensions
{
    public const string MissingVersionMessage =
        "A concurrency version is required. Reload the page and try again.";

    /// <summary>
    /// Requires <paramref name="expectedVersionSelector"/> (the request's <c>ExpectedVersion</c>) to
    /// be non-null. Use on every protected Update*Validator.
    /// </summary>
    public static IRuleBuilderOptions<T, int?> RequireLoadedVersion<T>(
        this IRuleBuilder<T, int?> ruleBuilder)
        => ruleBuilder
            .NotNull()
            .WithMessage(MissingVersionMessage);
}
