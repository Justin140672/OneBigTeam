using HR.Modules.Recruitment.Features.CreateCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CreateCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Candidate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest
            {
                CompanyId = companyId,
                FirstName = "Emma",
                LastName  = "Clarke",
                Email     = "emma.clarke@example.com",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Emma", result.Value.FirstName);
        Assert.Equal("Clarke", result.Value.LastName);
        Assert.Equal("emma.clarke@example.com", result.Value.Email);

        var saved = await db.Candidates.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Trims_Email()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest
            {
                CompanyId = Guid.NewGuid(),
                FirstName = "Liam",
                LastName  = "Turner",
                Email     = "  liam.turner@example.com  ",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("liam.turner@example.com", result.Value!.Email);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Email_Already_Exists_In_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = companyId, FirstName = "Noah", LastName = "Patel", Email = "noah.patel@example.com" },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = companyId, FirstName = "Noah", LastName = "P.", Email = "noah.patel@example.com" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Email_In_Different_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = companyA, FirstName = "Olivia", LastName = "Grant", Email = "olivia.grant@example.com" },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = companyB, FirstName = "Olivia", LastName = "Grant", Email = "olivia.grant@example.com" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static CreateCandidateHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
