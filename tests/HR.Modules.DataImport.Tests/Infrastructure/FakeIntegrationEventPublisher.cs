using HR.SharedKernel;

namespace HR.Modules.DataImport.Tests.Infrastructure;

/// <summary>
/// Captures every event published during a ConfirmImportSessionHandler test run, so assertions
/// can verify EmployeeCreatedIntegrationEvent (with IsImported: true) and
/// EmployeeImportedIntegrationEvent were both raised per created row.
/// </summary>
internal sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly List<IIntegrationEvent> _published = [];

    public IReadOnlyList<IIntegrationEvent> Published => _published;

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        _published.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
