using HR.Modules.Employees.Features.DeleteMyEqualityData;

namespace HR.Modules.Employees.Tests;

public class DeleteMyEqualityDataValidatorTests
{
    private static readonly DeleteMyEqualityDataValidator Validator = new();

    [Fact]
    public void Passes_For_Populated_Route_Ids()
        => Assert.True(Validator.Validate(new DeleteMyEqualityDataRequest(Guid.NewGuid(), Guid.NewGuid())).IsValid);

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
        => Assert.False(Validator.Validate(new DeleteMyEqualityDataRequest(Guid.Empty, Guid.NewGuid())).IsValid);

    [Fact]
    public void Fails_When_EmployeeId_Is_Empty()
        => Assert.False(Validator.Validate(new DeleteMyEqualityDataRequest(Guid.NewGuid(), Guid.Empty)).IsValid);
}
