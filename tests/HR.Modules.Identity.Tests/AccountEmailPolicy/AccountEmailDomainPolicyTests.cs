using HR.Modules.Identity.Services.AccountEmailPolicy;

namespace HR.Modules.Identity.Tests.AccountEmailPolicy;

/// <summary>
/// Ticket 9: the shared account-creation email-domain rule — exact + subdomain matching against
/// the embedded denylist, label-by-label (never raw string suffix), and malformed input is never
/// treated as allowed.
/// </summary>
public class AccountEmailDomainPolicyTests
{
    private static readonly AccountEmailDomainPolicy Policy = AccountEmailDomainPolicy.Default;

    public static TheoryData<string> EmbeddedEntries()
    {
        var data = new TheoryData<string>();
        foreach (var entry in BlockedEmailDomainList.ReadEmbeddedEntries())
            data.Add(entry);
        return data;
    }

    // ── Every embedded entry ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EmbeddedEntries))]
    public void Evaluate_Blocks_Every_Embedded_Entry_Exactly(string entry)
    {
        var evaluation = Policy.Evaluate($"person@{entry}");

        Assert.Equal(AccountEmailDomainVerdict.Blocked, evaluation.Verdict);
        Assert.False(evaluation.IsAllowed);
        Assert.Equal(entry, evaluation.Domain);
        Assert.Equal(entry, evaluation.MatchedBlockedDomain);
    }

    [Theory]
    [MemberData(nameof(EmbeddedEntries))]
    public void Evaluate_Blocks_Every_Embedded_Entry_In_Upper_Case(string entry)
    {
        var evaluation = Policy.Evaluate($"PERSON@{entry.ToUpperInvariant()}");

        Assert.Equal(AccountEmailDomainVerdict.Blocked, evaluation.Verdict);
        Assert.Equal(entry, evaluation.MatchedBlockedDomain);
    }

    [Theory]
    [MemberData(nameof(EmbeddedEntries))]
    public void Evaluate_Blocks_A_Subdomain_Of_Every_Embedded_Entry_And_Reports_The_Matched_Entry(string entry)
    {
        var evaluation = Policy.Evaluate($"person@mx.{entry}");

        Assert.Equal(AccountEmailDomainVerdict.Blocked, evaluation.Verdict);
        Assert.Equal($"mx.{entry}", evaluation.Domain);
        Assert.Equal(entry, evaluation.MatchedBlockedDomain);
    }

    // ── Explicit acceptance cases ───────────────────────────────────────────────

    [Theory]
    [InlineData("person@gmail.com", "gmail.com")]
    [InlineData("person@GMAIL.COM", "gmail.com")]
    [InlineData(" person@gmail.com ", "gmail.com")]
    [InlineData("person@gmail.com.", "gmail.com")]
    [InlineData("person@googlemail.com", "googlemail.com")]
    [InlineData("person@hotmail.com", "hotmail.com")]
    [InlineData("person@hotmail.co.uk", "hotmail.co.uk")]
    [InlineData("person@outlook.com", "outlook.com")]
    [InlineData("person@live.com", "live.com")]
    [InlineData("person@yahoo.com", "yahoo.com")]
    [InlineData("person@yahoo.co.uk", "yahoo.co.uk")]
    [InlineData("person@icloud.com", "icloud.com")]
    [InlineData("person@aol.com", "aol.com")]
    [InlineData("person@protonmail.com", "protonmail.com")]
    [InlineData("person@proton.me", "proton.me")]
    [InlineData("person@gmx.com", "gmx.com")]
    [InlineData("person@mail.com", "mail.com")]
    [InlineData("person@zohomail.com", "zohomail.com")]
    [InlineData("person@mailinator.com", "mailinator.com")]
    [InlineData("person@guerrillamail.com", "guerrillamail.com")]
    [InlineData("person@10minutemail.com", "10minutemail.com")]
    [InlineData("person@yopmail.com", "yopmail.com")]
    public void Evaluate_Blocks_Public_And_Disposable_Providers(string email, string expectedMatch)
    {
        var evaluation = Policy.Evaluate(email);

        Assert.Equal(AccountEmailDomainVerdict.Blocked, evaluation.Verdict);
        Assert.Equal(expectedMatch, evaluation.MatchedBlockedDomain);
    }

