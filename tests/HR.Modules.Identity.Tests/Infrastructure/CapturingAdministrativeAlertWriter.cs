using HR.Infrastructure.Abstractions;

namespace HR.Modules.Identity.Tests.Infrastructure;

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
