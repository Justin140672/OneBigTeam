using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services.OnboardingTasks;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

public class InviteAdditionalUsersTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Zero_Active_Users_Among_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader(employeeIds));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Only_One_Active_User_Among_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        context.UserProfiles.Add(UserProfile.Create(employeeId1, Guid.NewGuid(), Guid.Empty, "alice@example.com", "Alice", "Smith", Now));
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader([employeeId1, employeeId2]));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_Two_Or_More_Active_Users_Among_Employees()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        context.UserProfiles.Add(UserProfile.Create(employeeId1, Guid.NewGuid(), Guid.Empty, "alice@example.com", "Alice", "Smith", Now));
        context.UserProfiles.Add(UserProfile.Create(employeeId2, Guid.NewGuid(), Guid.Empty, "bob@example.com", "Bob", "Jones", Now));
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader([employeeId1, employeeId2]));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Excludes_Inactive_Users()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        var user1 = UserProfile.Create(employeeId1, Guid.NewGuid(), Guid.Empty, "alice@example.com", "Alice", "Smith", Now);
        var user2 = UserProfile.Create(employeeId2, Guid.NewGuid(), Guid.Empty, "bob@example.com", "Bob", "Jones", Now);
        user2.Deactivate(Now);
        context.UserProfiles.Add(user1);
        context.UserProfiles.Add(user2);
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader([employeeId1, employeeId2]));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_A_UserInvite_Exists_But_EmailSentAt_Is_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        context.UserInvites.Add(UserInvite.Create(employeeId, companyId, "queued@test.com", Now));
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader([employeeId]));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_Once_A_UserInvites_EmailSentAt_Is_Set()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var invite = UserInvite.Create(employeeId, companyId, "sent@test.com", Now);
        invite.MarkEmailSent(Now.AddMinutes(1));
        context.UserInvites.Add(invite);
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader([employeeId]));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_A_Bulk_Created_Invites_EmailSentAt_Is_Set()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var invite = UserInvite.Create(employeeId, companyId, "bulk-sent@test.com", Now, roleIds: []);
        invite.MarkEmailSent(Now.AddMinutes(1));
        context.UserInvites.Add(invite);
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader([employeeId]));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_All_Batch_Recipients_Were_Skipped_Or_Failed()
    {
        // No UserInvite ever got its EmailSentAt set for these employees — a Skipped recipient never
        // even gets a UserInvite created, and a Failed recipient's invite (if created before the
        // failure) never reaches MarkEmailSent, so this must not report the task complete.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var skippedEmployeeId = Guid.NewGuid();
        var failedEmployeeId = Guid.NewGuid();

        context.UserInvites.Add(UserInvite.Create(failedEmployeeId, companyId, "failed-recipient@test.com", Now));
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(
            context, new FakeEmployeeAudienceReader([skippedEmployeeId, failedEmployeeId]));

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    private static IdentityDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new IdentityDbContext(options);
    }
}
