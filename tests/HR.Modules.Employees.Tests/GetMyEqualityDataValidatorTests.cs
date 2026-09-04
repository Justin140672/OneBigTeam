using HR.Modules.Employees.Features.GetMyEqualityData;

namespace HR.Modules.Employees.Tests;

public class GetMyEqualityDataValidatorTests
{
    private static readonly GetMyEqualityDataValidator Validator = new();

    [Fact]
    public void Passes_For_Populated_Route_Ids()
        => Assert.True(Validator.Validate(new GetMyEqualityDataRequest(Guid.NewGuid(), Guid.NewGuid())).IsValid);

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
        => Assert.False(Validator.Validate(new GetMyEqualityDataRequest(Guid.Empty, Guid.NewGuid())).IsValid);

    [Fact]
    public void Fails_When_EmployeeId_Is_Empty()
        => Assert.False(Validator.Validate(new GetMyEqualityDataRequest(Guid.NewGuid(), Guid.Empty)).IsValid);
}
