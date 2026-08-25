using HR.Modules.Reporting.Services;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Test helper for constructing a real (non-mocked) <see cref="ReportExportAuditor"/> backed by
/// fakes, since <see cref="ReportExportAuditor"/> is a concrete sealed class with no interface to
/// mock. Every Export*Report handler test that just needs "some auditor" to satisfy the
/// constructor (without asserting on the published audit event) can use
/// <see cref="Create(out FakeAuditEventPublisher)"/>.
/// </summary>
internal static class TestReportExportAuditor
{
    private static readonly DateTime DefaultUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ReportExportAuditor Create(out FakeAuditEventPublisher publisher)
    {
        publisher = new FakeAuditEventPublisher();
        return new ReportExportAuditor(publisher, new FakeClock(DefaultUtcNow), new FakeCurrentUser(Guid.NewGuid()));
    }

    public static ReportExportAuditor Create() => Create(out _);
}
