using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class UpdateCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Updates_Candidate_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateCandidateRequest
            {
                CompanyId   = companyId,
                CandidateId = candidate.Id,
                FirstName   = "Emma",
                LastName    = "Clarke-Smith",
                Email       = "emma.clarke-smith@example.com",
                Phone       = "+44 7700 900001",
                ResumeUrl   = "https://example.com/resume.pdf",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Clarke-Smith", result.Value!.LastName);
        Assert.Equal("emma.clarke-smith@example.com", result.Value.Email);
        Assert.Equal("+44 7700 900001", result.Value.Phone);
        Assert.Equal("https://example.com/resume.pdf", result.Value.ResumeUrl);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new UpdateCandidateRequest
            {
                CompanyId   = Guid.NewGuid(),
                CandidateId = Guid.NewGuid(),
                FirstName   = "Emma",
                LastName    = "Clarke",
                Email       = "emma.clarke@example.com",
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_New_Email_Belongs_To_Another_Candidate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var emma = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var liam = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        db.Candidates.AddRange(emma, liam);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateCandidateRequest
            {
                CompanyId   = companyId,
                CandidateId = emma.Id,
                FirstName   = "Emma",
                LastName    = "Clarke",
                Email       = "liam.turner@example.com",
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Keeping_Same_Email()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateCandidateRequest
            {
                CompanyId   = companyId,
                CandidateId = candidate.Id,
                FirstName   = "Emma",
                LastName    = "Clarke",
                Email       = "emma.clarke@example.com",
                Phone       = "+44 7700 900001",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("+44 7700 900001", result.Value!.Phone);
    }

    private static UpdateCandidateHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
