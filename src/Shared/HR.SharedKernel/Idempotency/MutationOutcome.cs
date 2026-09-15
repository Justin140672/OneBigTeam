namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) final follow-up: how certain a client can be about what happened to a mutation it
/// just attempted. This is deliberately a three-way split, not the two-way "did I get a response or
/// not" split the client code used before this follow-up:
///
/// - <see cref="Succeeded"/> and <see cref="Rejected"/> are both DEFINITIVE — the server has told
///   the client, unambiguously, what happened (the mutation went through, or it was refused for a
///   reason the user must act on). Either way, this logical operation is over: the caller should
///   discard its idempotency key so the NEXT submission (even of an identical payload) is treated as
///   a new operation.
/// - <see cref="AmbiguousFailure"/> means the client cannot tell whether the mutation committed
///   server-side before the failure occurred (an HTTP 500 can happen AFTER the business transaction
///   already committed). The caller MUST retain its idempotency key and the exact request payload so
///   an unchanged retry replays the server's stored result rather than repeating the mutation.
/// </summary>
public enum MutationOutcomeKind
{
    Succeeded,
    Rejected,
    AmbiguousFailure,
}

/// <summary>
/// Ticket 3 (P1) final follow-up: the outcome of a single idempotent mutation attempt, carrying
/// enough information for the caller to decide whether to discard or retain its idempotency key —
/// see <see cref="MutationOutcomeKind"/> for the rule. <see cref="Value"/> is only populated for
/// <see cref="MutationOutcomeKind.Succeeded"/>; <see cref="Error"/> is a user-facing message for
/// either failure kind.
/// </summary>
public sealed record MutationOutcome<T>(MutationOutcomeKind Kind, T? Value, string? Error)
{
    public bool IsDefinitive => Kind is MutationOutcomeKind.Succeeded or MutationOutcomeKind.Rejected;

    public static MutationOutcome<T> Succeeded(T value) =>
        new(MutationOutcomeKind.Succeeded, value, null);

    public static MutationOutcome<T> Rejected(string error) =>
        new(MutationOutcomeKind.Rejected, default, error);

    public static MutationOutcome<T> Ambiguous(string error) =>
        new(MutationOutcomeKind.AmbiguousFailure, default, error);
}
