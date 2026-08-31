using HR.Infrastructure.Persistence;

namespace HR.Architecture.Tests;

/// <summary>
/// NFR-01: direct coverage of <see cref="AuditPayloadRedactionGuard"/> — the runtime guard that
/// rejects an audit payload containing either a prohibited field name or a sensitive-looking value.
/// (The assembly-wide sweep of every IAuditEvent implementation lives in AuditPayloadRedactionTests.)
/// </summary>
public class AuditPayloadRedactionValueTests
{
    [Fact]
    public void Throws_for_prohibited_field_name_at_top_level()
    {
        Assert.Throws<ProhibitedAuditFieldException>(() =>
            AuditPayloadRedactionGuard.AssertPayloadIsSafe("""{"salary":60000}""", "After"));
    }

    [Fact]
    public void Throws_for_prohibited_field_name_when_nested()
    {
        Assert.Throws<ProhibitedAuditFieldException>(() =>
            AuditPayloadRedactionGuard.AssertPayloadIsSafe(
                """{"change":{"details":{"bankAccountNumber":"redacted-later"}}}""", "Metadata"));
    }

    [Fact]
    public void Throws_for_prohibited_field_name_inside_array()
    {
        Assert.Throws<ProhibitedAuditFieldException>(() =>
            AuditPayloadRedactionGuard.AssertPayloadIsSafe(
                """{"items":[{"note":"ok"},{"password":"x"}]}""", "After"));
    }

    [Theory]
    [InlineData("""{"note":"NI is AB123456C"}""")]
    [InlineData("""{"note":"account GB29NWBK60161331926819"}""")]
    [InlineData("""{"note":"Authorization Bearer abc.def-ghi"}""")]
    [InlineData("""{"note":"$2b$12$abcdefghijklmnopqrstuvABCDEFGHIJKLMNOPQRSTUVWXYZ012345678"}""")]
    public void Throws_for_sensitive_value_under_innocuous_field_name(string json)
    {
        Assert.Throws<ProhibitedAuditFieldException>(() =>
            AuditPayloadRedactionGuard.AssertPayloadIsSafe(json, "After"));
    }

    [Fact]
    public void Throws_for_unparseable_json()
    {
        Assert.Throws<ProhibitedAuditFieldException>(() =>
            AuditPayloadRedactionGuard.AssertPayloadIsSafe("{ not json", "After"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Does_not_throw_for_empty_payload(string? json)
    {
        AuditPayloadRedactionGuard.AssertPayloadIsSafe(json, "After");
    }

    [Fact]
    public void Does_not_throw_for_clean_payload()
    {
        AuditPayloadRedactionGuard.AssertPayloadIsSafe(
            """{"effectiveFrom":"2026-01-01","salaryType":"Annual","currency":"GBP","direction":"Increase"}""",
            "After");
    }
}
