using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ListInvitableEmployees;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ListInvitableEmployeesHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset LongAgo = DateTimeOffset.UtcNow.AddDays(-30);

    private ListInvitableEmployeesHandler BuildHandler(params EmployeeInviteCandidate[] candidates) =>
        new(fixture.BuildContext(), new FakeEmployeeInviteCandidateReader(candidates));

    private static EmployeeInviteCandidate Candidate(Guid id, string name = "Test Person") =>
        new(id, name, $"{name.Replace(' ', '.')}@test.com", Guid.NewGuid(), "Engineer");

    [Fact]
    public async Task HandleAsync_Returns_Current_Employees_With_No_Account_Or_Invite()
    {
        var companyId = Guid.NewGuid();
        var a = Candidate(Guid.NewGuid(), "Alice Adams");
        var b = Candidate(Guid.NewGuid(), "Bob Brown");
        var handler = BuildHandler(a, b);

        var result = await handler.HandleAsync(new ListInvitableEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        var first = result.Value.Items.Single(i => i.EmployeeId == a.EmployeeId);
        Assert.Equal("Alice Adams", first.Name);
        Assert.Equal(a.WorkEmail, first.WorkEmail);
        Assert.Equal(a.PositionProfileId, first.PositionProfileId);
        Assert.Equal("Engineer", first.PositionTitle);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_Reader_Has_No_Candidates()
    {
        var handler = BuildHandler();

        var result = await handler.HandleAsync(new ListInvitableEmployeesRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_With_ApplicationUser()
    {
        var companyId = Guid.NewGuid();
        var withAccount = Candidate(Guid.NewGuid(), "Has Account");
        var invitable = Candidate(Guid.NewGuid(), "No Account");

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(withAccount.EmployeeId, "has@test.com", "hash", "Has", "Account", Now));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler(withAccount, invitable)
            .HandleAsync(new ListInvitableEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([invitable.EmployeeId], result.Value.Items.Select(i => i.EmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_With_UserProfile()
    {
        var companyId = Guid.NewGuid();
        var withProfile = Candidate(Guid.NewGuid(), "Has Profile");
        var invitable = Candidate(Guid.NewGuid(), "No Profile");

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(withProfile.EmployeeId, Guid.NewGuid(), companyId, "p@test.com", "Has", "Profile", Now));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler(withProfile, invitable)
            .HandleAsync(new ListInvitableEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([invitable.EmployeeId], result.Value.Items.Select(i => i.EmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_With_Pending_NonExpired_Invite()
    {
        var companyId = Guid.NewGuid();
        var pending = Candidate(Guid.NewGuid(), "Pending Invitee");
        var invitable = Candidate(Guid.NewGuid(), "Fresh Invitee");

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(pending.EmployeeId, companyId, "pending@test.com", Now));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler(pending, invitable)
            .HandleAsync(new ListInvitableEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([invitable.EmployeeId], result.Value.Items.Select(i => i.EmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Includes_Employee_Whose_Only_Invite_Is_Expired()
    {
        var companyId = Guid.NewGuid();
        var expired = Candidate(Guid.NewGuid(), "Expired Invitee");

        await using (var db = fixture.BuildContext())
        {
            // ExpiresAt = LongAgo + 7 days, still in the past => IsExpired.
            db.UserInvites.Add(UserInvite.Create(expired.EmployeeId, companyId, "expired@test.com", LongAgo));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler(expired)
            .HandleAsync(new ListInvitableEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([expired.EmployeeId], result.Value.Items.Select(i => i.EmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Includes_Employee_Whose_Only_Invite_Is_Cancelled()
    {
        var companyId = Guid.NewGuid();
        var cancelled = Candidate(Guid.NewGuid(), "Cancelled Invitee");

        await using (var db = fixture.BuildContext())
        {
            var invite = UserInvite.Create(cancelled.EmployeeId, companyId, "cancelled@test.com", Now);
            invite.Cancel(Now);
            db.UserInvites.Add(invite);
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler(cancelled)
            .HandleAsync(new ListInvitableEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([cancelled.EmployeeId], result.Value.Items.Select(i => i.EmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Ignores_Pending_Invite_For_A_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employee = Candidate(Guid.NewGuid(), "Cross Company");

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employee.EmployeeId, otherCompanyId, "x@test.com", Now));
            await db.SaveChangesAsync();
        }

        var result = await BuildHandler(employee)
            .HandleAsync(new ListInvitableEmployeesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([employee.EmployeeId], result.Value.Items.Select(i => i.EmployeeId));
    }
}
