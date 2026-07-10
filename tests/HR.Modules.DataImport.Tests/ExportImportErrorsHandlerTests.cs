using System.Text;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.ExportImportErrors;
using HR.Modules.DataImport.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

public class ExportImportErrorsHandlerTests
{
    private static readonly DateTimeOffset FixedNowOffset = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);

    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<ImportSession> SeedSessionAsync(DataImportDbContext db, Guid companyId)
    {
        var session = ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            "employees.csv",
            totalRows: 2,
            Guid.NewGuid(),
            "sessions/abc/employees.csv",
            "text/csv",
            FixedNowOffset);

        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();

        return session;
    }

    [Fact]
    public async Task HandleAsync_Produces_Correctly_Ordered_And_Escaped_Csv_Bytes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = await SeedSessionAsync(db, companyId);

        // Added out of RowNumber order to verify the handler orders by RowNumber, not insertion order.
        db.ImportRowErrors.Add(ImportRowError.Create(
            Guid.NewGuid(), companyId, session.Id, rowNumber: 3, ImportRowErrorSeverity.Error,
            "'LastName' is required, and 'WorkEmail' too", "FirstName=Jane", FixedNowOffset));

        db.ImportRowErrors.Add(ImportRowError.Create(
            Guid.NewGuid(), companyId, session.Id, rowNumber: 2, ImportRowErrorSeverity.Warning,
            "Department 'Sales' did not exist and was created.", "FirstName=John", FixedNowOffset));

        await db.SaveChangesAsync();

        var handler = new ExportImportErrorsHandler(db);

        var result = await handler.HandleAsync(
            new ExportImportErrorsRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var csv = Encoding.UTF8.GetString(result.Value!);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal("RowNumber,Severity,ErrorMessage,RawRowData", lines[0]);
        Assert.Equal("2,Warning,Department 'Sales' did not exist and was created.,FirstName=John", lines[1]);
        Assert.Equal("3,Error,\"'LastName' is required, and 'WorkEmail' too\",FirstName=Jane", lines[2]);
    }

    [Fact]
    public async Task HandleAsync_Zero_Errors_Returns_Valid_Csv_With_Just_Header_Row()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = await SeedSessionAsync(db, companyId);

        var handler = new ExportImportErrorsHandler(db);

        var result = await handler.HandleAsync(
            new ExportImportErrorsRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var csv = Encoding.UTF8.GetString(result.Value!);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var line = Assert.Single(lines);
        Assert.Equal("RowNumber,Severity,ErrorMessage,RawRowData", line);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var handler = new ExportImportErrorsHandler(db);

        var result = await handler.HandleAsync(
            new ExportImportErrorsRequest { CompanyId = Guid.NewGuid(), ImportSessionId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var ownerCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();
        var session = await SeedSessionAsync(db, ownerCompanyId);

        var handler = new ExportImportErrorsHandler(db);

        var result = await handler.HandleAsync(
            new ExportImportErrorsRequest { CompanyId = callerCompanyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
