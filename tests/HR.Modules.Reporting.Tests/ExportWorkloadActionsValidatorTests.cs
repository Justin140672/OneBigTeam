using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.ExportWorkloadActions;

namespace HR.Modules.Reporting.Tests;

public class ExportWorkloadActionsValidatorTests
{
    private readonly ExportWorkloadActionsValidator _validator = new();

    private static ExportWorkloadActionsRequest ValidRequest() => new(Guid.NewGuid());

    [Fact]
    public void Should_Not_Have_Error_For_Valid_Request()
    {
        var result = _validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_CompanyId_Is_Empty()
    {
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportWorkloadActionsRequest.CompanyId));
    }

    [Fact]
    public void Should_Have_Error_When_Format_Is_Invalid()
    {
        var request = ValidRequest() with { Format = (ReportExportFormat)999 };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportWorkloadActionsRequest.Format));
    }

    [Theory]
    [InlineData("ActionType")]
    [InlineData("AssignedUser")]
    [InlineData("Department")]
    [InlineData("DueDate")]
    public void Should_Not_Have_Error_For_Allowed_GroupBy_Values(string groupBy)
    {
        var request = ValidRequest() with { GroupBy = groupBy };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_Have_Error_When_GroupBy_Is_Not_Recognised()
    {
        var request = ValidRequest() with { GroupBy = "NotARealKey" };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ExportWorkloadActionsRequest.GroupBy));
    }
}