    [Theory]
    [InlineData("person@company.co.uk", "company.co.uk")]
    [InlineData("person@acme.example", "acme.example")]
    // Google Workspace / Microsoft 365 hosted organisation domain — the host doesn't matter.
    [InlineData("person@brightsparks-consulting.co.uk", "brightsparks-consulting.co.uk")]
    // Suffix look-alikes: label-wise matching, not raw string suffix.
    [InlineData("person@olive.com", "olive.com")]          // not "live.com"
    [InlineData("person@acme.com", "acme.com")]            // not "me.com"
    [InlineData("person@notgmail.com", "notgmail.com")]    // not "gmail.com"
    [InlineData("person@gmail.co", "gmail.co")]            // not in list
    [InlineData("person@gmail.com.acme.example", "gmail.com.acme.example")] // blocked name as a LEFT-hand label
    [InlineData("PERSON@BRIGHTSPARKS-CONSULTING.CO.UK", "brightsparks-consulting.co.uk")]
    public void Evaluate_Allows_Organisation_Domains(string email, string expectedDomain)
    {
        var evaluation = Policy.Evaluate(email);

        Assert.Equal(AccountEmailDomainVerdict.Allowed, evaluation.Verdict);
        Assert.True(evaluation.IsAllowed);
        Assert.Equal(expectedDomain, evaluation.Domain);
        Assert.Null(evaluation.MatchedBlockedDomain);
    }

    [Fact]
    public void Evaluate_Reports_The_Parent_Entry_For_A_Deep_Subdomain_Match()
    {
        var evaluation = Policy.Evaluate("person@a.b.c.gmail.com");

        Assert.Equal(AccountEmailDomainVerdict.Blocked, evaluation.Verdict);
        Assert.Equal("a.b.c.gmail.com", evaluation.Domain);
        Assert.Equal("gmail.com", evaluation.MatchedBlockedDomain);
    }

    [Fact]
    public void Evaluate_Full_Width_Gmail_Is_Never_Allowed()
    {
        var evaluation = Policy.Evaluate("person@ｇｍａｉｌ.com");

        Assert.NotEqual(AccountEmailDomainVerdict.Allowed, evaluation.Verdict);
        if (evaluation.Verdict == AccountEmailDomainVerdict.Blocked)
            Assert.Equal("gmail.com", evaluation.MatchedBlockedDomain);
    }

    // ── Malformed ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("a@b@gmail.com")]
    [InlineData("@gmail.com")]
    [InlineData("person@")]
    [InlineData("per son@gmail.com")]
    [InlineData("person@localhost")]
    [InlineData("person@192.168.0.1")]
    [InlineData("person@[192.168.0.1]")]
    [InlineData("person@-bad.com")]
    [InlineData("person@bad..com")]
    [InlineData("person@under_score.com")]
    public void Evaluate_Returns_Malformed_And_Never_Allowed_For_Malformed_Address(string? email)
    {
        var evaluation = Policy.Evaluate(email);

        Assert.Equal(AccountEmailDomainVerdict.Malformed, evaluation.Verdict);
        Assert.False(evaluation.IsAllowed);
        Assert.Null(evaluation.Domain);
        Assert.Null(evaluation.MatchedBlockedDomain);
    }

    // ── Construction ────────────────────────────────────────────────────────────

    [Fact]
    public void Default_Is_A_Singleton_Built_From_The_Embedded_List()
    {
        Assert.Same(AccountEmailDomainPolicy.Default, AccountEmailDomainPolicy.Default);

        var embedded = BlockedEmailDomainList.ReadEmbeddedEntries();
        Assert.Equal(embedded.Distinct().Count(), AccountEmailDomainPolicy.Default.BlockedDomains.Count);
    }

    [Fact]
    public void Options_Constructor_With_Default_Options_Matches_The_Default_Policy()
    {
        var policy = new AccountEmailDomainPolicy(
            Microsoft.Extensions.Options.Options.Create(new AccountEmailDomainPolicyOptions()));

        Assert.Equal(
            AccountEmailDomainPolicy.Default.BlockedDomains.OrderBy(d => d, StringComparer.Ordinal),
            policy.BlockedDomains.OrderBy(d => d, StringComparer.Ordinal));
    }

    [Fact]
    public void Options_Constructor_Merges_Configured_Additional_Domains()
    {
        var policy = new AccountEmailDomainPolicy(
            Microsoft.Extensions.Options.Options.Create(new AccountEmailDomainPolicyOptions
            {
                AdditionalBlockedDomains = ["newly-observed-disposable.test"],
            }));

        Assert.Equal(AccountEmailDomainVerdict.Blocked, policy.Evaluate("person@newly-observed-disposable.test").Verdict);
        // Baseline entries are never removed by configuration.
        Assert.Equal(AccountEmailDomainVerdict.Blocked, policy.Evaluate("person@gmail.com").Verdict);
    }
}
