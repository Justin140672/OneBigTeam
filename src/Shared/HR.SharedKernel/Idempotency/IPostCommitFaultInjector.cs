namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) final gap item 5: a deliberately tiny, cross-cutting testability seam — analogous to
/// <see cref="IClock"/> — that lets an integration test reproduce "the business transaction committed,
/// then the HTTP response never completed successfully" (e.g. a 500/502/503/504, or a dropped
/// connection) for a specific idempotent mutation, without any production code path ever depending on
/// test infrastructure.
///
/// Handlers that persist an idempotent mutation (e.g. AdjustLeaveBalanceHandler, CreateAssetHandler)
/// call <see cref="MaybeFailAfterCommitAsync"/> immediately AFTER their transaction/SaveChangesAsync
/// has committed, but before returning their success <c>Result</c>. In production this is always a
/// no-op (<see cref="NoOpPostCommitFaultInjector"/>). A test replaces the DI registration with a
/// fault-injecting double that throws exactly once for a given idempotency key, simulating a 500
/// AFTER commit — the exact scenario Ticket 3's final gap exists to make safe to retry.
/// </summary>
public interface IPostCommitFaultInjector
{
    Task MaybeFailAfterCommitAsync(string operationName, string? idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>Production default: never fails. See <see cref="IPostCommitFaultInjector"/>.</summary>
public sealed class NoOpPostCommitFaultInjector : IPostCommitFaultInjector
{
    public Task MaybeFailAfterCommitAsync(string operationName, string? idempotencyKey, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
