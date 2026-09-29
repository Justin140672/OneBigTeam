using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ListUsers;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ListUsersHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private ListUsersHandler BuildHandler(
        FakeEmployeeAudienceReader audienceReader,
        FakePositionProfileReader? positionProfileReader = null,
        FakeEmployeeNameReader? nameReader = null) =>
        new(
            fixture.BuildContext(),
            nameReader ?? new FakeEmployeeNameReader(),
            audienceReader,
            positionProfileReader ?? new FakePositionProfileReader());

    private static ListUsersRequest Request(Guid companyId) => new() { CompanyId = companyId, Page = 1, PageSize = 25 };

    [Fact]
    public async Task HandleAsync_Populates_PositionTitle_From_Audience_And_PositionProfile_Readers()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "invited@test.com", Now));
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
                [positionProfileId] = new PositionProfileSummary(positionProfileId, "Finance Assistant", null, null, true, null, null),
            });

        var result = await BuildHandler(audienceReader, positionReader).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(employeeId, row.EmployeeId);
        Assert.Equal(positionProfileId, row.PositionProfileId);
        Assert.Equal("Finance Assistant", row.PositionTitle);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Position_Null_When_Employee_Audience_Not_Resolvable()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "invited@test.com", Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader([employeeId]);

        var result = await BuildHandler(audienceReader).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Null(row.PositionProfileId);
        Assert.Null(row.PositionTitle);
    }

    [Fact]
    public async Task HandleAsync_Leaves_PositionTitle_Null_When_Profile_No_Longer_Resolves()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "invited@test.com", Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader(
            [employeeId],
            audienceProfiles: new Dictionary<Guid, EmployeeAudienceProfile>
            {
                [employeeId] = new EmployeeAudienceProfile(null, null, positionProfileId),
            });
        var result = await BuildHandler(audienceReader).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(positionProfileId, row.PositionProfileId);
        Assert.Null(row.PositionTitle);
    }

    [Fact]
    public async Task HandleAsync_Reports_Accurate_TotalCount_For_Small_Set()
    {
        var companyId = Guid.NewGuid();
        var employeeIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await using (var db = fixture.BuildContext())
        {
            foreach (var id in employeeIds)
                db.UserInvites.Add(UserInvite.Create(id, companyId, $"{id:N}@test.com", Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader(employeeIds);

        var result = await BuildHandler(audienceReader)
            .HandleAsync(new ListUsersRequest { CompanyId = companyId, Page = 1, PageSize = 2 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(2, result.Value.PageSize);
    }

    // Ticket 10 (P1): a disabled (IsActive=false) UserProfile-backed account previously always
    // reported accountStatus "Active" — it must now mirror IsActive like any other account.
    [Fact]
    public async Task HandleAsync_Reports_Disabled_AccountStatus_For_Inactive_UserProfile_Backed_Employee()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            var profile = UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "disabled-profile@test.com", "Disabled", "Profile", Now);
            profile.Deactivate(Now);
            db.UserProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader([employeeId]);

        var result = await BuildHandler(audienceReader).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal("Disabled", row.AccountStatus);
    }

    [Fact]
    public async Task HandleAsync_Reports_Active_AccountStatus_For_Active_UserProfile_Backed_Employee()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "active-profile@test.com", "Active", "Profile", Now));
            await db.SaveChangesAsync();
        }

        var audienceReader = new FakeEmployeeAudienceReader([employeeId]);

        var result = await BuildHandler(audienceReader).HandleAsync(Request(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal("Active", row.AccountStatus);
    }

    [Fact]
    public async Task HandleAsync_Returns_SignUp_Admin_With_Profile_Roles_And_No_Invite()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "signup.admin.list@test.com", "Sign", "Up", Now));
            db.UserRoles.Add(UserRole.Create(employeeId, SystemRoles.Employee, Now));
            db.UserRoles.Add(UserRole.Create(employeeId, SystemRoles.CompanyAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler(new FakeEmployeeAudienceReader([employeeId]))
            .HandleAsync(Request(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(employeeId, row.UserId);
        Assert.Equal("Active", row.AccountStatus);
        Assert.Equal("Claimed", row.InvitationStatus);
        Assert.Equal("Sign Up", row.Name);
        Assert.Equal("signup.admin.list@test.com", row.Email);
        Assert.Null(row.InviteId);
        Assert.Equivalent(new[] { SystemRoles.Employee, SystemRoles.CompanyAdministrator }, row.RoleIds);
    }
}
