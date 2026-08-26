using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.GetUserDetails;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class GetUserDetailsHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private GetUserDetailsHandler BuildHandler(FakeTargetUserCompanyGuard? guard = null) =>
        new(fixture.BuildContext(), new FakeEmployeeNameReader(), guard ?? new FakeTargetUserCompanyGuard());

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Guard_Reports_Not_A_Member()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // Seed a real invite for this employee/company so a passing result here could only be
        // explained by the guard short-circuiting, not by the data genuinely being missing.
        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "cross-tenant@test.com", Now));
            await db.SaveChangesAsync();
        }

        var guard = new FakeTargetUserCompanyGuard(isMember: false);
        var handler = BuildHandler(guard);

        var result = await handler.HandleAsync(
            new GetUserDetailsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal((companyId, employeeId), guard.LastCall);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Invite_And_No_User()
    {
        var handler = BuildHandler();

        var result = await handler.HandleAsync(
            new GetUserDetailsRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Details_For_Invited_Employee_When_Guard_Reports_Member()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "member@test.com", Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeTargetUserCompanyGuard(isMember: true));

        var result = await handler.HandleAsync(
            new GetUserDetailsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal("Pending", result.Value.InvitationStatus);
        Assert.Equal("NoAccount", result.Value.AccountStatus);
    }
}
