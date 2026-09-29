using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.GetUserDetails;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class GetUserDetailsHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private GetUserDetailsHandler BuildHandler(
        FakeTargetUserCompanyGuard? guard = null,
        FakeEmployeeAudienceReader? audienceReader = null,
        FakePositionProfileReader? positionProfileReader = null) =>
        new(
            fixture.BuildContext(),
            new FakeEmployeeNameReader(),
            audienceReader ?? new FakeEmployeeAudienceReader([]),
            positionProfileReader ?? new FakePositionProfileReader(),
            guard ?? new FakeTargetUserCompanyGuard());

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Guard_Reports_Not_A_Member()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

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

    [Fact]
    public async Task HandleAsync_Populates_Position_From_Audience_And_PositionProfile_Readers()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "pos@test.com", Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader(
            [employeeId],
            audienceProfiles: new Dictionary<Guid, EmployeeAudienceProfile>
            {
                [employeeId] = new EmployeeAudienceProfile(null, null, positionProfileId),
            });
        var positionReader = new FakePositionProfileReader(
            summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [positionProfileId] = new PositionProfileSummary(positionProfileId, "Senior Engineer", null, null, true, null, null),
            });

        var result = await BuildHandler(audienceReader: audienceReader, positionProfileReader: positionReader)
            .HandleAsync(new GetUserDetailsRequest { CompanyId = companyId, EmployeeId = employeeId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value.PositionProfileId);
        Assert.Equal("Senior Engineer", result.Value.PositionTitle);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Position_Null_When_Employee_Has_No_Position()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "nopos@test.com", Now));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler()
            .HandleAsync(new GetUserDetailsRequest { CompanyId = companyId, EmployeeId = employeeId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PositionProfileId);
        Assert.Null(result.Value.PositionTitle);
    }

    [Fact]
    public async Task HandleAsync_Leaves_PositionTitle_Null_When_Profile_No_Longer_Resolves()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "gone@test.com", Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader(
            [employeeId],
            audienceProfiles: new Dictionary<Guid, EmployeeAudienceProfile>
            {
                [employeeId] = new EmployeeAudienceProfile(null, null, positionProfileId),
            });
        var result = await BuildHandler(audienceReader: audienceReader)
            .HandleAsync(new GetUserDetailsRequest { CompanyId = companyId, EmployeeId = employeeId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value.PositionProfileId);
        Assert.Null(result.Value.PositionTitle);
    }

    [Fact]
    public async Task HandleAsync_Returns_Active_Details_For_SignUp_Admin_With_Profile_Roles_And_No_Invite()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "signup.admin@test.com", "Sign", "Up", Now));
            db.UserRoles.Add(UserRole.Create(employeeId, SystemRoles.Employee, Now));
            db.UserRoles.Add(UserRole.Create(employeeId, SystemRoles.CompanyAdministrator, Now));
            db.UserRoles.Add(UserRole.Create(employeeId, SystemRoles.HrAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler().HandleAsync(
            new GetUserDetailsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var details = result.Value;
        Assert.Equal(employeeId, details.UserId);
        Assert.Equal("Active", details.AccountStatus);
        Assert.Equal("Claimed", details.InvitationStatus);
        Assert.Equal("Sign Up", details.Name);
        Assert.Equal("signup.admin@test.com", details.Email);
        Assert.Null(details.InviteId);
        Assert.Equivalent(
            new[] { SystemRoles.Employee, SystemRoles.CompanyAdministrator, SystemRoles.HrAdministrator },
            details.RoleIds);
        Assert.Equal(3, details.RoleNames.Count);
    }

    [Fact]
    public async Task HandleAsync_Reports_Disabled_And_LastLogin_For_Disabled_Profile_Account()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var lastLogin = new DateTimeOffset(2026, 9, 27, 10, 30, 0, TimeSpan.Zero);

        await using (var db = fixture.BuildContext())
        {
            var profile = UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "disabled.details@test.com", "Dis", "Abled", Now.AddDays(-30));
            profile.RecordLogin(lastLogin);
            profile.Deactivate(Now);
            db.UserProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler().HandleAsync(
            new GetUserDetailsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Disabled", result.Value.AccountStatus);
        Assert.Equal(lastLogin, result.Value.LastLoginAt);
    }
}
