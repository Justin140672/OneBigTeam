using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>ADM-03: records RaiseAsync calls for report-generation failure alert assertions.</summary>
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
