using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Tests;

public class CandidateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Trims_And_Normalises_Optional_Fields()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), " Emma ", " Clarke ", " emma.clarke@example.com ", "  ", "   ", Now);

        Assert.Equal("Emma", candidate.FirstName);
        Assert.Equal("Clarke", candidate.LastName);
        Assert.Equal("emma.clarke@example.com", candidate.Email);
        Assert.Null(candidate.Phone);
        Assert.Null(candidate.ResumeUrl);
    }

    [Fact]
    public void UpdateDetails_Overwrites_Fields_And_UpdatedAt()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var later = Now.AddDays(1);

        candidate.UpdateDetails("Emma", "Clarke-Smith", "emma.clarke-smith@example.com", "+44 7700 900001", "https://example.com/resume.pdf", later);

        Assert.Equal("Clarke-Smith", candidate.LastName);
        Assert.Equal("emma.clarke-smith@example.com", candidate.Email);
        Assert.Equal("+44 7700 900001", candidate.Phone);
        Assert.Equal("https://example.com/resume.pdf", candidate.ResumeUrl);
        Assert.Equal(later, candidate.UpdatedAt);
    }
}
