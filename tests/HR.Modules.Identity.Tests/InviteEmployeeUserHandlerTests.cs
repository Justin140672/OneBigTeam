using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.InviteEmployeeUser;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class InviteEmployeeUserHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private InviteEmployeeUserHandler BuildHandler(
        FakeEmployeeNameReader nameReader,
        FakeAuditEventPublisher auditPublisher,
        FakeInvitationEmailSender? emailSender = null) =>
        new(
            fixture.BuildContext(),
            Clock,
            nameReader,
            emailSender ?? new FakeInvitationEmailSender(),
            new FakeInviteLinkBuilder(),
            auditPublisher);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Resolve()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var nameReader = new FakeEmployeeNameReader(); // no names => employee not found
        var handler = BuildHandler(nameReader, new FakeAuditEventPublisher());

        var request = new InviteEmployeeUserRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = "new.hire@test.com",
            RoleIds = [Guid.NewGuid()],
        };

        var result = await handler.HandleAsync(request, actorUserId: Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Employee_Already_Has_Linked_User()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "existing@test.com", "hash", "Existing", "User", Now));
            await db.SaveChangesAsync();
        }

        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Existing User" });
        var handler = BuildHandler(nameReader, new FakeAuditEventPublisher());

        var request = new InviteEmployeeUserRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = "existing@test.com",
            RoleIds = [Guid.NewGuid()],
        };

        var result = await handler.HandleAsync(request, actorUserId: Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Invite_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "New Hire" });
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(nameReader, auditPublisher);

        var request = new InviteEmployeeUserRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = "new.hire@test.com",
            RoleIds = [roleId],
        };

        var result = await handler.HandleAsync(request, actorUserId: actorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal("new.hire@test.com", result.Value.Email);

        await using var db = fixture.BuildContext();
        var invite = await db.UserInvites.FirstOrDefaultAsync(i => i.EmployeeId == employeeId);
        Assert.NotNull(invite);
        Assert.Equal(actorId, invite!.CreatedByUserId);
        Assert.Contains(roleId, invite.PendingRoleIds);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserInvitedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Removes_Existing_Unclaimed_Invites_For_Employee()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "old@test.com", Now));
            await db.SaveChangesAsync();
        }

        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "New Hire" });
        var handler = BuildHandler(nameReader, new FakeAuditEventPublisher());

        var request = new InviteEmployeeUserRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = "new@test.com",
            RoleIds = [Guid.NewGuid()],
        };

        await handler.HandleAsync(request, actorUserId: null, CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var invites = await db2.UserInvites.Where(i => i.EmployeeId == employeeId).ToListAsync();
        Assert.Single(invites);
        Assert.Equal("new@test.com", invites[0].Email);
    }

    [Fact]
    public async Task HandleAsync_Uses_UserInvitation_Template_With_Correct_ActionUrl()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var emailSender = new FakeInvitationEmailSender();
        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });
        var handler = BuildHandler(nameReader, new FakeAuditEventPublisher(), emailSender);

        var request = new InviteEmployeeUserRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = "jane@test.com",
            RoleIds = [Guid.NewGuid()],
        };

        var result = await handler.HandleAsync(request, actorUserId: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal("jane@test.com", sent.ToEmail);
        Assert.Equal("Jane Doe", sent.RecipientName);
        Assert.Contains("/invite/", sent.ActionUrl);
    }

    [Fact]
    public async Task HandleAsync_Returns_EmailSent_True_When_Sender_Succeeds()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var emailSender = new FakeInvitationEmailSender(succeeds: true);
        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Test User" });
        var handler = BuildHandler(nameReader, new FakeAuditEventPublisher(), emailSender);

        var request = new InviteEmployeeUserRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = "test@test.com",
            RoleIds = [Guid.NewGuid()],
        };

        var result = await handler.HandleAsync(request, actorUserId: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.EmailSent);
    }

    [Fact]
    public async Task HandleAsync_Returns_EmailSent_False_And_Saves_Invite_When_Sender_Fails()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var emailSender = new FakeInvitationEmailSender(succeeds: false);
        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Test User" });
        var handler = BuildHandler(nameReader, new FakeAuditEventPublisher(), emailSender);

        var request = new InviteEmployeeUserRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Email = "fail@test.com",
            RoleIds = [Guid.NewGuid()],
        };

        var result = await handler.HandleAsync(request, actorUserId: null, CancellationToken.None);

        // Invite must be persisted even when email fails.
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EmailSent);

        await using var db = fixture.BuildContext();
        var invite = await db.UserInvites.FirstOrDefaultAsync(i => i.EmployeeId == employeeId);
        Assert.NotNull(invite);
    }
}
