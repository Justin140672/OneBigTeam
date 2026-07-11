using System.Text;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.ValidateImportSession;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

public class ValidateImportSessionHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNowOffset = new(FixedUtcNow, TimeSpan.Zero);

    private const string StandardHeader =
        "First Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile";

    // Appended to a data row (after Employee Number) to satisfy the mandatory
    // DateOfBirth/Nationality/Gender/Department/Location/EmploymentType/PositionProfile fields.
    private const string MandatoryFieldSuffix = "1990-01-01,British,Female,Engineering,London,Permanent,Developer";

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
                lookupResolver ?? new FakeImportLookupResolver()),
            new FakeClock(FixedUtcNow));

    // Pre-seeds a lookup resolver with the Department/EmploymentType/Location/PositionProfile
    // names used by MandatoryFieldSuffix, so validating rows with those mandatory lookup fields
    // resolves quietly instead of generating "did not exist and was created" warnings.
    private static FakeImportLookupResolver SeededResolver(Guid companyId)
    {
        var resolver = new FakeImportLookupResolver();
        resolver.SeedExistingDepartment(companyId, "Engineering", Guid.NewGuid());
        resolver.SeedExistingEmploymentType(companyId, "Permanent", Guid.NewGuid());
        resolver.SeedExistingLocation(companyId, "London", Guid.NewGuid());
        resolver.SeedExistingPositionProfile(companyId, "Developer", Guid.NewGuid());
        return resolver;
    }

    private static async Task<ImportSession> SeedPendingSessionAsync(
        DataImportDbContext db,
        FakeImportFileStorageService storage,
        Guid companyId,
        string csvContent,
        int totalRows,
        string storageKey = "sessions/abc/employees.csv")
    {
        var session = ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            "employees.csv",
            totalRows,
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
    public async Task HandleAsync_All_Valid_Rows_Completes_Session_With_All_Rows_Staged_As_Valid()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var csv =
            StandardHeader + "\n" +
            $"John,Doe,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n" +
            $"Jane,Doe,jane.doe@example.com,2026-01-02,EMP002,{MandatoryFieldSuffix}\n";

        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 2);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(session.Id, result.Value!.Id);
        Assert.Equal(nameof(ImportStatus.Validated), result.Value.Status);
        Assert.Equal(2, result.Value.TotalRows);
        Assert.Equal(2, result.Value.SuccessfulRows);
        Assert.Equal(0, result.Value.FailedRows);

        var staged = await db.ImportStagingEmployees
            .Where(s => s.ImportSessionId == session.Id)
            .OrderBy(s => s.RowNumber)
            .ToListAsync();

        Assert.Equal(2, staged.Count);
        Assert.All(staged, s => Assert.True(s.IsValid));
        Assert.Equal(2, staged[0].RowNumber);
        Assert.Equal("EMP001", staged[0].EmployeeNumber);
        Assert.Equal("john.doe@example.com", staged[0].WorkEmail);
        Assert.Equal(3, staged[1].RowNumber);

        Assert.Empty(await db.ImportRowErrors.Where(e => e.ImportSessionId == session.Id).ToListAsync());

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Validated, savedSession.Status);
    }

    [Fact]
    public async Task HandleAsync_Mixed_Valid_And_Invalid_Rows_Completes_With_Errors_And_Persists_Row_Errors()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var csv =
            StandardHeader + "\n" +
            $"John,Doe,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n" +
            $"Jane,,jane.doe@example.com,2026-01-02,EMP002,{MandatoryFieldSuffix}\n"; // row 3: missing Last Name

        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 2);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(ImportStatus.Validated), result.Value!.Status);
        Assert.Equal(2, result.Value.TotalRows);
        Assert.Equal(1, result.Value.SuccessfulRows);
        Assert.Equal(1, result.Value.FailedRows);

        var staged = await db.ImportStagingEmployees
            .Where(s => s.ImportSessionId == session.Id)
            .OrderBy(s => s.RowNumber)
            .ToListAsync();

        Assert.Equal(2, staged.Count);
        Assert.True(staged.Single(s => s.RowNumber == 2).IsValid);
        Assert.False(staged.Single(s => s.RowNumber == 3).IsValid);

        var rowErrors = await db.ImportRowErrors
            .Where(e => e.ImportSessionId == session.Id)
            .ToListAsync();

        var rowError = Assert.Single(rowErrors);
        Assert.Equal(3, rowError.RowNumber);
        Assert.Contains("'LastName' is required.", rowError.ErrorMessage);
        Assert.Equal(ImportRowErrorSeverity.Error, rowError.Severity);

        var savedSession = await db.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(ImportStatus.Validated, savedSession.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Session_Is_Not_Pending()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        var csv = StandardHeader + "\n" + "John,Doe,john.doe@example.com,2026-01-01,EMP001\n";
        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 1);

        // Move the session past Pending before calling validate.
        session.Start(FixedNowOffset);
        session.Complete(successfulRows: 1, failedRows: 0, FixedNowOffset);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        Assert.Empty(await db.ImportStagingEmployees.Where(s => s.ImportSessionId == session.Id).ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Session_Belongs_To_A_Different_Company()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var ownerCompanyId = Guid.NewGuid();
        var callerCompanyId = Guid.NewGuid();

        var csv = StandardHeader + "\n" + "John,Doe,john.doe@example.com,2026-01-01,EMP001\n";
        var session = await SeedPendingSessionAsync(db, storage, ownerCompanyId, csv, totalRows: 1);

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = callerCompanyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        Assert.Empty(await db.ImportStagingEmployees.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Header_Not_Matching_Standard_Name_Fails_Required_Field_When_No_ColumnMapping_Supplied()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        // File uses "Given Name" instead of the standard "First Name" header, and no
        // ColumnMapping is supplied, so FirstName is never mapped from this file.
        var csv =
            $"Given Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            $"John,Doe,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n";

        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 1);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.SuccessfulRows);
        Assert.Equal(1, result.Value.FailedRows);

        var rowError = await db.ImportRowErrors.SingleAsync(
            e => e.ImportSessionId == session.Id && e.Severity == ImportRowErrorSeverity.Error);
        Assert.Contains("'FirstName' is required.", rowError.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ColumnMapping_Override_Allows_Nonstandard_Header_To_Parse_And_Validate_Correctly()
    {
        await using var db = BuildContext();
        var storage = new FakeImportFileStorageService();
        var companyId = Guid.NewGuid();

        // Same nonstandard "Given Name" header as above, but this time a ColumnMapping override
        // redirects FirstName to read from that header instead of the default "First Name".
        var csv =
            $"Given Name,Last Name,Work Email,Start Date,Employee Number,Date Of Birth,Nationality,Gender,Department,Location,Employment Type,Position Profile\n" +
            $"John,Doe,john.doe@example.com,2026-01-01,EMP001,{MandatoryFieldSuffix}\n";

        var session = await SeedPendingSessionAsync(db, storage, companyId, csv, totalRows: 1);
        var handler = BuildHandler(db, storage, lookupResolver: SeededResolver(companyId));

        var result = await handler.HandleAsync(
            new ValidateImportSessionRequest
            {
                CompanyId = companyId,
                ImportSessionId = session.Id,
                ColumnMapping = new Dictionary<string, string> { ["FirstName"] = "Given Name" },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(ImportStatus.Validated), result.Value!.Status);
        Assert.Equal(1, result.Value.SuccessfulRows);
        Assert.Equal(0, result.Value.FailedRows);

        Assert.Empty(await db.ImportRowErrors.Where(e => e.ImportSessionId == session.Id).ToListAsync());
    }
}
