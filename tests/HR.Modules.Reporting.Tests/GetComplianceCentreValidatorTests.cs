using HR.Modules.Reporting.Features.GetComplianceCentre;

namespace HR.Modules.Reporting.Tests;

public class GetComplianceCentreValidatorTests
{
    private readonly GetComplianceCentreValidator _validator = new();

    private static GetComplianceCentreRequest Req(
        Guid? companyId = null, string? category = null, string? severity = null,
        DateOnly? dueStart = null, DateOnly? dueEnd = null) =>
        new(companyId ?? Guid.NewGuid(), category, null, null, dueStart, dueEnd, severity);

    [Fact]
    public void Validate_Fails_When_CompanyId_Empty()
    {
        var result = _validator.Validate(Req(companyId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetComplianceCentreRequest.CompanyId));
    }

    [Fact]
    public void Validate_Succeeds_With_Only_CompanyId()
    {
        Assert.True(_validator.Validate(Req()).IsValid);
    }

    [Theory]
    [InlineData("ExpiringVisa")]
    [InlineData("expiringvisa")]
    [InlineData("ProbationReview")]
    public void Validate_Accepts_Valid_Category(string category)
    {
        Assert.True(_validator.Validate(Req(category: category)).IsValid);
    }

    [Fact]
    public void Validate_Fails_For_Invalid_Category()
    {
        var result = _validator.Validate(Req(category: "NotACategory"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetComplianceCentreRequest.Category));
    }

    [Theory]
    [InlineData("Overdue")]
    [InlineData("duesoon")]
    [InlineData("Informational")]
    public void Validate_Accepts_Valid_Severity(string severity)
    {
        Assert.True(_validator.Validate(Req(severity: severity)).IsValid);
    }

    [Fact]
    public void Validate_Fails_For_Invalid_Severity()
    {
        var result = _validator.Validate(Req(severity: "bogus"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetComplianceCentreRequest.Severity));
    }

    [Fact]
    public void Validate_Fails_When_DueDateEnd_Before_DueDateStart()
    {
        var result = _validator.Validate(Req(
            dueStart: new DateOnly(2026, 6, 1), dueEnd: new DateOnly(2026, 5, 1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetComplianceCentreRequest.DueDateEnd));
    }

    [Fact]
    public void Validate_Accepts_Equal_DueDateStart_And_DueDateEnd()
    {
        var date = new DateOnly(2026, 6, 1);
        Assert.True(_validator.Validate(Req(dueStart: date, dueEnd: date)).IsValid);
    }

    [Fact]
    public void Validate_Accepts_DueDateEnd_Without_DueDateStart()
    {
        Assert.True(_validator.Validate(Req(dueEnd: new DateOnly(2026, 6, 1))).IsValid);
    }
}
