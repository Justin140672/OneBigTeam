using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

/// <summary>
/// ADM-03: records every <see cref="RaiseAsync"/> call so ingestion call-site tests can assert on
/// the exact command shape (severity, category, dedup key, …) without a real DbContext.
/// </summary>
internal sealed class CapturingAdministrativeAlertWriter : IAdministrativeAlertWriter
{
    private readonly List<RaiseAdministrativeAlertCommand> _commands = [];

    public IReadOnlyList<RaiseAdministrativeAlertCommand> Commands => _commands;

    public Task RaiseAsync(RaiseAdministrativeAlertCommand command, CancellationToken cancellationToken = default)
    {
        _commands.Add(command);
        return Task.CompletedTask;
    }
}
