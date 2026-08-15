using ClosedXML.Excel;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.GetImportSessionColumns;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

public class GetImportSessionColumnsHandlerTests
{
    private static readonly DateTimeOffset FixedNowOffset = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);

    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetImportSessionColumnsHandler BuildHandler(
        DataImportDbContext db, FakeImportFileStorageService storage) =>
        new(db, storage, new EmployeeImportFileParser());

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Builds a minimal XLSX workbook (via ClosedXML) from comma-delimited "csv-shaped" header/data
    // lines, so existing test fixtures (written as csv-style strings for readability) can still be
    // used against the now xlsx-only parser.
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

    private static async Task<ImportSession> SeedSessionAsync(
        DataImportDbContext db,
        FakeImportFileStorageService storage,
        Guid companyId,
        string csvShapedContent,
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

        storage.SeedContent(storageKey, BuildXlsxBytes(csvShapedContent));

        return session;
    }

    [Fact]
    public async Task HandleAsync_Exact_Header_Match_Produces_Suggestion_For_Every_Target_Field()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var headerLine = string.Join(',', StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName.Values);
        var session = await SeedSessionAsync(db, storage, companyId, headerLine + "\n");

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new GetImportSessionColumnsRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(session.Id, result.Value!.ImportSessionId);
        Assert.Equal(
            StandardEmployeeColumnMapping.Default.TargetFieldToHeaderName.Count,
            result.Value.FieldSuggestions.Count);

        foreach (var suggestion in result.Value.FieldSuggestions)
        {
            Assert.Equal(suggestion.StandardHeaderName, suggestion.SuggestedHeader);
        }
    }

    [Theory]
    [InlineData("First  Name")] // extra internal space
    [InlineData("first_name")] // underscore, different casing
    public async Task HandleAsync_Nonexact_Header_Still_Gets_Suggested_Via_Normalized_Match(string actualHeader)
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var csv = $"{actualHeader},Last Name,Work Email,Start Date,Employee Number\n";
        var session = await SeedSessionAsync(db, storage, companyId, csv);

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new GetImportSessionColumnsRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var firstNameSuggestion = result.Value!.FieldSuggestions.Single(s => s.TargetField == "FirstName");
        Assert.Equal(actualHeader, firstNameSuggestion.SuggestedHeader);
    }

    [Fact]
    public async Task HandleAsync_Column_Missing_From_File_Leaves_SuggestedHeader_Null()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        // "Work Email" is not present anywhere in the file.
        var csv = "First Name,Last Name,Start Date,Employee Number\n";
        var session = await SeedSessionAsync(db, storage, companyId, csv);

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new GetImportSessionColumnsRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var workEmailSuggestion = result.Value!.FieldSuggestions.Single(s => s.TargetField == "WorkEmail");
        Assert.Null(workEmailSuggestion.SuggestedHeader);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new GetImportSessionColumnsRequest { CompanyId = Guid.NewGuid(), ImportSessionId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var ownerCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();

        var csv = "First Name,Last Name,Work Email,Start Date,Employee Number\n";
        var session = await SeedSessionAsync(db, storage, ownerCompanyId, csv);

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new GetImportSessionColumnsRequest { CompanyId = callerCompanyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
