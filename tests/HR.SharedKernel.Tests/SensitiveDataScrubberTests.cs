using HR.SharedKernel;

namespace HR.SharedKernel.Tests;

/// <summary>
/// NFR-01: unit coverage for the single source of truth for sensitive-data classification.
/// </summary>
public class SensitiveDataScrubberTests
{
    [Theory]
    [InlineData("salary")]
    [InlineData("previousSalary")]
    [InlineData("nationalInsuranceNumber")]
    [InlineData("bankAccountNumber")]
    [InlineData("password")]
    [InlineData("token")]
    [InlineData("secret")]
    [InlineData("accessToken")]
    [InlineData("BankAccountSortCode")]   // matched via the "bankaccount" fragment
    [InlineData("SALARY")]                // case-insensitive exact match
    [InlineData("employee_national_insurance")] // matched via the "national_insurance" fragment
    public void IsProhibitedFieldName_true_for_sensitive_names(string name)
    {
        Assert.True(SensitiveDataScrubber.IsProhibitedFieldName(name));
    }

    [Theory]
    // Ticket 3: voluntary equality-monitoring answer fields — special-category data.
    [InlineData("genderIdentity")]
    [InlineData("genderIdentitySelfDescribed")]
    [InlineData("marriedOrCivilPartnershipStatus")]
    [InlineData("maritalStatus")]
    [InlineData("civilPartnershipStatus")]
    [InlineData("ethnicGroup")]
    [InlineData("ethnicGroupSelfDescribed")]
    [InlineData("ethnicity")]
    [InlineData("disabilityStatus")]
    [InlineData("disabilityImpact")]
    [InlineData("sexualOrientation")]
    [InlineData("sexualOrientationSelfDescribed")]
    [InlineData("religionOrBelief")]
    [InlineData("religionOrBeliefSelfDescribed")]
    [InlineData("religion")]
    [InlineData("EthnicGroup")]            // casing variant
    [InlineData("RELIGIONORBELIEF")]       // casing variant
    [InlineData("DisabilityImpact")]       // casing variant
    public void IsProhibitedFieldName_true_for_equality_monitoring_fields(string name)
    {
        Assert.True(SensitiveDataScrubber.IsProhibitedFieldName(name));
    }

    [Theory]
    // Regression guard: the equality audit events use "<field>Provided" boolean flag names and
    // "Created" — none of these must be rejected by the redaction guard.
    [InlineData("GenderIdentityProvided")]
    [InlineData("MarriedOrCivilPartnershipStatusProvided")]
    [InlineData("EthnicGroupProvided")]
    [InlineData("DisabilityStatusProvided")]
    [InlineData("SexualOrientationProvided")]
    [InlineData("ReligionOrBeliefProvided")]
    [InlineData("Created")]
    public void IsProhibitedFieldName_false_for_equality_audit_flag_names(string name)
    {
        Assert.False(SensitiveDataScrubber.IsProhibitedFieldName(name));
    }

    [Theory]
    [InlineData("employeeNumber")]
    [InlineData("firstName")]
    [InlineData("displaySalaryOnProfile")]           // contains "salary" but is not an exact match / fragment
    [InlineData("DisplaySalaryOnEmployeeProfile")]   // boolean display-preference flag, deliberately allowed
    [InlineData("currency")]
    [InlineData("direction")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsProhibitedFieldName_false_for_non_sensitive_names(string? name)
    {
        Assert.False(SensitiveDataScrubber.IsProhibitedFieldName(name));
    }

    [Theory]
    [InlineData("AB123456C")]
    [InlineData("QQ 12 34 56 C")]
    [InlineData("GB29NWBK60161331926819")]      // IBAN
    [InlineData("12-34-56")]                     // sort code
    [InlineData("4111111111111111")]            // 16-digit bank/card number
    [InlineData("Bearer abc123.DEF-456")]       // authorization header value
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc123sig")] // JWT
    [InlineData("$2b$12$abcdefghijklmnopqrstuvABCDEFGHIJKLMNOPQRSTUVWXYZ012345678")] // bcrypt hash ($2b$12$ + 53 chars)
    public void ContainsSensitiveValue_true_for_sensitive_values(string value)
    {
        Assert.True(SensitiveDataScrubber.ContainsSensitiveValue(value));
        Assert.NotNull(SensitiveDataScrubber.MatchSensitiveValue(value));
    }

    [Theory]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")] // GUID
    [InlineData("00000000-0000-0000-0000-000000000001")] // seeded GUID — 12-digit final segment must not trip BankOrCardNumber
    [InlineData("12345678-1234-1234-1234-123456789012")] // GUID whose final segment is all digits
    [InlineData("Policy-cb0754f9d9ab40079b70a858434dac74")] // lowercase :N GUID embedded in a name — must not trip Iban
    [InlineData("audit.tester.bf825b7f8baf4c0890f23225a2d8a0f9@example.com")] // lowercase :N GUID embedded in an email
    [InlineData("Annual")]
    [InlineData("EMP-0001")]
    [InlineData("123")]                                  // short number
    [InlineData("Increase")]
    [InlineData(null)]
    [InlineData("")]
    public void ContainsSensitiveValue_false_for_non_sensitive_values(string? value)
    {
        Assert.False(SensitiveDataScrubber.ContainsSensitiveValue(value));
        Assert.Null(SensitiveDataScrubber.MatchSensitiveValue(value));
    }

    [Fact]
    public void ScrubText_replaces_match_and_keeps_surrounding_text()
    {
        var scrubbed = SensitiveDataScrubber.ScrubText("Employee NI is AB123456C on file");

        Assert.Contains("Employee NI is", scrubbed);
        Assert.Contains("on file", scrubbed);
        Assert.Contains(SensitiveDataScrubber.Redacted, scrubbed);
        Assert.DoesNotContain("AB123456C", scrubbed);
    }

    [Fact]
    public void ScrubText_replaces_bearer_token()
    {
        var scrubbed = SensitiveDataScrubber.ScrubText("Authorization: Bearer abc123.DEF-456");

        Assert.StartsWith("Authorization: ", scrubbed);
        Assert.Contains(SensitiveDataScrubber.Redacted, scrubbed);
        Assert.DoesNotContain("abc123.DEF-456", scrubbed);
    }

    [Fact]
    public void ScrubText_leaves_clean_text_untouched()
    {
        const string clean = "effectiveFrom=2026-01-01 salaryType=Annual currency=GBP direction=Increase";
        Assert.Equal(clean, SensitiveDataScrubber.ScrubText(clean));
    }
}
