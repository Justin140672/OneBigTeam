using HR.Modules.Notifications.Features.ListOperationalAlerts;

namespace HR.Modules.Notifications.Tests.Features;

public class ListOperationalAlertsValidatorTests
{
    private static readonly ListOperationalAlertsValidator Validator = new();

    private static ListOperationalAlertsRequest Request(
        string? category = null, string? status = null, int page = 1, int pageSize = 25) =>
        new(CompanyId: null, Category: category, Status: status, Page: page, PageSize: pageSize);

    [Fact]
    public void Defaults_Are_Valid()
    {
        Assert.True(Validator.Validate(Request()).IsValid);
    }

    [Fact]
    public void Null_Category_And_Status_Are_Valid()
    {
        Assert.True(Validator.Validate(Request(category: null, status: null)).IsValid);
    }

    // ── Page ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Page_Boundary(int page, bool expectValid)
    {
        Assert.Equal(expectValid, Validator.Validate(Request(page: page)).IsValid);
    }

    // ── PageSize ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void PageSize_Boundary(int pageSize, bool expectValid)
    {
        Assert.Equal(expectValid, Validator.Validate(Request(pageSize: pageSize)).IsValid);
    }

    // ── Category ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ReportGeneration")]
    [InlineData("reportgeneration")]
    [InlineData("IntegrationDelivery")]
    public void Valid_Category_Name_Passes_CaseInsensitively(string category)
    {
        Assert.True(Validator.Validate(Request(category: category)).IsValid);
    }

    [Theory]
    [InlineData("not-a-category")]
    [InlineData("")]
    [InlineData("123")]
    public void Invalid_Category_Fails(string category)
    {
        var result = Validator.Validate(Request(category: category));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListOperationalAlertsRequest.Category));
    }

    // ── Status ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("open")]
    [InlineData("resolved")]
    public void Valid_Status_Passes(string status)
    {
        Assert.True(Validator.Validate(Request(status: status)).IsValid);
    }

    [Theory]
    [InlineData("Open")]      // case-sensitive: only lowercase accepted
    [InlineData("RESOLVED")]
    [InlineData("closed")]
    [InlineData("")]
    public void Invalid_Status_Fails(string status)
    {
        var result = Validator.Validate(Request(status: status));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListOperationalAlertsRequest.Status));
    }
}
