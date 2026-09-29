using HR.Modules.Reporting.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal static class TestReportExportAuditor
{
    private static readonly DateTime DefaultUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ReportExportAuditor Create(out FakeAuditEventPublisher publisher)
    {
        publisher = new FakeAuditEventPublisher();
        return new ReportExportAuditor(
            publisher,
            new CapturingAdministrativeAlertWriter(),
            new FakeClock(DefaultUtcNow),
            new FakeCurrentUser(Guid.NewGuid()),
            NullLogger<ReportExportAuditor>.Instance);
    }

    public static ReportExportAuditor Create() => Create(out _);
}
