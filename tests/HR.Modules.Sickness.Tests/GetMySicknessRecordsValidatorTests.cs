using HR.Modules.Sickness.Features.GetMySicknessRecords;

namespace HR.Modules.Sickness.Tests;

public class GetMySicknessRecordsValidatorTests
{
    private readonly GetMySicknessRecordsValidator _validator = new();

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new GetMySicknessRecordsRequest
        {
            CompanyId = Guid.Empty,
            EmployeeId = Guid.NewGuid()
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        var result = _validator.Validate(new GetMySicknessRecordsRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.Empty
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Succeeds_With_Valid_Request()
    {
        var result = _validator.Validate(new GetMySicknessRecordsRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }
}
