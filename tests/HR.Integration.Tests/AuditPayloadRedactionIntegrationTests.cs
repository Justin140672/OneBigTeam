using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// NFR-01: proves end-to-end that a published audit event whose payload carries a prohibited
/// field name or a sensitive-looking value is rejected by the real DbAuditEventPublisher and
/// never lands in the audit staging table, while a clean event is persisted normally.
/// </summary>
[Collection("Integration")]
public class AuditPayloadRedactionIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AuditPayloadRedactionIntegrationTests(ApiWebApplicationFactory factory) => _factory = factory;

    private sealed record ProbeAuditEvent(Guid CompanyId, object? Payload) : IAuditEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        AuditActorType IAuditEvent.ActorType => AuditActorType.Human;
        string IAuditEvent.EventType => "test.nfr01.probe";
        string IAuditEvent.EntityType => "Nfr01Probe";
        Guid IAuditEvent.EntityId => CompanyId;
        Guid? IAuditEvent.EmployeeId => null;
        Guid? IAuditEvent.ActorUserId => new("a0d10001-0000-0000-0000-000000000001");
        Guid? IAuditEvent.ActorEmployeeId => null;
        DateTimeOffset IAuditEvent.OccurredAt => DateTimeOffset.UtcNow;
        Guid? IAuditEvent.CorrelationId => null;
        string? IAuditEvent.Summary => "NFR-01 probe";
        object? IAuditEvent.Before => null;
        object? IAuditEvent.After => Payload;
        object? IAuditEvent.Metadata => null;
    }

    private async Task<bool> PublishAndCheckPersisted(object? payload)
    {
        var evt = new ProbeAuditEvent(Guid.NewGuid(), payload);

        using (var scope = _factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
            await publisher.PublishAsync(evt, CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            return await db.AuditPendingItems.AsNoTracking().AnyAsync(p => p.EventId == evt.EventId);
        }
    }

    [Fact]
    public async Task Clean_payload_is_persisted()
    {
        var persisted = await PublishAndCheckPersisted(
            new { effectiveFrom = "2026-01-01", salaryType = "Annual", currency = "GBP", direction = "Increase" });

        Assert.True(persisted);
    }

    [Fact]
    public async Task Prohibited_field_name_payload_is_not_persisted()
    {
        var persisted = await PublishAndCheckPersisted(new { password = "hunter2" });

        Assert.False(persisted);
    }

    [Fact]
    public async Task Sensitive_value_payload_is_not_persisted()
    {
        var persisted = await PublishAndCheckPersisted(new { note = "Authorization: Bearer abc.def-ghi" });

        Assert.False(persisted);
    }
}
