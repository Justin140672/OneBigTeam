using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.GetImportSession;
using HR.Modules.DataImport.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

public class GetImportSessionHandlerTests
{
    private static readonly DateTimeOffset FixedNowOffset = new(2026, 6, 20, 9, 0, 0, TimeSpan.Zero);

    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_Correct_Field_Mapping_From_Entity()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var session = ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            "employees.csv",
            totalRows: 5,
            Guid.NewGuid(),
            "sessions/abc/employees.csv",
            "text/csv",
            FixedNowOffset);

        session.Start(FixedNowOffset.AddMinutes(1));
        session.Validate(successfulRows: 4, failedRows: 1, FixedNowOffset.AddMinutes(2));

        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();

        var handler = new GetImportSessionHandler(db);

        var result = await handler.HandleAsync(
            new GetImportSessionRequest { CompanyId = companyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(session.Id, response.Id);
        Assert.Equal("Employees", response.EntityType);
        Assert.Equal("employees.csv", response.FileName);
        Assert.Equal(nameof(ImportStatus.Validated), response.Status);
        Assert.Equal(5, response.TotalRows);
        Assert.Equal(5, response.ProcessedRows);
        Assert.Equal(4, response.SuccessfulRows);
        Assert.Equal(1, response.FailedRows);
        Assert.Equal(session.StartedAt, response.StartedAt);
        Assert.Equal(session.CompletedAt, response.CompletedAt);
        Assert.Null(response.ErrorSummary);
        Assert.Equal(session.CreatedAt, response.CreatedAt);
        Assert.Equal(session.UpdatedAt, response.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Missing_Session()
    {
        await using var db = BuildContext();

        var handler = new GetImportSessionHandler(db);

        var result = await handler.HandleAsync(
            new GetImportSessionRequest { CompanyId = Guid.NewGuid(), ImportSessionId = Guid.NewGuid() },
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

        var session = ImportSession.Create(
            Guid.NewGuid(),
            ownerCompanyId,
            "Employees",
            "employees.csv",
            totalRows: 1,
            Guid.NewGuid(),
            "sessions/abc/employees.csv",
            "text/csv",
            FixedNowOffset);

        db.ImportSessions.Add(session);
        await db.SaveChangesAsync();

        var handler = new GetImportSessionHandler(db);

        var result = await handler.HandleAsync(
            new GetImportSessionRequest { CompanyId = callerCompanyId, ImportSessionId = session.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
