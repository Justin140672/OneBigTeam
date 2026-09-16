using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Ticket 24 (P1): a generalisation of <see cref="ApplicationSaveBarrier"/> for forcing a
/// deterministic, genuinely-concurrent Postgres race on an arbitrary table/command-verb/id
/// combination, used by the Identity invite-acceptance concurrency tests (invite claim/cancel
/// UPDATEs on <c>identity.user_invites</c>, and the operation/profile/role INSERT races on
/// <c>identity.invite_acceptance_operations</c> / <c>identity.user_profiles</c> / <c>identity.user_roles</c>).
///
/// See <see cref="ApplicationSaveBarrier"/>'s own doc comment for the full rationale (subscribes to
/// the EF Core "CommandExecuting" diagnostic event, which fires synchronously before the command is
/// sent to the server, and blocks the calling thread via a <see cref="Barrier"/> until the required
/// number of racers have all arrived at the same point, so Postgres itself — not test timing — decides
/// the winner).
///
/// Usage: <c>using var arm = SqlCommandBarrier.Arm("user_invites", inviteId);</c> (defaults to
/// matching an "UPDATE" command) before firing the racing requests. Pass <c>commandVerb: "INSERT"</c>
/// to instead bar on an insert (e.g. for the operation/profile/role creation races) — since those
/// inserts carry a freshly-generated primary key rather than the id you already know (invite/employee
/// id), match on whichever parameter value the racing rows share instead (e.g. the invite id column
/// on invite_acceptance_operations, or the employee id column on user_profiles/user_roles).
/// </summary>
internal sealed class SqlCommandBarrier : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    public static readonly SqlCommandBarrier Instance = new();

    private const string CommandExecutingEvent = "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting";

    private static readonly object Gate = new();
    private static readonly Dictionary<(string TableNameSubstring, Guid Id), ArmedEntry> Armed = new();

    private int _subscribed;

    private SqlCommandBarrier()
    {
    }

    private sealed record ArmedEntry(string CommandVerb, Barrier Barrier);

    /// <summary>
    /// Arms the barrier: <paramref name="participantCount"/> commands whose text contains both
    /// <paramref name="commandVerb"/> and <paramref name="tableNameSubstring"/>, and which carry a
    /// parameter with value <paramref name="id"/>, must arrive before any of them is released to
    /// execute. Dispose the result once the race is over (always, even on assertion failure).
    /// </summary>
    public static IDisposable Arm(string tableNameSubstring, Guid id, int participantCount = 2, string commandVerb = "UPDATE")
    {
        Instance.EnsureSubscribed();

        lock (Gate)
        {
            Armed[(tableNameSubstring, id)] = new ArmedEntry(commandVerb, new Barrier(participantCount));
        }

        return new ArmScope(tableNameSubstring, id);
    }

    private static void Disarm(string tableNameSubstring, Guid id)
    {
        ArmedEntry? entry;
        lock (Gate)
        {
            if (!Armed.Remove((tableNameSubstring, id), out entry))
                return;
        }

        // By the time a test disposes its ArmScope, the race has already been awaited (both
        // responses received), so no thread can still be blocked in SignalAndWait for this key.
        entry.Barrier.Dispose();
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

        (string TableNameSubstring, Guid Id, Barrier Barrier)? match = FindMatch(commandText, data.Command);
        if (match is not { } matched)
        {
            return;
        }

        // Deliberately NOT an async await - DiagnosticListener notifications are synchronous,
        // non-awaited callbacks, so a blocking wait is required to hold the calling thread here.
        matched.Barrier.SignalAndWait(TimeSpan.FromSeconds(30));
    }

    private static (string, Guid, Barrier)? FindMatch(string commandText, DbCommand command)
    {
        lock (Gate)
        {
            if (Armed.Count == 0)
            {
                return null;
            }

            foreach (var ((tableNameSubstring, id), entry) in Armed)
            {
                if (commandText.IndexOf(entry.CommandVerb, StringComparison.Ordinal) < 0 ||
                    commandText.IndexOf(tableNameSubstring, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                foreach (DbParameter parameter in command.Parameters)
                {
                    if (parameter.Value is Guid guidValue && guidValue == id)
                    {
                        return (tableNameSubstring, id, entry.Barrier);
                    }
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
        private readonly string _tableNameSubstring;
        private readonly Guid _id;
        private bool _disposed;

        public ArmScope(string tableNameSubstring, Guid id)
        {
            _tableNameSubstring = tableNameSubstring;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Disarm(_tableNameSubstring, _id);
        }
    }
}
