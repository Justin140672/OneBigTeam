using HR.Modules.Employees.Features.CancelLeavingProcess;

namespace HR.Modules.Employees.Tests;

public class CancelLeavingProcessValidatorTests
{
    private static CancelLeavingProcessRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), CancellationReason: "Employee retracted resignation.");

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var validator = new CancelLeavingProcessValidator();

        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var validator = new CancelLeavingProcessValidator();
        var request = ValidRequest() with { CompanyId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelLeavingProcessRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var validator = new CancelLeavingProcessValidator();
        var request = ValidRequest() with { EmployeeId = Guid.Empty };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelLeavingProcessRequest.EmployeeId));
    }

    [Fact]
    public void Validate_Fails_When_CancellationReason_Is_Empty()
    {
        var validator = new CancelLeavingProcessValidator();
        var request = ValidRequest() with { CancellationReason = "" };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelLeavingProcessRequest.CancellationReason));
    }

    [Fact]
    public void Validate_Fails_When_CancellationReason_Exceeds_MaxLength()
    {
        var validator = new CancelLeavingProcessValidator();
        var request = ValidRequest() with { CancellationReason = new string('a', 1001) };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelLeavingProcessRequest.CancellationReason));
    }

    [Fact]
    public void Validate_Passes_When_CancellationReason_Is_Exactly_MaxLength()
    {
        var validator = new CancelLeavingProcessValidator();
        var request = ValidRequest() with { CancellationReason = new string('a', 1000) };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
