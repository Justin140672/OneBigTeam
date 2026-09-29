using ClosedXML.Excel;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.ValidateImportSession;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.DataImport.Tests;

public class ValidateImportSessionHandlerFileDeletionTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNowOffset = new(FixedUtcNow, TimeSpan.Zero);

    private const string StandardHeader =
        "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile,Salary Amount";

    private const string MandatoryFieldSuffix = "1990-01-01,British,Female,Engineering,London,Permanent,Developer,50000";

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ValidateImportSessionHandler BuildHandler(
        DataImportDbContext db,
        FakeImportFileStorageService storage,
        FakeEmployeeImportLookupReader? lookupReader = null,
        FakeImportLookupResolver? lookupResolver = null) =>
        new(
            db,
            storage,
            new EmployeeImportFileParser(),
            new EmployeeStagingRowValidator(
                lookupReader ?? new FakeEmployeeImportLookupReader(),
                lookupResolver ?? new FakeImportLookupResolver(),
                new FakeCompanyEmployeeNumberSettingsReader()),
            new FakeClock(FixedUtcNow),
            NullLogger<ValidateImportSessionHandler>.Instance);

    private static FakeImportLookupResolver SeededResolver(Guid companyId)
    {
        var resolver = new FakeImportLookupResolver();
        resolver.SeedExistingDepartment(companyId, "Engineering", Guid.NewGuid());
        resolver.SeedExistingEmploymentType(companyId, "Permanent", Guid.NewGuid());
        resolver.SeedExistingLocation(companyId, "London", Guid.NewGuid());
        resolver.SeedExistingPositionProfile(companyId, "Developer", Guid.NewGuid());
        return resolver;
    }

    private static byte[] BuildXlsxBytes(string csvShapedContent)
    {
        var lines = csvShapedContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        for (var row = 0; row < lines.Count; row++)
        {
            var cells = lines[row].Split(',');
            for (var col = 0; col < cells.Length; col++)
                worksheet.Cell(row + 1, col + 1).Value = cells[col];
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<ImportSession> SeedPendingSessionAsync(
        DataImportDbContext db,
        FakeImportFileStorageService storage,
        Guid companyId,
        string csvShapedContent,
        int totalRows,
        string storageKey = "sessions/abc/employees.xlsx")
    {
        var session = ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            "employees.xlsx",
            totalRows,
            Guid.NewGuid(),
            storageKey,
            XlsxContentType,
            FixedNowOffset);

        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();

        storage.SeedContent(storageKey, BuildXlsxBytes(csvShapedContent));

        return session;
    }

    private static async Task<ImportSession> SeedPendingSessionWithUnreadableFileAsync(
        DataImportDbContext db,
        FakeImportFileStorageService storage,
        Guid companyId,
        string storageKey = "sessions/abc/employees.xlsx")
    {
        var session = ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            "employees.xlsx",
            totalRows: 1,
            Guid.NewGuid(),
            storageKey,
            XlsxContentType,
            FixedNowOffset);

        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();

        storage.SeedContent(storageKey, "this is not a valid xlsx file"u8.ToArray());

        return session;
    }

    [Fact]
    public async Task HandleAsync_Successful_Validate_Deletes_The_Raw_File_And_Sets_FileDeletedAt()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var csv =
            StandardHeader + "\n" +
            $"John,Doe,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n";

        var storageKey = "sessions/abc/employees.xlsx";
        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 1, storageKey: storageKey);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(ImportStatus.Validated), result.Value!.Status);

        Assert.Contains(storageKey, storage.Deletions);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(savedSession.FileDeletedAt);
        Assert.Equal(0, savedSession.FileDeletionAttemptCount);
    }

    [Fact]
    public async Task HandleAsync_All_Rows_Invalid_Completes_With_Errors_And_Still_Deletes_The_Raw_File()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var csv =
            StandardHeader + "\n" +
            $"John,,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n";

        var storageKey = "sessions/abc/employees.xlsx";
        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 1, storageKey: storageKey);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(ImportStatus.CompletedWithErrors), result.Value!.Status);

        Assert.Contains(storageKey, storage.Deletions);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(savedSession.FileDeletedAt);
    }

    [Fact]
    public async Task HandleAsync_File_Read_Failure_Fails_The_Session_And_Still_Deletes_The_Raw_File()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var storageKey = "sessions/abc/employees.xlsx";
        var session = await SeedPendingSessionWithUnreadableFileAsync(db, storage, companyId, storageKey);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Contains(storageKey, storage.Deletions);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Failed, savedSession.Status);
        Assert.NotNull(savedSession.FileDeletedAt);
    }

    [Fact]
    public async Task HandleAsync_Transient_Deletion_Failure_Does_Not_Fail_The_Successful_Validate_Result()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var companyId = Guid.NewGuid();

        var csv =
            StandardHeader + "\n" +
            $"John,Doe,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n";

        var storageKey = "sessions/abc/employees.xlsx";
        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 1, storageKey: storageKey);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        // The overall validate outcome must not be affected by a deletion failure — it is a
        // best-effort cleanup concern, not a business outcome.
        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(ImportStatus.Validated), result.Value!.Status);

        Assert.DoesNotContain(storageKey, storage.Deletions);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(savedSession.FileDeletedAt);
        Assert.Equal(1, savedSession.FileDeletionAttemptCount);
        Assert.NotNull(savedSession.FileDeletionLastAttemptedAt);
    }

    [Fact]
    public async Task HandleAsync_Transient_Deletion_Failure_On_The_Read_Failure_Path_Still_Returns_Its_Own_Failure()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var companyId = Guid.NewGuid();

        var storageKey = "sessions/abc/employees.xlsx";
        var session = await SeedPendingSessionWithUnreadableFileAsync(db, storage, companyId, storageKey);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Failed, savedSession.Status);
        Assert.Null(savedSession.FileDeletedAt);
        Assert.Equal(1, savedSession.FileDeletionAttemptCount);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Attempt_Deletion_Again_If_FileDeletedAt_Already_Set()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var csv =
            StandardHeader + "\n" +
            $"John,Doe,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n";

        var storageKey = "sessions/abc/employees.xlsx";
        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 1, storageKey: storageKey);

        session.MarkFileDeleted(FixedNowOffset.AddMinutes(-30));
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(storage.Deletions);
    }
}
