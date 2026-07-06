using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetCandidate;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetCandidateHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Candidate_When_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await new GetCandidateHandler(db).HandleAsync(
            new GetCandidateRequest { CompanyId = companyId, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(candidate.Id, result.Value!.Id);
        Assert.Equal("emma.clarke@example.com", result.Value.Email);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();

        var result = await new GetCandidateHandler(db).HandleAsync(
            new GetCandidateRequest { CompanyId = Guid.NewGuid(), CandidateId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await new GetCandidateHandler(db).HandleAsync(
            new GetCandidateRequest { CompanyId = Guid.NewGuid(), CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
