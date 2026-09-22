using HR.Infrastructure.Abstractions;

namespace HR.Modules.DataImport.Tests.Infrastructure;

// Mirrors the fake of the same name in HR.Modules.Notifications.Tests, which is project-local
// rather than shared.
internal sealed class FakeAdministrativeAlertWriter : IAdministrativeAlertWriter
{
    public List<RaiseAdministrativeAlertCommand> Raised { get; } = [];

    public Task RaiseAsync(RaiseAdministrativeAlertCommand command, CancellationToken cancellationToken = default)
    {
        Raised.Add(command);
        return Task.CompletedTask;
    }
}
