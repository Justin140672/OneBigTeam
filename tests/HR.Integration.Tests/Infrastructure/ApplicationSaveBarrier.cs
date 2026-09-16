using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Ticket 16 (P2) follow-up to Ticket 14: a deterministic "both racers hold the same stale version"
/// barrier for Recruitment application-transition concurrency tests, so a genuinely-racing pair of
/// HTTP requests reliably exercises Postgres's own optimistic-concurrency check (stale-version UPDATE
/// loses) rather than an ambiguous outcome caused by unsynchronised <c>Task.WhenAll</c> timing (where
/// the loser's read can happen strictly after the winner's write already committed, producing a
/// business-validation rejection instead of a genuine version conflict).
///
/// Implemented — like <see cref="Performance.QueryCountingInterceptor"/> — as a process-wide
/// <see cref="DiagnosticListener"/> observer rather than a DI-registered <c>IInterceptor</c>: this
/// codebase's module <c>AddDbContext</c> registrations do not pick up a test-side DI interceptor, but
/// the "Microsoft.EntityFrameworkCore" diagnostic source fires unconditionally for every context.
/// Subscribes to "CommandExecuting" (fires synchronously, BEFORE the command is sent to the server) —
/// NOT "CommandExecuted" (fires after, too late to act as a pre-save barrier).
///
/// Usage: <c>using var arm = ApplicationSaveBarrier.Arm(applicationId);</c> before starting the race,
/// then fire both racing requests. When each request's SaveChangesAsync issues its batched UPDATE
/// command touching <c>recruitment.applications</c> for the armed <paramref name="applicationId"/> (the id is
/// matched against the command's own parameter values, not string-parsed from SQL text — robust to
/// parameter ordering/naming), the observer blocks that request's calling thread SYNCHRONOUSLY (this
/// diagnostic event is a synchronous, non-awaited notification, so a blocking wait is required — not
/// an async await) until the required number of racers (2 by default) have both arrived at this same
/// point, then releases both together via <see cref="Barrier"/>. From there, which UPDATE actually
/// executes first against Postgres — and therefore which request's optimistic-concurrency check
/// succeeds — is genuinely decided by Postgres, not fixed by the test.
///
/// Thread-safety / scope: state is keyed per Application.Id in a process-wide dictionary guarded by a
/// single lock, so unrelated tests running in the same process (this assembly disables test
/// parallelization; scoped per-id anyway as a second safety net) never interfere with each other.
/// <see cref="Arm"/> returns an <see cref="IDisposable"/> that disarms (and disposes the underlying
/// <see cref="Barrier"/>) in the caller's <c>finally</c>/<c>using</c> block — always dispose it even if
/// the race assertions fail, or a leaked armed barrier could hang an unrelated later test that happens
/// to reuse the same Application.Id (astronomically unlikely with fresh Guids, but cheap to avoid).
/// </summary>
internal sealed class ApplicationSaveBarrier : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    public static readonly ApplicationSaveBarrier Instance = new();

    private const string CommandExecutingEvent = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting";

    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, Barrier> ArmedBarriers = new();

    private int _subscribed;

    private ApplicationSaveBarrier()
    {
    }

    /// <summary>
    /// Arms the barrier for <paramref name="applicationId"/>, requiring <paramref name="participantCount"/>
    /// (2 for a two-way race) UPDATE commands against <c>recruitment.applications</c> carrying this id
    /// to arrive before either is released to execute. Dispose the result once the race is over.
    /// </summary>
    public static IDisposable Arm(Guid applicationId, int participantCount = 2)
    {
        Instance.EnsureSubscribed();

        lock (Gate)
        {
            ArmedBarriers[applicationId] = new Barrier(participantCount);
        }

        return new ArmScope(applicationId);
    }

    private static void Disarm(Guid applicationId)
    {
        Barrier? barrier;
        lock (Gate)
        {
            if (!ArmedBarriers.Remove(applicationId, out barrier))
                return;
        }

        // Release anyone still (unexpectedly) waiting rather than leaving them blocked forever, then
        // dispose. RemoveParticipants throws if nobody is waiting, so guard with the barrier's own
        // count; simplest safe option is just to dispose - Barrier.Dispose while a thread is inside
        // SignalAndWait is documented as unsupported, but by the time a test disposes its ArmScope the
        // race has already been awaited (both responses received), so no thread can still be blocked
        // in SignalAndWait for this id.
        barrier.Dispose();
    }

    private void EnsureSubscribed()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
        {
            DiagnosticListener.AllListeners.Subscribe(this);
        }
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
    {
        if (value.Name == "Microsoft.EntityFrameworkCore")
        {
            value.Subscribe(this);
        }
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Key != CommandExecutingEvent || value.Value is not CommandEventData data)
        {
            return;
        }

        var commandText = data.Command.CommandText;

        // Cheap pre-filter before touching parameters: only the batched SaveChanges command that
        // updates the applications table is of interest here (skip plain SELECTs, and INSERTs into
        // idempotency_records / application_stage_history_entries that carry no matching UPDATE).
        if (commandText.IndexOf("UPDATE", StringComparison.Ordinal) < 0 ||
            commandText.IndexOf("applications", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        Guid? matchedId = FindArmedApplicationId(data.Command);
        if (matchedId is not { } applicationId)
        {
            return;
        }

        Barrier? barrier;
        lock (Gate)
        {
            ArmedBarriers.TryGetValue(applicationId, out barrier);
        }

        // Blocks the calling thread synchronously until every racer's UPDATE command for this
        // applicationId has reached this same point - deliberately NOT an async await, since
        // DiagnosticListener notifications are synchronous, non-awaited callbacks.
        barrier?.SignalAndWait(TimeSpan.FromSeconds(30));
    }

    private static Guid? FindArmedApplicationId(DbCommand command)
    {
        lock (Gate)
        {
            if (ArmedBarriers.Count == 0)
            {
                return null;
            }

            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is Guid guidValue && ArmedBarriers.ContainsKey(guidValue))
                {
                    return guidValue;
                }
            }
        }

        return null;
    }

    void IObserver<DiagnosticListener>.OnCompleted()
    {
    }

    void IObserver<DiagnosticListener>.OnError(Exception error)
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted()
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
    {
    }

    private sealed class ArmScope : IDisposable
    {
        private readonly Guid _applicationId;
        private bool _disposed;

        public ArmScope(Guid applicationId) => _applicationId = applicationId;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Disarm(_applicationId);
        }
    }
}
