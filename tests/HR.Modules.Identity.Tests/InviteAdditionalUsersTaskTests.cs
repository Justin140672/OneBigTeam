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
        context.Users.Add(ApplicationUser.Create(employeeId1, "alice@example.com", "hash", "Alice", "Smith", Now));
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
        context.Users.Add(ApplicationUser.Create(employeeId1, "alice@example.com", "hash", "Alice", "Smith", Now));
        context.Users.Add(ApplicationUser.Create(employeeId2, "bob@example.com", "hash", "Bob", "Jones", Now));
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
        var user1 = ApplicationUser.Create(employeeId1, "alice@example.com", "hash", "Alice", "Smith", Now);
        var user2 = ApplicationUser.Create(employeeId2, "bob@example.com", "hash", "Bob", "Jones", Now);
        user2.Deactivate(Now);
        context.Users.Add(user1);
        context.Users.Add(user2);
        await context.SaveChangesAsync();

        var task = new InviteAdditionalUsersTask(context, new FakeEmployeeAudienceReader([employeeId1, employeeId2]));

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
