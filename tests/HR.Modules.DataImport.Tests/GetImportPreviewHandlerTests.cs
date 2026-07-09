using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.GetImportPreview;
using HR.Modules.DataImport.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

public class GetImportPreviewHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ImportSession SeedSession(DataImportDbContext db, Guid companyId, int totalRows = 2)
    {
        var session = ImportSession.Create(
            Guid.NewGuid(), companyId, "Employee", "employees.csv", totalRows, Guid.NewGuid(),
            "sessions/abc/employees.csv", "text/csv", FixedNow);
        db.ImportSessions.Add(session);
        db.SaveChanges();
        return session;
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new GetImportPreviewHandler(db);

        var result = await handler.HandleAsync(
            new GetImportPreviewRequest { CompanyId = Guid.NewGuid(), ImportSessionId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var session = SeedSession(db, Guid.NewGuid());
        var handler = new GetImportPreviewHandler(db);

        var result = await handler.HandleAsync(
            new GetImportPreviewRequest { CompanyId = Guid.NewGuid(), ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Splits_Valid_And_Invalid_Rows_And_Returns_Counts()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 2);

        var validRaw = """{"FirstName":"John","LastName":"Doe","StartDate":"2026-01-01"}""";
        var invalidRaw = """{"FirstName":"Jane"}""";

        db.ImportStagingEmployees.Add(ImportStagingEmployee.Create(
            Guid.NewGuid(), companyId, session.Id, 2, "EMP001", "john@example.com", null,
            null, null, null, null, validRaw, isValid: true, FixedNow));
        db.ImportStagingEmployees.Add(ImportStagingEmployee.Create(
            Guid.NewGuid(), companyId, session.Id, 3, null, "jane@example.com", null,
            null, null, null, null, invalidRaw, isValid: false, FixedNow));
        await db.SaveChangesAsync();

        var handler = new GetImportPreviewHandler(db);

        var result = await handler.HandleAsync(
            new GetImportPreviewRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(session.Id, result.Value!.ImportSessionId);
        Assert.Equal(1, result.Value.ValidRowCount);
        Assert.Equal(1, result.Value.InvalidRowCount);
        var validRow = Assert.Single(result.Value.ValidRows);
        Assert.Equal(2, validRow.RowNumber);
        Assert.Equal("John", validRow.FirstName);
        Assert.Equal("Doe", validRow.LastName);
        Assert.Equal("john@example.com", validRow.WorkEmail);
    }

    [Fact]
    public async Task HandleAsync_Splits_ReferenceDataCreated_Warnings_From_Other_Row_Errors()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 1);

        db.ImportRowErrors.Add(ImportRowError.Create(
            Guid.NewGuid(), companyId, session.Id, 2, ImportRowErrorSeverity.Warning,
            "Department 'Sales' did not exist and was created.", null, FixedNow));
        db.ImportRowErrors.Add(ImportRowError.Create(
            Guid.NewGuid(), companyId, session.Id, 3, ImportRowErrorSeverity.Error,
            "'FirstName' is required.", null, FixedNow));
        db.ImportRowErrors.Add(ImportRowError.Create(
            Guid.NewGuid(), companyId, session.Id, 4, ImportRowErrorSeverity.Warning,
            "Manager reference 'x' could not be assigned.", null, FixedNow));
        await db.SaveChangesAsync();

        var handler = new GetImportPreviewHandler(db);

        var result = await handler.HandleAsync(
            new GetImportPreviewRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var referenceWarning = Assert.Single(result.Value!.ReferenceDataCreatedWarnings);
        Assert.Equal(2, referenceWarning.RowNumber);
        Assert.Contains("did not exist and was created", referenceWarning.Message);

        Assert.Equal(2, result.Value.RowErrors.Count);
        Assert.Contains(result.Value.RowErrors, e => e.RowNumber == 3);
        Assert.Contains(result.Value.RowErrors, e => e.RowNumber == 4);
    }

    [Fact]
    public async Task HandleAsync_Returns_Session_Status_And_TotalRows()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var session = SeedSession(db, companyId, totalRows: 5);
        session.Start(FixedNow);
        session.Validate(successfulRows: 5, failedRows: 0, FixedNow.AddMinutes(1));
        await db.SaveChangesAsync();

        var handler = new GetImportPreviewHandler(db);

        var result = await handler.HandleAsync(
            new GetImportPreviewRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Validated", result.Value!.Status);
        Assert.Equal(5, result.Value.TotalRows);
    }
}
