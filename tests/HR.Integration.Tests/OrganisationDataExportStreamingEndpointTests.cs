using System.Net;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class OrganisationDataExportStreamingEndpointTests
{
    private const string StorageKey = "organisation-exports/streaming-test/key.zip";
    private static readonly Guid AcmeCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyAdmin = Guid.Parse("66000010-0000-0000-0000-000000000002");

    private readonly ApiWebApplicationFactory _factory;

    public OrganisationDataExportStreamingEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private WebApplicationFactoryHandle WithStorage(GeneratedExportStorage storage) =>
        new(_factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOrganisationDataExportStorage>();
                services.AddSingleton<IOrganisationDataExportStorage>(storage);
            })));

    private sealed class WebApplicationFactoryHandle(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : IAsyncDisposable
    {
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory { get; } = factory;

        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    [Fact]
    public async Task Download_Streams_A_Large_Archive_With_Bounded_Memory_Beyond_SignalR_Message_Limits()
    {
        const long archiveBytes = 512L * 1024 * 1024;
        var storage = new GeneratedExportStorage(archiveBytes);
        await using var handle = WithStorage(storage);
        var exportId = await SeedCompletedExportAsync(handle.Factory);
        using var client = await CompanyAdminClient(handle.Factory);

        GC.Collect();
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        long peak = baseline;
        using var sampler = new CancellationTokenSource();
        var sampling = Task.Run(async () =>
        {
            while (!sampler.IsCancellationRequested)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: false));
                await Task.Delay(25);
            }
        });

        using var response = await client.GetAsync(
            DownloadUrl(exportId), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.EndsWith(".zip", response.Content.Headers.ContentDisposition.FileName!.Trim('"'));

        await using var body = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await body.ReadAsync(buffer)) > 0)
        {
            total += read;
        }

        await sampler.CancelAsync();
        await sampling;

        Assert.Equal(archiveBytes, total);
        Assert.True(storage.OpenCount == 1);
        Assert.True(
            peak - baseline < 128L * 1024 * 1024,
            $"Managed memory grew by {(peak - baseline) / (1024 * 1024)} MiB while streaming a {archiveBytes / (1024 * 1024)} MiB archive.");
        Assert.True(
            GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore < archiveBytes / 2,
            "Streaming a large archive must not allocate memory proportional to its size.");
    }

    [Fact]
    public async Task Download_Response_Begins_Before_The_Archive_Has_Been_Read()
    {
        var storage = new GeneratedExportStorage(256L * 1024 * 1024);
        await using var handle = WithStorage(storage);
        var exportId = await SeedCompletedExportAsync(handle.Factory);
        using var client = await CompanyAdminClient(handle.Factory);

        using var response = await client.GetAsync(DownloadUrl(exportId), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(storage.LastStream!.BytesGenerated < 64L * 1024 * 1024);
    }

    [Fact]
    public async Task Download_Records_Count_And_Audit_Exactly_Once_And_Marks_The_Response_As_An_Attachment()
    {
        var storage = new GeneratedExportStorage(1024);
        await using var handle = WithStorage(storage);
        var exportId = await SeedCompletedExportAsync(handle.Factory);
        using var client = await CompanyAdminClient(handle.Factory);

        using var response = await client.GetAsync(DownloadUrl(exportId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1024, (await response.Content.ReadAsByteArrayAsync()).Length);
        Assert.Contains("private", response.Headers.CacheControl!.ToString());
        Assert.Contains("no-store", response.Headers.CacheControl.ToString());

        using var scope = handle.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var export = await db.OrganisationDataExports.SingleAsync(e => e.Id == exportId);
        Assert.Equal(1, export.DownloadCount);
        Assert.Equal(CompanyAdmin, export.LastDownloadedByUserId);

        var auditDb = scope.ServiceProvider.GetRequiredService<HR.Infrastructure.Persistence.AuditDbContext>();
        var audit = await auditDb.AuditEvents
            .Where(e => e.EntityId == exportId && e.EventType == "organisation-data-export.downloaded")
            .ToListAsync();
        Assert.Single(audit);
    }

    [Fact]
    public async Task Download_Cancellation_Disposes_The_Underlying_Storage_Stream()
    {
        var storage = new GeneratedExportStorage(2L * 1024 * 1024 * 1024);
        await using var handle = WithStorage(storage);
        var exportId = await SeedCompletedExportAsync(handle.Factory);
        using var client = await CompanyAdminClient(handle.Factory);
        using var cts = new CancellationTokenSource();

        var response = await client.GetAsync(DownloadUrl(exportId), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStreamAsync(cts.Token);
        var buffer = new byte[81920];
        Assert.True(await body.ReadAsync(buffer, cts.Token) > 0);

        await cts.CancelAsync();
        body.Dispose();
        response.Dispose();

        var disposed = SpinWait.SpinUntil(() => storage.LastStream!.Disposed, TimeSpan.FromSeconds(15));
        Assert.True(disposed, "The storage stream must be disposed once the client disconnects.");
        Assert.True(storage.LastStream!.BytesGenerated < 2L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task Download_Of_An_Expired_Export_Returns_NotFound_And_Never_Opens_Storage()
    {
        var storage = new GeneratedExportStorage(1024);
        await using var handle = WithStorage(storage);
        var exportId = await SeedCompletedExportAsync(handle.Factory, completedAt: DateTimeOffset.UtcNow.AddDays(-30));
        using var client = await CompanyAdminClient(handle.Factory);

        using var response = await client.GetAsync(DownloadUrl(exportId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, storage.OpenCount);
    }

    [Fact]
    public async Task Download_For_Another_Tenant_Is_Forbidden_And_Never_Opens_Storage()
    {
        var storage = new GeneratedExportStorage(1024);
        await using var handle = WithStorage(storage);
        var exportId = await SeedCompletedExportAsync(handle.Factory);
        using var client = await CompanyAdminClient(handle.Factory);

        using var response = await client.GetAsync($"/api/companies/{OtherCompanyId}/reporting/data-exports/{exportId}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, storage.OpenCount);
    }

    [Fact]
    public async Task Download_Without_Authentication_Is_Unauthorized()
    {
        var storage = new GeneratedExportStorage(1024);
        await using var handle = WithStorage(storage);
        using var client = handle.Factory.CreateClient();

        using var response = await client.GetAsync(DownloadUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, storage.OpenCount);
    }

    private static string DownloadUrl(Guid exportId) =>
        $"/api/companies/{AcmeCompanyId}/reporting/data-exports/{exportId}/download";

    private static async Task<Guid> SeedCompletedExportAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, DateTimeOffset? completedAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

        var export = OrganisationDataExport.Create(AcmeCompanyId, CompanyAdmin, "Company Admin", DateTimeOffset.UtcNow.AddDays(-31));
        var token = Guid.NewGuid();
        export.BeginAttempt(token, DateTimeOffset.UtcNow.AddDays(-31));
        export.MarkCompleted(token, StorageKey, 1024, completedAt ?? DateTimeOffset.UtcNow.AddMinutes(-5));
        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();
        return export.Id;
    }

    private async Task<HttpClient> CompanyAdminClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, CompanyAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, AcmeCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdmin, SystemRoles.Employee, AcmeCompanyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdmin, SystemRoles.CompanyAdministrator, AcmeCompanyId);
        return client;
    }

    private sealed class GeneratedExportStorage(long archiveBytes) : IOrganisationDataExportStorage
    {
        private int _openCount;

        public int OpenCount => Volatile.Read(ref _openCount);

        public GeneratedStream? LastStream { get; private set; }

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _openCount);
            LastStream = new GeneratedStream(archiveBytes);
            return Task.FromResult<Stream?>(LastStream);
        }

        public Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class GeneratedStream(long totalBytes) : Stream
    {
        private long _generated;
        private int _disposed;

        public long BytesGenerated => Interlocked.Read(ref _generated);

        public bool Disposed => Volatile.Read(ref _disposed) == 1;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesGenerated; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Fill(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Interlocked.Exchange(ref _disposed, 1);
            base.Dispose(disposing);
        }

        private int Fill(Span<byte> destination)
        {
            var remaining = totalBytes - Interlocked.Read(ref _generated);
            var count = (int)Math.Min(destination.Length, remaining);
            destination[..count].Clear();
            Interlocked.Add(ref _generated, count);
            return count;
        }
    }
}
