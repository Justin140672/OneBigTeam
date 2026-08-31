using HR.Modules.Identity.Features.GetPermissionHistory;

namespace HR.Modules.Identity.Tests;

public class GetPermissionHistoryValidatorTests
{
    private static GetPermissionHistoryRequest ValidRequest() => new() { CompanyId = Guid.NewGuid() };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new GetPermissionHistoryValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new GetPermissionHistoryValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPermissionHistoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_Page_Is_Zero()
    {
        var validator = new GetPermissionHistoryValidator();
        var request = ValidRequest() with { Page = 0 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPermissionHistoryRequest.Page));
    }

    [Fact]
    public void Validate_Passes_When_PageSize_Is_The_Maximum_Boundary_Of_100()
    {
        var validator = new GetPermissionHistoryValidator();
        var request = ValidRequest() with { PageSize = 100 };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Exceeds_The_Maximum_Boundary_Of_100()
    {
        var validator = new GetPermissionHistoryValidator();
        var request = ValidRequest() with { PageSize = 101 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPermissionHistoryRequest.PageSize));
    }

    [Fact]
    public void Validate_Fails_When_PageSize_Is_Zero()
    {
        var validator = new GetPermissionHistoryValidator();
        var request = ValidRequest() with { PageSize = 0 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPermissionHistoryRequest.PageSize));
    }

    [Fact]
    public void Validate_Fails_When_ToDate_Is_Before_FromDate()
    {
        var validator = new GetPermissionHistoryValidator();
        var from = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var request = ValidRequest() with { FromDate = from, ToDate = from.AddDays(-1) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetPermissionHistoryRequest.ToDate));
    }

    [Fact]
    public void Validate_Passes_When_ToDate_Equals_FromDate()
    {
        var validator = new GetPermissionHistoryValidator();
        var from = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var request = ValidRequest() with { FromDate = from, ToDate = from };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_ToDate_Is_After_FromDate()
    {
        var validator = new GetPermissionHistoryValidator();
        var from = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
        var request = ValidRequest() with { FromDate = from, ToDate = from.AddDays(1) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Only_FromDate_Is_Supplied()
    {
        var validator = new GetPermissionHistoryValidator();
        var request = ValidRequest() with { FromDate = DateTimeOffset.UtcNow };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_When_Only_ToDate_Is_Supplied()
    {
        var validator = new GetPermissionHistoryValidator();
        var request = ValidRequest() with { ToDate = DateTimeOffset.UtcNow };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
