// SEA-08: Search security matrix — recruitment search cross-company isolation,
// consistent out-of-range page behaviour and search term validation.
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListCandidates;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class RecruitmentSearchSecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    // ── Cross-company isolation ────────────────────────────────────────────

    [Fact]
    public async Task ListCandidates_TotalCount_Excludes_Other_Company_Records()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        db.Candidates.AddRange(
            Candidate.Create(Guid.NewGuid(), companyA, "Alice", "Smith", "alice@a.example", null, null, Now),
            Candidate.Create(Guid.NewGuid(), companyA, "Bob",   "Jones", "bob@a.example",   null, null, Now),
            Candidate.Create(Guid.NewGuid(), companyB, "Carol", "Other", "carol@b.example", null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalCount);
        Assert.All(result.Value.Items, i => Assert.NotEqual("Carol", i.FirstName));
    }

    [Fact]
    public async Task ListCandidates_Search_Does_Not_Surface_Other_Company_Candidates()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        db.Candidates.AddRange(
            Candidate.Create(Guid.NewGuid(), companyA, "Alice", "Smith", "alice@a.example", null, null, Now),
            Candidate.Create(Guid.NewGuid(), companyB, "Alice", "Jones", "alice@b.example", null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = companyA, Search = "alice" },
            CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("Smith", result.Value.Items[0].LastName);
    }

    // ── Out-of-range page behaviour ────────────────────────────────────────

    [Fact]
    public async Task ListCandidates_Out_Of_Range_Page_Returns_Empty_Items_With_Correct_TotalCount()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.Candidates.Add(
            Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@test.example", null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = companyId, PageNumber = 999, PageSize = 20 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    // ── Search term validation ─────────────────────────────────────────────

    [Fact]
    public void ListCandidates_Validator_Rejects_Oversized_Search_Term()
    {
        var result = new ListCandidatesValidator().Validate(new ListCandidatesRequest
        {
            CompanyId = Guid.NewGuid(),
            Search = new string('x', 201),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListCandidatesRequest.Search));
    }

    [Fact]
    public void ListCandidates_Validator_Rejects_Zero_PageNumber()
    {
        var result = new ListCandidatesValidator().Validate(new ListCandidatesRequest
        {
            CompanyId = Guid.NewGuid(),
            PageNumber = 0,
        });

        Assert.False(result.IsValid);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
