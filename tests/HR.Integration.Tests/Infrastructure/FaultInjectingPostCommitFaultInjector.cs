using HR.SharedKernel.Idempotency;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Ticket 3 (P1) final gap item 5: test double for <see cref="IPostCommitFaultInjector"/>, registered
/// once per <see cref="ApiWebApplicationFactory"/> (see <see cref="ApiWebApplicationFactory.PostCommitFaultInjector"/>)
/// and shared across every test in the collection — exactly like <c>FakeEmailSender</c>/<c>FakeStripeGateway</c>.
///
/// A test arms it with the exact <c>operationName</c> (e.g. <c>nameof(AdjustLeaveBalanceHandler)</c> for the
/// POST-commit case, or <c>$"{nameof(AdjustLeaveBalanceHandler)}.PreCommit"</c> for the pre-commit case) and
/// <c>idempotencyKey</c> it expects the handler to call <see cref="MaybeFailAfterCommitAsync"/> with. The double
/// throws exactly once for that exact (operationName, idempotencyKey) pair, then disarms itself — so a retried
/// request with the same key sails through on the second attempt, matching the real "server committed once, the
/// caller's response never arrived" scenario Ticket 3's final gap exists to make safe to retry.
/// </summary>
internal sealed class FaultInjectingPostCommitFaultInjector : IPostCommitFaultInjector
{
    private readonly object _lock = new();
    private string? _armedOperationName;
    private string? _armedIdempotencyKey;

    /// <summary>Arms the double to throw exactly once the next time <see cref="MaybeFailAfterCommitAsync"/>
    /// is called with this exact operation name and idempotency key.</summary>
    public void ArmOnce(string operationName, string idempotencyKey)
    {
        lock (_lock)
        {
            _armedOperationName = operationName;
            _armedIdempotencyKey = idempotencyKey;
        }
    }

    /// <summary>Disarms the double so no subsequent call throws, regardless of arguments.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _armedOperationName = null;
            _armedIdempotencyKey = null;
        }
    }

    public Task MaybeFailAfterCommitAsync(string operationName, string? idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_armedOperationName == operationName && _armedIdempotencyKey == idempotencyKey)
            {
                // Fire once only — a real 500 happens exactly once per underlying request attempt;
                // it must not keep firing on the replay/retry that follows.
                _armedOperationName = null;
                _armedIdempotencyKey = null;
                throw new InvalidOperationException(
                    $"Simulated post-commit failure for integration test (operation '{operationName}').");
            }
        }

        return Task.CompletedTask;
    }
}
