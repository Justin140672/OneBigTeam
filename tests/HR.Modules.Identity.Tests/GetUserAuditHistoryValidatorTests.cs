using HR.Modules.Identity.Features.GetUserAuditHistory;

namespace HR.Modules.Identity.Tests;

public class GetUserAuditHistoryValidatorTests
{
    private static GetUserAuditHistoryRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
    };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new GetUserAuditHistoryValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new GetUserAuditHistoryValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetUserAuditHistoryRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new GetUserAuditHistoryValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetUserAuditHistoryRequest.EmployeeId));
    }
}
