using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Features.DownloadOrganisationDataExport;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class DownloadOrganisationDataExportHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowOffset = new(Now);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static ReportingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<OrganisationDataExport> SeedCompletedAsync(
        ReportingDbContext db, Guid? companyId = null, DateTimeOffset? completedAt = null, string storageKey = "exports/key.zip")
    {
        var export = OrganisationDataExport.Create(companyId ?? CompanyId, UserId, "Admin", NowOffset.AddHours(-3));
        var token = Guid.NewGuid();
        export.BeginAttempt(token, NowOffset.AddHours(-2));
        export.MarkCompleted(token, storageKey, 10, completedAt ?? NowOffset.AddHours(-1));
        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();
        return export;
    }

    private static DownloadOrganisationDataExportHandler Handler(
        ReportingDbContext db, FakeExportStorage storage, HR.SharedKernel.IAuditEventPublisher audit) =>
        new(db, storage, audit, new FakeClock(Now));

    [Fact]
    public async Task HandleAsync_Returns_The_Live_Storage_Stream_Without_Copying_It()
    {
        await using var db = BuildContext();
        var export = await SeedCompletedAsync(db);
        var source = new TrackingStream(10);
        var storage = new FakeExportStorage(source);
        var audit = new FakeAuditEventPublisher();

        var result = await Handler(db, storage, audit).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(source, result.Value!.Content);
        Assert.Equal(0, source.BytesRead);
        Assert.False(source.Disposed);
        Assert.Equal("application/zip", result.Value.ContentType);
        Assert.Equal("organisation-data-export-2026-09-01.zip", result.Value.FileName);
    }

    [Fact]
    public async Task HandleAsync_Reports_Length_Only_When_The_Stream_Knows_It()
    {
        await using var db = BuildContext();
        var export = await SeedCompletedAsync(db);
        var seekable = new MemoryStream(new byte[42]);
        var unknown = new TrackingStream(10) { Seekable = false };

        var withLength = await Handler(db, new FakeExportStorage(seekable), new FakeAuditEventPublisher()).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None);
        var withoutLength = await Handler(db, new FakeExportStorage(unknown), new FakeAuditEventPublisher()).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None);

        Assert.Equal(42, withLength.Value!.ContentLength);
        Assert.Null(withoutLength.Value!.ContentLength);
    }

    [Fact]
    public async Task HandleAsync_Records_The_Download_And_Audits_Before_Any_Bytes_Are_Sent()
    {
        await using var db = BuildContext();
        var export = await SeedCompletedAsync(db);
        var source = new TrackingStream(10);
        var audit = new FakeAuditEventPublisher();

        await Handler(db, new FakeExportStorage(source), audit).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None);

        var stored = await db.OrganisationDataExports.SingleAsync(e => e.Id == export.Id);
        Assert.Equal(1, stored.DownloadCount);
        Assert.Equal(UserId, stored.LastDownloadedByUserId);
        var evt = Assert.Single(audit.Published);
        Assert.Equal("organisation-data-export.downloaded", evt.EventType);
        Assert.Equal(0, source.BytesRead);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Another_Companys_Export_Without_Opening_Storage()
    {
        await using var db = BuildContext();
        var export = await SeedCompletedAsync(db, companyId: Guid.NewGuid());
        var storage = new FakeExportStorage(new TrackingStream(1));

        var result = await Handler(db, storage, new FakeAuditEventPublisher()).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal(0, storage.OpenCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_An_Expired_Export_Without_Opening_Storage()
    {
        await using var db = BuildContext();
        var export = await SeedCompletedAsync(db, completedAt: NowOffset.AddDays(-30));
        var storage = new FakeExportStorage(new TrackingStream(1));

        var result = await Handler(db, storage, new FakeAuditEventPublisher()).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, storage.OpenCount);
        Assert.Equal(0, (await db.OrganisationDataExports.SingleAsync()).DownloadCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_A_Pending_Export()
    {
        await using var db = BuildContext();
        var pending = OrganisationDataExport.Create(CompanyId, UserId, "Admin", NowOffset);
        db.OrganisationDataExports.Add(pending);
        await db.SaveChangesAsync();

        var result = await Handler(db, new FakeExportStorage(new TrackingStream(1)), new FakeAuditEventPublisher()).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = pending.Id }, UserId, CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_The_Object_Is_Missing_From_Storage_And_Does_Not_Count_A_Download()
    {
        await using var db = BuildContext();
        var export = await SeedCompletedAsync(db);
        var audit = new FakeAuditEventPublisher();

        var result = await Handler(db, new FakeExportStorage(null), audit).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(audit.Published);
        Assert.Equal(0, (await db.OrganisationDataExports.SingleAsync()).DownloadCount);
    }

    [Fact]
    public async Task HandleAsync_Disposes_The_Stream_When_Recording_The_Download_Fails()
    {
        await using var db = BuildContext();
        var export = await SeedCompletedAsync(db);
        var source = new TrackingStream(10);
        var audit = new ThrowingAuditPublisher();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(db, new FakeExportStorage(source), audit).HandleAsync(
            new DownloadOrganisationDataExportRequest { CompanyId = CompanyId, ExportId = export.Id }, UserId, CancellationToken.None));

        Assert.True(source.Disposed);
    }

    private sealed class FakeExportStorage(Stream? stream) : IOrganisationDataExportStorage
    {
        public int OpenCount { get; private set; }

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult(stream);
        }

        public Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingAuditPublisher : HR.SharedKernel.IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("audit store unavailable");
    }

    private sealed class TrackingStream(long length) : Stream
    {
        public bool Seekable { get; set; } = true;
        public long BytesRead { get; private set; }
        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => Seekable;
        public override bool CanWrite => false;
        public override long Length => Seekable ? length : throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - BytesRead);
            BytesRead += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
