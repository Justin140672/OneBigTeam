using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-01: <see cref="TargetUserCompanyGuard"/> is a thin delegate onto
/// <see cref="HR.Modules.Employees.Contracts.IEmployeeAudienceReader.EmployeeExistsAsync"/> — these
/// tests pin that delegation (both the parameters passed through and the return value propagated).
/// </summary>
public class TargetUserCompanyGuardTests
{
    [Fact]
    public async Task IsMemberAsync_Delegates_To_EmployeeAudienceReader_With_Given_CompanyId_And_UserId()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var reader = new FakeEmployeeAudienceReader([], exists: true);
        var guard = new TargetUserCompanyGuard(reader);

        var result = await guard.IsMemberAsync(companyId, userId, CancellationToken.None);

        Assert.True(result);
        Assert.Equal((companyId, userId), reader.LastEmployeeExistsCall);
    }

    [Fact]
    public async Task IsMemberAsync_Returns_False_When_EmployeeAudienceReader_Reports_Not_A_Member()
    {
        var reader = new FakeEmployeeAudienceReader([], exists: false);
        var guard = new TargetUserCompanyGuard(reader);

        var result = await guard.IsMemberAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }
}
