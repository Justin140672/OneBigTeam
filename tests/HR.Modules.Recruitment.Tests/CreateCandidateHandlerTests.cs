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

    [Theory]
    [InlineData("NOAH.PATEL@EXAMPLE.COM")]
    [InlineData("  noah.patel@example.com  ")]
    [InlineData(" Noah.Patel@Example.com\t")]
    public async Task HandleAsync_Returns_Duplicate_When_Case_Or_Whitespace_Variant_Exists_In_Company(string variant)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var first = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = companyId, FirstName = "Noah", LastName = "Patel", Email = "noah.patel@example.com" },
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = companyId, FirstName = "Noah", LastName = "P.", Email = variant },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        var duplicate = Assert.IsType<CreateCandidateDuplicateCandidateResponse>(result.DuplicateCandidate);
        Assert.Equal("candidate_email_exists", duplicate.Code);
        Assert.Equal(CreateCandidateDuplicateCandidateResponse.ErrorCode, duplicate.Code);
        Assert.Equal(first.Value!.Id, duplicate.ExistingCandidateId);
        Assert.Equal("Noah", duplicate.ExistingCandidateFirstName);
        Assert.Equal("Patel", duplicate.ExistingCandidateLastName);
        Assert.Equal("noah.patel@example.com", duplicate.ExistingCandidateEmail);
        Assert.True(duplicate.ExistingCandidateIsActive);

        var saved = Assert.Single(await db.Candidates.ToListAsync());
        Assert.Equal(first.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Reports_Existing_Candidate_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var existing = HR.Modules.Recruitment.Domain.Candidate.Create(
            Guid.NewGuid(), companyId, "Ava", "Jones", "Ava.Jones@Example.com", null, new DateTimeOffset(FixedUtcNow));
        existing.Deactivate(Guid.NewGuid(), "No longer looking", new DateTimeOffset(FixedUtcNow));
        db.Candidates.Add(existing);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = companyId, FirstName = "Ava", LastName = "Jones", Email = "ava.jones@example.com" },
            CancellationToken.None);

        var duplicate = Assert.IsType<CreateCandidateDuplicateCandidateResponse>(result.DuplicateCandidate);
        Assert.Equal(existing.Id, duplicate.ExistingCandidateId);
        Assert.Equal("Ava.Jones@Example.com", duplicate.ExistingCandidateEmail);
        Assert.False(duplicate.ExistingCandidateIsActive);
        Assert.Single(await db.Candidates.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Success_Has_No_DuplicateCandidate_And_Persists_NormalisedEmail()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = Guid.NewGuid(), FirstName = "Mia", LastName = "Wong", Email = "  Mia.Wong@Example.COM " },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.DuplicateCandidate);
        Assert.Equal("Mia.Wong@Example.COM", result.Value!.Email);

        var saved = await db.Candidates.SingleAsync();
        Assert.Equal("mia.wong@example.com", saved.NormalisedEmail);
    }

    [Fact]
    public async Task HandleAsync_Allows_Case_Variant_Email_In_Different_Company()
    {
        await using var db = BuildContext();

        var first = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = Guid.NewGuid(), FirstName = "Olivia", LastName = "Grant", Email = "olivia.grant@example.com" },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateCandidateRequest { CompanyId = Guid.NewGuid(), FirstName = "Olivia", LastName = "Grant", Email = "  OLIVIA.GRANT@Example.com " },
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(result.IsSuccess);
        Assert.Null(result.DuplicateCandidate);
        Assert.Equal(2, await db.Candidates.CountAsync());
    }

    private static CreateCandidateHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
