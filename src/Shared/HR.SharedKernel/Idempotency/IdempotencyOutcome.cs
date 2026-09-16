namespace HR.SharedKernel.Idempotency;

public enum IdempotencyOutcomeKind
{
    /// <summary>This call performed the mutation for the first time under this key.</summary>
    Completed,

    /// <summary>A prior call already completed this exact request; its stored result is replayed.</summary>
    Replayed,

    /// <summary>The key was already used for a request with a different fingerprint (caller bug).</summary>
    KeyReused,

    /// <summary>Ticket 14 (P2): the underlying aggregate's version no longer matched the caller's
    /// expected version — a concurrent writer saved first. Nothing committed (including no
    /// idempotency record), so the same key may be retried once the caller has reloaded and
    /// confirmed/reapplied their change. See
    /// <see cref="DbContextIdempotencyExtensions.SaveIdempotentWithConcurrencyAsync{TRecord,TAggregate,TResponse}"/>.</summary>
    ConcurrencyConflict,
}

public sealed record IdempotencyOutcome<TResponse>(IdempotencyOutcomeKind Kind, int StatusCode, TResponse? Response)
{
    public static IdempotencyOutcome<TResponse> Completed(int statusCode, TResponse response) =>
        new(IdempotencyOutcomeKind.Completed, statusCode, response);

    public static IdempotencyOutcome<TResponse> Replayed(int statusCode, TResponse response) =>
        new(IdempotencyOutcomeKind.Replayed, statusCode, response);

    public static IdempotencyOutcome<TResponse> KeyReused() =>
        new(IdempotencyOutcomeKind.KeyReused, 0, default);

    public static IdempotencyOutcome<TResponse> ConcurrencyConflict() =>
        new(IdempotencyOutcomeKind.ConcurrencyConflict, 0, default);
}
