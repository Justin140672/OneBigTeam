using ClosedXML.Excel;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.UploadImportFile;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
            new FakeClock(FixedUtcNow));

    private static IFormFile FakeFile(string fileName, string contentType, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    // A CSV with a header row and N data rows.
    private static byte[] CsvBytes(int dataRowCount)
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

    private static UploadImportFileRequest BuildRequest(
        Guid companyId,
        string entityType = "Employees",
        IFormFile? file = null) => new()
    {
        CompanyId  = companyId,
        EntityType = entityType,
        File       = file ?? FakeFile("employees.csv", "text/csv", CsvBytes(3)),
    };

    [Fact]
    public async Task HandleAsync_ValidCsv_CreatesSession_With_Correct_TotalRows_And_Pending_Status()
    {
        await using var db = BuildContext();
        var storage        = new FakeImportFileStorageService();
        var companyId      = Guid.NewGuid();
        var initiatedBy    = Guid.NewGuid();
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, file: FakeFile("employees.csv", "text/csv", CsvBytes(3))),
            initiatedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Employees", result.Value.EntityType);
        Assert.Equal("employees.csv", result.Value.FileName);
        Assert.Equal(3, result.Value.TotalRows); // header row excluded
        Assert.Equal(nameof(ImportStatus.Pending), result.Value.Status);

        var saved = await db.ImportSessions.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(3, saved.TotalRows);
        Assert.Equal(ImportStatus.Pending, saved.Status);
        Assert.Equal(initiatedBy, saved.InitiatedByUserId);
        Assert.Equal("text/csv", saved.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(saved.StorageKey));

        Assert.Single(storage.Uploads);
        Assert.Equal("employees.csv", storage.Uploads[0].FileName);
    }

    [Fact]
    public async Task HandleAsync_ValidXlsx_CreatesSession_With_Correct_TotalRows()
    {
        await using var db = BuildContext();
        var storage        = new FakeImportFileStorageService();
        var companyId      = Guid.NewGuid();
        var handler        = BuildHandler(db, storage);

        var file = FakeFile(
            "employees.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            XlsxBytes(5));

        var result = await handler.HandleAsync(
            BuildRequest(companyId, file: file),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.TotalRows); // header row excluded
        Assert.Equal("employees.xlsx", result.Value.FileName);

        var saved = await db.ImportSessions.SingleAsync();
        Assert.Equal(5, saved.TotalRows);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", saved.ContentType);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Too_Large()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db, options: new ImportFileUploadOptions { MaxFileSizeBytes = 10 });

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.csv", "text/csv", CsvBytes(3))),
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
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.txt", "text/plain", CsvBytes(3))),
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
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.csv", "application/json", CsvBytes(3))),
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

        // Named and declared as XLSX but the bytes are not a ZIP/OOXML container (spoofed/renamed CSV).
        var spoofedFile = FakeFile(
            "employees.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            CsvBytes(3));

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
    public async Task HandleAsync_Empty_Csv_Produces_Zero_TotalRows()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var file = FakeFile("employees.csv", "text/csv", CsvBytes(0)); // header row only

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: file),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalRows);
    }

    [Fact]
    public async Task HandleAsync_When_SaveChangesAsync_Fails_Deletes_The_Uploaded_File()
    {
        // DataImportDbContext is sealed (matching the dominant convention across this codebase's
        // modules — Documents/Recruitment are the outliers, not the standard), so the usual
        // "subclass and override SaveChangesAsync" trick isn't available here. Instead, point the
        // context at a connection that genuinely fails when SaveChangesAsync opens it (nothing
        // listens on 127.0.0.1:1, so the OS refuses the connection almost instantly) to force a
        // real save failure without subclassing or weakening the module's DbContext sealing.
        await using var db = new DataImportDbContext(
            new DbContextOptionsBuilder<DataImportDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=edge_case;Username=x;Password=x;Timeout=2;Command Timeout=2")
                .Options);

        var storage = new FakeImportFileStorageService();
        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), file: FakeFile("employees.csv", "text/csv", CsvBytes(3))),
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);
    }
}
