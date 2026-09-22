using ClosedXML.Excel;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.UploadImportFile;
using HR.Modules.DataImport.Jobs;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Tests;

public class UploadImportFileHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadImportFileHandler BuildHandler(
        DataImportDbContext db,
        FakeImportFileStorageService? storage = null,
        ImportFileUploadOptions? options = null) =>
        new(db,
            storage ?? new FakeImportFileStorageService(),
            new ImportFileValidator(Options.Create(options ?? new ImportFileUploadOptions())),
            new FakeClock(FixedUtcNow),
            NullLogger<UploadImportFileHandler>.Instance);

    private static IFormFile FakeFile(string fileName, string contentType, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    // Plain text content that is neither valid CSV nor a valid XLSX/ZIP container — used only to
    // exercise the "declared type doesn't match content" and "wrong extension/content type" rejection
    // paths, which happen before any file parsing takes place.
    private static byte[] PlainTextBytes(int dataRowCount)
    {
        var lines = new List<string> { "first_name,last_name,email" };
        for (var i = 1; i <= dataRowCount; i++)
            lines.Add($"First{i},Last{i},user{i}@example.com");

        return System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }

    // A real XLSX workbook (ZIP/OOXML) with a header row and N data rows, built via ClosedXML.
    private static byte[] XlsxBytes(int dataRowCount)
    {
        using var workbook  = new XLWorkbook();
        var worksheet       = workbook.Worksheets.Add("Sheet1");

        worksheet.Cell(1, 1).Value = "first_name";
        worksheet.Cell(1, 2).Value = "last_name";
        worksheet.Cell(1, 3).Value = "email";

        for (var i = 1; i <= dataRowCount; i++)
        {
            worksheet.Cell(i + 1, 1).Value = $"First{i}";
            worksheet.Cell(i + 1, 2).Value = $"Last{i}";
            worksheet.Cell(i + 1, 3).Value = $"user{i}@example.com";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static readonly string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static UploadImportFileRequest BuildRequest(
        Guid companyId,
        string entityType = "Employees",
        IFormFile? file = null) => new()
    {
        CompanyId  = companyId,
        EntityType = entityType,
        File       = file ?? FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(3)),
    };

    [Fact]
    public async Task HandleAsync_ValidXlsx_CreatesSession_With_Correct_TotalRows_And_Pending_Status()
    {
        await using var db = BuildContext();
        var storage        = new FakeImportFileStorageService();
        var companyId      = Guid.NewGuid();
        var initiatedBy    = Guid.NewGuid();
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, file: FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(3))),
            initiatedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Employees", result.Value.EntityType);
        Assert.Equal("employees.xlsx", result.Value.FileName);
        Assert.Equal(3, result.Value.TotalRows); // header row excluded
        Assert.Equal(nameof(ImportStatus.Pending), result.Value.Status);

        var saved = await db.ImportSessions.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(3, saved.TotalRows);
        Assert.Equal(ImportStatus.Pending, saved.Status);
        Assert.Equal(initiatedBy, saved.InitiatedByUserId);
        Assert.Equal(XlsxContentType, saved.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(saved.StorageKey));

        Assert.Single(storage.Uploads);
        Assert.Equal("employees.xlsx", storage.Uploads[0].FileName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Csv_Extension_No_Longer_Allowed()
    {
        // CSV import support has been removed; only .xlsx is accepted now.
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.csv", "text/csv", PlainTextBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".csv", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(await db.ImportSessions.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Too_Large()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db, options: new ImportFileUploadOptions { MaxFileSizeBytes = 10 });

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(await db.ImportSessions.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Extension_Not_Allowed()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.txt", "text/plain", PlainTextBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".txt", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(await db.ImportSessions.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_ContentType_Not_Allowed()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.xlsx", "application/json", PlainTextBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        Assert.Empty(await db.ImportSessions.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Xlsx_Content_Does_Not_Match_Declared_Type()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        // Named and declared as XLSX but the bytes are not a ZIP/OOXML container (spoofed/renamed file).
        var spoofedFile = FakeFile(
            "employees.xlsx",
            XlsxContentType,
            PlainTextBytes(3));

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: spoofedFile),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(await db.ImportSessions.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Empty_Xlsx_Is_Rejected_With_No_Data_Rows_Error()
    {
        // A file with only a header row (zero data rows) must be rejected upfront with a clear
        // validation error, rather than silently creating a session/import that can never produce
        // any employees.
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var file = FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(0)); // header row only

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: file),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("no data rows", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ImportSessions.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_When_SessionSaveFails_After_A_Successful_Upload_Deletes_The_Uploaded_File()
    {
        // Follow-up review finding: the intent-reservation save (call 1) succeeds — proving the
        // upload genuinely happened against a durable pre-upload intent — and only the SECOND save
        // (the session insert + intent confirmation) fails.
        var interceptor = new ThrowFromCallSaveChangesInterceptor(failFromCallNumber: 2, persistent: false);
        await using var db = new DataImportDbContext(
            new DbContextOptionsBuilder<DataImportDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(interceptor)
                .Options);

        var storage = new FakeImportFileStorageService();
        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);

        // The immediate compensating delete succeeded, so the intent (persisted before the upload)
        // is marked deleted — it will never need cleanup.
        var intent = await db.OrphanedImportFileUploads.SingleAsync();
        Assert.NotNull(intent.DeletedAt);
    }

    [Fact]
    public async Task HandleAsync_Compensates_Using_An_Independent_Cleanup_Token_Not_The_Cancelled_Request_Token()
    {
        // Security/code review finding #3: a cancelled/timed-out request must never prevent the
        // compensating delete of a blob that was already uploaded — compensation must use its own
        // independently-bounded token, never the (possibly already-cancelled) request token. The
        // request's own token is cancelled only once the second save (after the upload) is reached.
        var interceptor = new ThrowFromCallSaveChangesInterceptor(failFromCallNumber: 2, persistent: false);
        await using var db = new DataImportDbContext(
            new DbContextOptionsBuilder<DataImportDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(interceptor)
                .Options);

        var storage = new FakeImportFileStorageService();
        var handler = BuildHandler(db, storage);

        using var requestCts = new CancellationTokenSource();

        interceptor.BeforeThrow = () => requestCts.Cancel();

        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(3))),
            Guid.NewGuid(),
            requestCts.Token));

        Assert.Single(storage.Deletions);
        var deleteToken = Assert.Single(storage.DeleteCancellationTokens);
        Assert.False(deleteToken.IsCancellationRequested);
        Assert.NotEqual(requestCts.Token, deleteToken);
    }

    [Fact]
    public async Task HandleAsync_When_SessionSaveFails_And_Immediate_Delete_Also_Fails_Leaves_The_Reserved_Intent_Unconfirmed_For_Reconciliation()
    {
        // Follow-up review finding: no separate durable orphan record is created any more — the
        // Reserved intent already exists (persisted before the upload) and is simply left
        // unconfirmed when both the session save and the immediate compensating delete fail.
        var interceptor = new ThrowFromCallSaveChangesInterceptor(failFromCallNumber: 2, persistent: false);
        await using var db = new DataImportDbContext(
            new DbContextOptionsBuilder<DataImportDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(interceptor)
                .Options);

        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var companyId = Guid.NewGuid();
        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(
            BuildRequest(companyId, file: FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None));

        // The immediate delete failed, so no delete is recorded, but the Reserved intent persisted
        // BEFORE the upload must still be there, unconfirmed, so the reconciliation sweep can
        // resolve it.
        Assert.Empty(storage.Deletions);
        Assert.Empty(await db.ImportSessions.ToListAsync());

        var intent = await db.OrphanedImportFileUploads.SingleOrDefaultAsync();
        Assert.NotNull(intent);
        Assert.Equal(companyId, intent!.CompanyId);
        Assert.Equal(storage.Uploads[0].StorageKey, intent.StorageKey);
        Assert.Null(intent.DeletedAt);
        Assert.Null(intent.ConfirmedAt);

        // Proves reconciliation actually cleans this up, not merely that a log line was written:
        // the object genuinely still exists in storage (the delete failed), so the sweep must find
        // it and delete it, not merely clear the intent as "never uploaded".
        var reconciliationStorage = new FakeImportFileStorageService();
        reconciliationStorage.SeedContent(intent.StorageKey, [1, 2, 3]);
        var job = new PurgeOrphanedImportFileUploadsJob(
            db, reconciliationStorage,
            Options.Create(new DataImportFileRetentionOptions { UploadIntentGracePeriodMinutes = 0 }),
            new FakeLegalHoldStatusReader(), new FakeAdministrativeAlertWriter(),
            new FakeClock(FixedUtcNow.AddHours(2)), NullLogger<PurgeOrphanedImportFileUploadsJob>.Instance);

        await job.ExecuteAsync();

        var resolved = await db.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        Assert.Contains(intent.StorageKey, reconciliationStorage.Deletions);
        Assert.NotNull(resolved.DeletedAt);
    }

    [Fact]
    public async Task HandleAsync_When_SessionSaveFails_Persistently_And_Delete_Also_Fails_Still_Rethrows_Original_Exception()
    {
        // Every SaveChangesAsync call from the second one onward fails identically (a persistent
        // database outage that started right after the upload) — the ORIGINAL exception from the
        // session save must still be the one that propagates, not a different exception from the
        // compensation path. The first save (the pre-upload intent reservation) must still succeed,
        // otherwise nothing would ever be uploaded and this scenario would be meaningless.
        var interceptor = new ThrowFromCallSaveChangesInterceptor(failFromCallNumber: 2, persistent: true);
        await using var db = new DataImportDbContext(
            new DbContextOptionsBuilder<DataImportDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(interceptor)
                .Options);

        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = int.MaxValue };
        var handler = BuildHandler(db, storage);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.xlsx", XlsxContentType, XlsxBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("Simulated database failure.", thrown.Message);
        Assert.Empty(storage.Deletions);
    }

    /// <summary>Throws a <see cref="DbUpdateException"/> from the given SaveChangesAsync call number
    /// onward against a context this interceptor is attached to (every earlier call succeeds
    /// normally). When <paramref name="persistent"/> is false, only that single call throws and
    /// every call after it succeeds — modelling a single transient save failure. When true, every
    /// call from that point on throws — modelling a persistent database outage. Used in place of
    /// subclassing (DataImportDbContext is sealed).</summary>
    private sealed class ThrowFromCallSaveChangesInterceptor(int failFromCallNumber, bool persistent) : SaveChangesInterceptor
    {
        private int _callCount;

        /// <summary>Optional hook invoked immediately before this interceptor throws, e.g. to
        /// cancel a request's own token to simulate a client disconnect racing the save.</summary>
        public Action? BeforeThrow { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            _callCount++;
            if (_callCount == failFromCallNumber || (persistent && _callCount > failFromCallNumber))
            {
                BeforeThrow?.Invoke();
                throw new DbUpdateException("Simulated database failure.");
            }

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == failFromCallNumber || (persistent && _callCount > failFromCallNumber))
            {
                BeforeThrow?.Invoke();
                throw new DbUpdateException("Simulated database failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
