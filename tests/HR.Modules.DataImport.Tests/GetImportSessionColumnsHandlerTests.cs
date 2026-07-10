using System.Text;
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

    private static async Task<ImportSession> SeedSessionAsync(
        DataImportDbContext db,
        FakeImportFileStorageService storage,
        Guid companyId,
        string csvContent,
        string storageKey = "sessions/abc/employees.csv")
    {
        var session = ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            "employees.csv",
            totalRows: 1,
            Guid.NewGuid(),
            storageKey,
            "text/csv",
            FixedNowOffset);

        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();

        storage.SeedContent(storageKey, Encoding.UTF8.GetBytes(csvContent));

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
