using HR.Modules.Identity.Services.AccountEmailPolicy;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Tests.AccountEmailPolicy;

/// <summary>
/// Ticket 9: loading/validation of the version-controlled denylist (embedded resource) plus
/// operator-configured additions, and the startup options validator. A broken list must fail
/// closed (errors / exception), never degrade into "allow every domain".
/// </summary>
public class BlockedEmailDomainListTests
{
    // ── Embedded resource ───────────────────────────────────────────────────────

    [Fact]
    public void ReadEmbeddedEntries_Loads_A_NonEmpty_List()
    {
        var entries = BlockedEmailDomainList.ReadEmbeddedEntries();

        Assert.NotEmpty(entries);
        Assert.Contains("gmail.com", entries);
        Assert.Contains("hotmail.com", entries);
        Assert.Contains("outlook.com", entries);
        Assert.Contains("mailinator.com", entries);
    }

    [Fact]
    public void ReadEmbeddedEntries_Excludes_Comments_And_Blank_Lines()
    {
        var entries = BlockedEmailDomainList.ReadEmbeddedEntries();

        Assert.DoesNotContain(entries, e => e.StartsWith('#'));
        Assert.DoesNotContain(entries, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void Every_Embedded_Entry_Is_Already_In_Normalised_Form()
    {
        var notNormalised = BlockedEmailDomainList.ReadEmbeddedEntries()
            .Where(entry => !AccountEmailDomain.TryNormalizeDomain(entry, out var normalised) || normalised != entry)
            .ToList();

        Assert.Empty(notNormalised);
    }

    [Fact]
    public void Embedded_List_Has_No_Duplicate_Entries()
    {
        var duplicates = BlockedEmailDomainList.ReadEmbeddedEntries()
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Build_With_Embedded_List_And_No_Additional_Entries_Has_No_Errors()
    {
        var (domains, errors) = BlockedEmailDomainList.Build(BlockedEmailDomainList.ReadEmbeddedEntries(), null);

        Assert.Empty(errors);
        Assert.Equal(BlockedEmailDomainList.ReadEmbeddedEntries().Count, domains.Count);
    }

    // ── ParseEntries ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseEntries_Trims_Lines_Handles_CrLf_And_Skips_Comments_And_Blanks()
    {
        var entries = BlockedEmailDomainList.ParseEntries(
            "# comment\r\n\r\n  gmail.com  \r\nhotmail.com\n   # indented comment\n\t\nyahoo.com");

        Assert.Equal(new[] { "gmail.com", "hotmail.com", "yahoo.com" }, entries);
    }

    [Fact]
    public void ParseEntries_Returns_Empty_For_Comment_Only_Content()
    {
        Assert.Empty(BlockedEmailDomainList.ParseEntries("# only\n# comments\n\n"));
    }

    // ── Build ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not a domain")]
    [InlineData("person@gmail.com")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost")]
    [InlineData("-bad.com")]
    [InlineData("under_score.com")]
    public void Build_Reports_An_Error_For_An_Invalid_Additional_Entry(string invalid)
    {
        var (_, errors) = BlockedEmailDomainList.Build(BlockedEmailDomainList.ReadEmbeddedEntries(), [invalid]);

        var error = Assert.Single(errors);
        Assert.Contains(AccountEmailDomainPolicyOptions.SectionName, error);
    }

    [Fact]
    public void Build_Reports_Email_Address_Entries_Distinctly_From_Invalid_Domains()
    {
        var (_, errors) = BlockedEmailDomainList.Build(["gmail.com"], ["person@gmail.com", "not a domain", ""]);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("must be a domain, not an email address"));
        Assert.Contains(errors, e => e.Contains("is not a valid domain"));
        Assert.Contains(errors, e => e.Contains("blank entry"));
    }

    [Fact]
    public void Build_Reports_Invalid_Embedded_Entries_Against_The_Embedded_Source()
    {
        var (_, errors) = BlockedEmailDomainList.Build(["gmail.com", "not a domain"], null);

        var error = Assert.Single(errors);
        Assert.Contains("embedded", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_With_Empty_Embedded_And_No_Additional_Entries_Reports_Empty_List_Error()
    {
        var (domains, errors) = BlockedEmailDomainList.Build([], null);

        Assert.Empty(domains);
        var error = Assert.Single(errors);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_With_Only_Invalid_Entries_Reports_Both_The_Entry_And_The_Empty_List()
    {
        var (domains, errors) = BlockedEmailDomainList.Build(["not a domain"], null);

        Assert.Empty(domains);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_With_Empty_Embedded_But_A_Valid_Additional_Entry_Is_Not_Empty()
    {
        var (domains, errors) = BlockedEmailDomainList.Build([], ["custom-disposable.test"]);

        Assert.Empty(errors);
        Assert.Equal(new[] { "custom-disposable.test" }, domains);
    }

    [Fact]
    public void Build_Normalises_And_Deduplicates_Additional_Entries()
    {
        var (domains, errors) = BlockedEmailDomainList.Build(
            ["gmail.com"], ["  GMAIL.COM.  ", "Custom-Disposable.Test", "custom-disposable.test", "bücher.example"]);

        Assert.Empty(errors);
        Assert.Equal(3, domains.Count);
        Assert.Contains("gmail.com", (IEnumerable<string>)domains);
        Assert.Contains("custom-disposable.test", (IEnumerable<string>)domains);
        Assert.Contains("xn--bcher-kva.example", (IEnumerable<string>)domains);
    }

    // ── Policy construction with additional entries ─────────────────────────────

    [Fact]
    public void Policy_Built_With_Additional_Entry_Blocks_It_And_Its_Subdomains()
    {
        var policy = new AccountEmailDomainPolicy(["custom-disposable.test"]);

        Assert.Equal(AccountEmailDomainVerdict.Blocked, policy.Evaluate("person@custom-disposable.test").Verdict);
        Assert.Equal(AccountEmailDomainVerdict.Blocked, policy.Evaluate("person@inbox.custom-disposable.test").Verdict);
        Assert.Equal(AccountEmailDomainVerdict.Allowed, policy.Evaluate("person@not-custom-disposable.test").Verdict);
        // Embedded baseline still applies.
        Assert.Equal(AccountEmailDomainVerdict.Blocked, policy.Evaluate("person@gmail.com").Verdict);
    }

    [Fact]
    public void Policy_Built_With_Unicode_Additional_Entry_Blocks_Both_Unicode_And_Punycode_Addresses()
    {
        var policy = new AccountEmailDomainPolicy(["bücher.example"]);

        Assert.Equal(AccountEmailDomainVerdict.Blocked, policy.Evaluate("person@BÜCHER.example").Verdict);
        Assert.Equal(AccountEmailDomainVerdict.Blocked, policy.Evaluate("person@xn--bcher-kva.example").Verdict);
    }

    [Fact]
    public void Policy_Built_Without_Additional_Entries_Does_Not_Block_Unlisted_Custom_Domain()
    {
        var policy = new AccountEmailDomainPolicy(additionalBlockedDomains: null);

        Assert.Equal(AccountEmailDomainVerdict.Allowed, policy.Evaluate("person@custom-disposable.test").Verdict);
    }

    [Theory]
    [InlineData("not a domain")]
    [InlineData("person@gmail.com")]
    [InlineData("")]
    public void Policy_Constructor_Throws_For_An_Invalid_Additional_Entry(string invalid)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new AccountEmailDomainPolicy([invalid]));

        Assert.Contains("denylist is invalid", ex.Message);
    }

    // ── Options validator ───────────────────────────────────────────────────────

    [Fact]
    public void OptionsValidator_Succeeds_For_Default_Options()
    {
        var result = new AccountEmailDomainPolicyOptionsValidator().Validate(Options.DefaultName, new AccountEmailDomainPolicyOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OptionsValidator_Succeeds_For_Valid_Additional_Domains()
    {
        var result = new AccountEmailDomainPolicyOptionsValidator().Validate(
            Options.DefaultName,
            new AccountEmailDomainPolicyOptions { AdditionalBlockedDomains = ["custom-disposable.test", "GMAIL.COM"] });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("not a domain")]
    [InlineData("person@gmail.com")]
    [InlineData("")]
    [InlineData("localhost")]
    public void OptionsValidator_Fails_For_Invalid_Additional_Domains(string invalid)
    {
        var result = new AccountEmailDomainPolicyOptionsValidator().Validate(
            Options.DefaultName,
            new AccountEmailDomainPolicyOptions { AdditionalBlockedDomains = ["custom-disposable.test", invalid] });

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        var failure = Assert.Single(result.Failures!);
        Assert.StartsWith("Ticket 9 account email-domain policy:", failure);
    }
}
