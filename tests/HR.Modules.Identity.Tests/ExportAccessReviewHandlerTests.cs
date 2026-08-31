using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ExportAccessReview;
using HR.Modules.Identity.Features.GetAccessReview;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-08: unit tests for <see cref="ExportAccessReviewHandler"/> — maps GetAccessReviewHandler's
/// output to export rows, and audits both success and (via a throwing exporter) failure.
/// </summary>
[Collection("IdentityDatabase")]
public class ExportAccessReviewHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    private sealed class ThrowingReportExporter : IReportExporter
    {
        public ReportExportFile Export(ReportExportFormat format, ReportExportData data) =>
            throw new InvalidOperationException("Export renderer unavailable");
    }

    private GetAccessReviewHandler BuildReviewHandler(IReadOnlyList<Guid> employeeIds) =>
        new(fixture.BuildContext(), new FakeEmployeeNameReader(), new FakeEmployeeAudienceReader(employeeIds), Clock);

    [Fact]
    public async Task HandleAsync_Exports_One_Row_Per_Privilege_And_Publishes_A_Success_Audit_Event()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "export-priv@test.com", "hash", "Export", "Priv", Now));
            db.Roles.Add(Role.Create(roleId, $"ExportRole.{Guid.NewGuid():N}", Now));
            db.UserRoles.Add(UserRole.Create(employeeId, roleId, Now));
            await db.SaveChangesAsync();
        }

        var reviewHandler = BuildReviewHandler([employeeId]);
        var exporter = new FakeReportExporter();
        var publisher = new FakeAuditEventPublisher();
        var handler = new ExportAccessReviewHandler(
            reviewHandler, exporter, publisher, FakeCurrentUser.Authenticated(ActorUserId, companyId.ToString()), Clock);

        var result = await handler.HandleAsync(
            new ExportAccessReviewRequest { CompanyId = companyId, Format = ReportExportFormat.Csv },
            CancellationToken.None);

        Assert.NotNull(result.File);
        Assert.Equal(ReportExportFormat.Csv, exporter.LastFormat);
        Assert.NotNull(exporter.LastData);
        Assert.Single(exporter.LastData!.Rows);

        var published = Assert.Single(publisher.PublishedEvents);
        var exportedEvent = Assert.IsType<AccessReviewExportedAuditEvent>(published);
        Assert.True(exportedEvent.Success);
        Assert.Equal(1, exportedEvent.RowCount);
        Assert.Null(exportedEvent.FailureReason);
        Assert.Equal("Csv", exportedEvent.Format);
        Assert.Equal(ActorUserId, exportedEvent.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_A_Failure_Audit_Event_And_Rethrows_When_The_Exporter_Throws()
    {
        var companyId = Guid.NewGuid();
        var reviewHandler = BuildReviewHandler([]);
        var publisher = new FakeAuditEventPublisher();
        var handler = new ExportAccessReviewHandler(
            reviewHandler, new ThrowingReportExporter(), publisher,
            FakeCurrentUser.Authenticated(ActorUserId, companyId.ToString()), Clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new ExportAccessReviewRequest { CompanyId = companyId, Format = ReportExportFormat.Pdf },
            CancellationToken.None));

        var published = Assert.Single(publisher.PublishedEvents);
        var exportedEvent = Assert.IsType<AccessReviewExportedAuditEvent>(published);
        Assert.False(exportedEvent.Success);
        Assert.Null(exportedEvent.RowCount);
        Assert.Equal("Export renderer unavailable", exportedEvent.FailureReason);
        Assert.Equal("Pdf", exportedEvent.Format);
    }

    /// <summary>Hand-rolled fake for IReportExporter — mirrors HR.Modules.Reporting.Tests' FakeReportExporter.</summary>
    private sealed class FakeReportExporter : IReportExporter
    {
        public ReportExportFormat? LastFormat { get; private set; }
        public ReportExportData? LastData { get; private set; }

        public ReportExportFile Export(ReportExportFormat format, ReportExportData data)
        {
            LastFormat = format;
            LastData = data;
            return new ReportExportFile([1, 2, 3], "text/csv", "access-review.csv");
        }
    }
}
