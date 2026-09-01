using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeAdministrativeAlertWriter : IAdministrativeAlertWriter
{
    public List<RaiseAdministrativeAlertCommand> Raised { get; } = [];

    public Task RaiseAsync(RaiseAdministrativeAlertCommand command, CancellationToken cancellationToken = default)
    {
        Raised.Add(command);
        return Task.CompletedTask;
    }
}
