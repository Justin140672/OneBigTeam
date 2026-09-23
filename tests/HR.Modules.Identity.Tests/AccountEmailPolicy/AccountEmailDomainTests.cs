using HR.Modules.Identity.Services.AccountEmailPolicy;

namespace HR.Modules.Identity.Tests.AccountEmailPolicy;

/// <summary>
/// Ticket 9: domain extraction/normalisation shared by the account-creation email-domain policy and
/// the denylist loader. Both sides must land on exactly the same canonical form, and anything that
/// can't be normalised with confidence must be reported as malformed (never silently "allowed").
/// </summary>
public class AccountEmailDomainTests
{
    // ── Extraction + normalisation ──────────────────────────────────────────────

    [Theory]
    [InlineData("person@gmail.com", "gmail.com")]
    [InlineData("person@GMAIL.COM", "gmail.com")]
    [InlineData("Person.Name@Gmail.Com", "gmail.com")]
    [InlineData(" person@gmail.com ", "gmail.com")]
    [InlineData("\tperson@gmail.com\n", "gmail.com")]
    [InlineData("person@gmail.com.", "gmail.com")]
    [InlineData("person@company.co.uk", "company.co.uk")]
    [InlineData("first.last+tag@mx.acme.example", "mx.acme.example")]
    public void TryExtractDomain_Normalises_Case_Whitespace_And_Trailing_Root_Dot(string email, string expected)
    {
        var ok = AccountEmailDomain.TryExtractDomain(email, out var domain);

        Assert.True(ok);
        Assert.Equal(expected, domain);
    }

    [Fact]
    public void TryExtractDomain_Converts_Unicode_Domain_To_Punycode()
    {
        var ok = AccountEmailDomain.TryExtractDomain("person@bücher.example", out var domain);

        Assert.True(ok);
        Assert.Equal("xn--bcher-kva.example", domain);
    }

    [Fact]
    public void TryExtractDomain_Unicode_And_Punycode_Spellings_Normalise_To_The_Same_Value()
    {
        Assert.True(AccountEmailDomain.TryExtractDomain("person@BÜCHER.example", out var unicode));
        Assert.True(AccountEmailDomain.TryExtractDomain("person@xn--bcher-kva.example", out var punycode));

        Assert.Equal(punycode, unicode);
    }

    [Fact]
    public void TryExtractDomain_Full_Width_Gmail_Is_Either_Mapped_To_Gmail_Or_Rejected_As_Malformed()
    {
        // UTS #46 maps full-width Latin letters to their ASCII equivalents, so this is expected to
        // normalise to "gmail.com". The only outcome that would be a security hole is normalising to
        // some OTHER domain — so assert "gmail.com or malformed", never anything else.
        var ok = AccountEmailDomain.TryExtractDomain("person@ｇｍａｉｌ.com", out var domain);

        if (ok)
            Assert.Equal("gmail.com", domain);
        else
            Assert.Equal(string.Empty, domain);
    }

    // ── Malformed input ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("a@b@c.com")]
    [InlineData("@gmail.com")]
    [InlineData("person@")]
    [InlineData("per son@gmail.com")]
    [InlineData("per\tson@gmail.com")]
    [InlineData("per\u0001son@gmail.com")]
    [InlineData("person@localhost")]
    [InlineData("person@192.168.0.1")]
    [InlineData("person@[192.168.0.1]")]
    [InlineData("person@example.123")]
    [InlineData("person@-bad.com")]
    [InlineData("person@bad-.com")]
    [InlineData("person@bad..com")]
    [InlineData("person@.gmail.com")]
    [InlineData("person@gmail.com..")]
    [InlineData("person@under_score.com")]
    [InlineData("person@gmail com")]
    [InlineData("person@gmail.com/evil")]
    public void TryExtractDomain_Returns_False_And_Empty_Domain_For_Malformed_Address(string? email)
    {
        var ok = AccountEmailDomain.TryExtractDomain(email, out var domain);

        Assert.False(ok);
        Assert.Equal(string.Empty, domain);
    }

    // ── Boundaries ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryNormalizeDomain_Accepts_Minimum_Two_Label_Domain()
    {
        Assert.True(AccountEmailDomain.TryNormalizeDomain("a.co", out var domain));
        Assert.Equal("a.co", domain);
    }

    [Fact]
    public void TryNormalizeDomain_Rejects_Single_Label_Domain()
    {
        Assert.False(AccountEmailDomain.TryNormalizeDomain("co", out _));
    }

    [Fact]
    public void TryNormalizeDomain_Accepts_Label_Of_Exactly_63_Characters()
    {
        var label = new string('a', 63);

        Assert.True(AccountEmailDomain.TryNormalizeDomain($"{label}.com", out var domain));
        Assert.Equal($"{label}.com", domain);
    }

    [Fact]
    public void TryNormalizeDomain_Rejects_Label_Of_64_Characters()
    {
        var label = new string('a', 64);

        Assert.False(AccountEmailDomain.TryNormalizeDomain($"{label}.com", out _));
    }

    [Fact]
    public void TryNormalizeDomain_Accepts_Domain_Of_Exactly_253_Characters()
    {
        // 3 x (63 + '.') = 192, + 57 + ".com" (4) = 253.
        var label = new string('a', 63);
        var value = $"{label}.{label}.{label}.{new string('b', 57)}.com";
        Assert.Equal(253, value.Length);

        Assert.True(AccountEmailDomain.TryNormalizeDomain(value, out var domain));
        Assert.Equal(value, domain);
    }

    [Fact]
    public void TryNormalizeDomain_Rejects_Domain_Of_254_Characters()
    {
        var label = new string('a', 63);
        var value = $"{label}.{label}.{label}.{new string('b', 58)}.com";
        Assert.Equal(254, value.Length);

        Assert.False(AccountEmailDomain.TryNormalizeDomain(value, out _));
    }

    [Theory]
    [InlineData("123.example", "123.example")]   // numeric label that is NOT the TLD is fine
    [InlineData("example.c0m", "example.c0m")]   // TLD with a digit but not all-numeric is fine
    [InlineData("a-b.com", "a-b.com")]           // inner hyphen is fine
    public void TryNormalizeDomain_Numeric_And_Hyphen_Rules_Only_Reject_The_Disallowed_Positions(string value, string expected)
    {
        Assert.True(AccountEmailDomain.TryNormalizeDomain(value, out var domain));
        Assert.Equal(expected, domain);
    }

    [Fact]
    public void TryNormalizeDomain_Strips_Only_A_Single_Trailing_Dot()
    {
        Assert.True(AccountEmailDomain.TryNormalizeDomain("gmail.com.", out var single));
        Assert.Equal("gmail.com", single);

        Assert.False(AccountEmailDomain.TryNormalizeDomain("gmail.com..", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    public void TryNormalizeDomain_Rejects_Null_Empty_Whitespace_Or_Root_Only(string? value)
    {
        Assert.False(AccountEmailDomain.TryNormalizeDomain(value, out var domain));
        Assert.Equal(string.Empty, domain);
    }
}
