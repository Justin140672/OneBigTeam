using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListCandidates;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ListCandidatesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Candidates_For_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.Candidates.AddRange(
            Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now),
            Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_LastName_Then_FirstName()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.Candidates.AddRange(
            Candidate.Create(Guid.NewGuid(), companyId, "Zoe", "Adams", "zoe.adams@example.com", null, null, Now),
            Candidate.Create(Guid.NewGuid(), companyId, "Amy", "Baker", "amy.baker@example.com", null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = companyId },
            CancellationToken.None);

        var names = result.Value!.Items.Select(i => i.LastName).ToList();
        Assert.Equal(["Adams", "Baker"], names);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Search_Term_Matching_Name_Or_Email()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.Candidates.AddRange(
            Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now),
            Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = companyId, Search = "clarke" },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Clarke", result.Value.Items[0].LastName);
    }

    [Fact]
    public async Task HandleAsync_Paginates_Results()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            db.Candidates.Add(Candidate.Create(Guid.NewGuid(), companyId, $"First{i}", $"Last{i}", $"candidate{i}@example.com", null, null, Now));
        }
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = companyId, PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Candidates_From_Other_Companies()
    {
        await using var db = BuildContext();

        db.Candidates.AddRange(
            Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now),
            Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Liam", "Turner", "liam.turner@example.com", null, null, Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidatesHandler(db).HandleAsync(
            new ListCandidatesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
