using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;

namespace HR.Modules.Recruitment.Tests;

public class CandidateEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("  Emma.Clarke@Example.COM \t", "emma.clarke@example.com")]
    [InlineData("EMMA.CLARKE@EXAMPLE.COM", "emma.clarke@example.com")]
    [InlineData("emma.clarke@example.com", "emma.clarke@example.com")]
    [InlineData("\r\n emma.clarke@example.com\n", "emma.clarke@example.com")]
    public void Normalise_Trims_And_Lowercases(string input, string expected)
    {
        Assert.Equal(expected, CandidateEmail.Normalise(input));
    }

    [Fact]
    public void Normalise_Does_Not_Apply_Provider_Specific_Rules()
    {
        // Dots and plus-addressing identify genuinely different mailboxes; they must not be merged.
        Assert.Equal("emma.clarke+jobs@example.com", CandidateEmail.Normalise("Emma.Clarke+Jobs@Example.com"));
        Assert.NotEqual(CandidateEmail.Normalise("emmaclarke@example.com"), CandidateEmail.Normalise("emma.clarke@example.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Normalise_Empty_Or_Whitespace_Returns_Empty(string input)
    {
        Assert.Equal(string.Empty, CandidateEmail.Normalise(input));
    }

    [Fact]
    public void Normalise_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CandidateEmail.Normalise(null!));
    }

    [Fact]
    public void Normalise_Is_Idempotent()
    {
        var once = CandidateEmail.Normalise("  Mixed.Case@Example.COM ");
        Assert.Equal(once, CandidateEmail.Normalise(once));
    }

    [Fact]
    public void Create_Sets_NormalisedEmail_While_Email_Keeps_Original_Case_Trimmed()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "  Emma.Clarke@Example.COM \t", null, Now);

        Assert.Equal("Emma.Clarke@Example.COM", candidate.Email);
        Assert.Equal("emma.clarke@example.com", candidate.NormalisedEmail);
    }

    [Fact]
    public void UpdateDetails_Refreshes_NormalisedEmail()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, Now);

        candidate.UpdateDetails("Emma", "Clarke", " Emma.Clarke-Smith@EXAMPLE.com ", null, Now.AddMinutes(1));

        Assert.Equal("Emma.Clarke-Smith@EXAMPLE.com", candidate.Email);
        Assert.Equal("emma.clarke-smith@example.com", candidate.NormalisedEmail);
    }

    [Fact]
    public void UpdateDetails_Case_Only_Change_Keeps_NormalisedEmail_And_Updates_Email_Display()
    {
        var candidate = Candidate.Create(Guid.NewGuid(), Guid.NewGuid(), "Emma", "Clarke", "emma.clarke@example.com", null, Now);

        candidate.UpdateDetails("Emma", "Clarke", "Emma.Clarke@Example.com", null, Now.AddMinutes(1));

        Assert.Equal("Emma.Clarke@Example.com", candidate.Email);
        Assert.Equal("emma.clarke@example.com", candidate.NormalisedEmail);
    }

    [Fact]
    public void Purge_Sets_NormalisedEmail_To_Purged_Placeholder()
    {
        var id = Guid.NewGuid();
        var candidate = Candidate.Create(id, Guid.NewGuid(), "Emma", "Clarke", "Emma.Clarke@Example.com", null, Now);

        candidate.Purge(Guid.NewGuid(), Now.AddDays(400));

        var expected = $"purged-{id:N}@purged.invalid";
        Assert.Equal(expected, candidate.Email);
        Assert.Equal(expected, candidate.NormalisedEmail);
    }

    [Fact]
    public void LockKey_Has_Stable_Format()
    {
        var companyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal(
            "recruitment:candidate-email:11111111222233334444555555555555:emma.clarke@example.com",
            CandidateEmailUniqueness.LockKey(companyId, "emma.clarke@example.com"));
    }

    [Fact]
    public void LockKey_Is_Equal_For_Case_And_Whitespace_Variants_After_Normalisation()
    {
        var companyId = Guid.NewGuid();

        var a = CandidateEmailUniqueness.LockKey(companyId, CandidateEmail.Normalise("x@example.com"));
        var b = CandidateEmailUniqueness.LockKey(companyId, CandidateEmail.Normalise("  X@EXAMPLE.COM "));

        Assert.Equal(a, b);
    }

    [Fact]
    public void LockKey_Differs_Between_Companies_And_Emails()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        Assert.NotEqual(
            CandidateEmailUniqueness.LockKey(companyA, "x@example.com"),
            CandidateEmailUniqueness.LockKey(companyB, "x@example.com"));
        Assert.NotEqual(
            CandidateEmailUniqueness.LockKey(companyA, "x@example.com"),
            CandidateEmailUniqueness.LockKey(companyA, "y@example.com"));
    }

    [Fact]
    public void UniqueIndexName_Is_The_Migrated_Index_Name()
    {
        Assert.Equal("ux_candidates_company_id_normalised_email", CandidateEmailUniqueness.UniqueIndexName);
    }
}
