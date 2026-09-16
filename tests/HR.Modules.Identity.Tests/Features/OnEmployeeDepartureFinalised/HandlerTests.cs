using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.OnEmployeeDepartureFinalised;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests.Features.OnEmployeeDepartureFinalised;

// P1 fix (departure access disablement): this handler is now the ONLY trigger that reacts to an
// employee's departure to enqueue durable disablement of the linked ApplicationUser — replaces
// the former OnOffboardingPlanCompleted handler (deleted; offboarding-plan completion must never
// disable an account by itself).
[Collection("IdentityDatabase")]
public class HandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private static Handler BuildHandler(IdentityDbContext db, RecordingBackgroundJobClient jobClient) =>
        new(db, Clock, jobClient);

    private static EmployeeDepartureFinalisedIntegrationEvent BuildEvent(
        Guid companyId, Guid employeeId, bool accessDisabled) =>
        new(companyId, employeeId, DateOnly.FromDateTime(Now.Date), Now, accessDisabled);

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_AccessDisabled_Is_False()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "active@test.com", "hash", "Active", "User", Now));
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(fixture.BuildContext(), jobClient);

        await handler.HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: false), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        Assert.False(await db2.AccountDisablements.AnyAsync(d => d.EmployeeId == employeeId));
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_No_Linked_ApplicationUser()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(fixture.BuildContext(), jobClient);

        await handler.HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        Assert.False(await db2.AccountDisablements.AnyAsync(d => d.EmployeeId == employeeId));
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_ApplicationUser_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            var user = ApplicationUser.Create(employeeId, "already-off@test.com", "hash", "Already", "Off", Now);
            user.Deactivate(Now);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(fixture.BuildContext(), jobClient);

        await handler.HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        Assert.False(await db2.AccountDisablements.AnyAsync(d => d.EmployeeId == employeeId));
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Creates_Pending_AccountDisablement_And_Enqueues_Job_For_Active_User()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "departing@test.com", "hash", "Dep", "Arting", Now));
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(fixture.BuildContext(), jobClient);

        await handler.HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var request = await db2.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId);
        Assert.Equal(AccountDisablement.StatusPending, request.Status);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(employeeId, request.ApplicationUserId);

        var enqueued = Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(typeof(AccountDisablementJob), enqueued.Type);
        Assert.Equal(nameof(AccountDisablementJob.ProcessAsync), enqueued.Method.Name);
        Assert.Equal(request.Id, enqueued.Args[0]);
        Assert.Equal(companyId, enqueued.Args[1]);
    }

    [Fact]
    public async Task HandleAsync_Redelivery_Only_Ever_Creates_One_AccountDisablement_Row_And_Enqueues_Once()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "redelivered@test.com", "hash", "Re", "Delivered", Now));
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();

        await BuildHandler(fixture.BuildContext(), jobClient)
            .HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);
        await BuildHandler(fixture.BuildContext(), jobClient)
            .HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        Assert.Single(await db2.AccountDisablements.Where(d => d.EmployeeId == employeeId).ToListAsync());
        Assert.Single(jobClient.CreatedJobs);
    }

    // Ticket 10 (P1): profile-only (real Supabase-backed) accounts previously never got a
    // disablement request created for them at all — this handler only ever looked in db.Users.
    [Fact]
    public async Task HandleAsync_Creates_Pending_AccountDisablement_And_Enqueues_Job_For_Active_ProfileOnly_Account()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "profile-only@test.com", "Prof", "Ile", Now));
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(fixture.BuildContext(), jobClient);

        await handler.HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var request = await db2.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId);
        Assert.Equal(AccountDisablement.StatusPending, request.Status);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(employeeId, request.ApplicationUserId);

        var enqueued = Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(typeof(AccountDisablementJob), enqueued.Type);
        Assert.Equal(nameof(AccountDisablementJob.ProcessAsync), enqueued.Method.Name);
        Assert.Equal(request.Id, enqueued.Args[0]);
        Assert.Equal(companyId, enqueued.Args[1]);
    }

    [Fact]
    public async Task HandleAsync_Is_NoOp_When_ProfileOnly_Account_Already_Inactive()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            var profile = UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "already-off-profile@test.com", "Already", "Off", Now);
            profile.Deactivate(Now);
            db.UserProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(fixture.BuildContext(), jobClient);

        await handler.HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        Assert.False(await db2.AccountDisablements.AnyAsync(d => d.EmployeeId == employeeId));
        Assert.Empty(jobClient.CreatedJobs);
    }

    // Both an ApplicationUser row and a UserProfile row can exist for the same employee id
    // (e.g. a legacy local-auth account never cleaned up alongside a newer Supabase profile) —
    // the handler must prefer/act on the ApplicationUser and never fall back to the profile
    // when a user row is present.
    [Fact]
    public async Task HandleAsync_Prefers_ApplicationUser_Over_UserProfile_When_Both_Exist()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "both-user@test.com", "hash", "Both", "User", Now));
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "both-profile@test.com", "Both", "Profile", Now));
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(fixture.BuildContext(), jobClient);

        await handler.HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        var request = await db2.AccountDisablements.SingleAsync(d => d.EmployeeId == employeeId);
        Assert.Equal(employeeId, request.ApplicationUserId); // resolved via ApplicationUser, same id by convention

        var enqueued = Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(request.Id, enqueued.Args[0]);
    }

    [Fact]
    public async Task HandleAsync_Redelivery_For_ProfileOnly_Account_Only_Ever_Creates_One_AccountDisablement_Row()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "redelivered-profile@test.com", "Re", "Delivered", Now));
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();

        await BuildHandler(fixture.BuildContext(), jobClient)
            .HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);
        await BuildHandler(fixture.BuildContext(), jobClient)
            .HandleAsync(BuildEvent(companyId, employeeId, accessDisabled: true), CancellationToken.None);

        await using var db2 = fixture.BuildContext();
        Assert.Single(await db2.AccountDisablements.Where(d => d.EmployeeId == employeeId).ToListAsync());
        Assert.Single(jobClient.CreatedJobs);
    }
}
