using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class EmployeeUserAccountStatusReaderTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetStatusesAsync_Returns_Active_For_A_UserProfile_Based_Account()
    {
        // A real Supabase-backed account (AcceptInvite, self-service SignUp) — has a UserProfile
        // row, never an ApplicationUser one. Before this reader also checked UserProfiles, an
        // employee who'd accepted their invite disappeared from the status column entirely: not
        // found in Users, and excluded from the invite fallback because the invite is now Claimed.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "employee@example.com", "Ada", "Lovelace", Now));
            await db.SaveChangesAsync();
        }

        await using var context = fixture.BuildContext();
        var reader = new EmployeeUserAccountStatusReader(context);

        var statuses = await reader.GetStatusesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.True(statuses.TryGetValue(employeeId, out var summary));
        Assert.Equal(EmployeeUserAccountStatus.Active, summary!.Status);
        Assert.Null(summary.LastLoginAt);
    }

    [Fact]
    public async Task GetStatusesAsync_Prefers_ApplicationUser_Over_UserProfile_When_Both_Exist()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "employee@example.com", "hash", "Ada", "Lovelace", Now));
            db.UserProfiles.Add(UserProfile.Create(
                employeeId, Guid.NewGuid(), companyId, "employee@example.com", "Ada", "Lovelace", Now));
            await db.SaveChangesAsync();
        }

        await using var context = fixture.BuildContext();
        var reader = new EmployeeUserAccountStatusReader(context);

        var statuses = await reader.GetStatusesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.True(statuses.TryGetValue(employeeId, out var summary));
        Assert.Equal(EmployeeUserAccountStatus.Active, summary!.Status);
    }

    [Fact]
    public async Task GetStatusesAsync_Returns_PendingInvitation_When_No_User_Or_Profile_Exists()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // UserInvite.IsExpired compares against the real wall clock (DateTimeOffset.UtcNow), not
        // the fixed `Now` used elsewhere in this file, so the invite must be created "now" (not a
        // hardcoded past date) to stay within its 7-day expiry window and exercise the
        // PendingInvitation (not InvitationExpired) branch.
        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "employee@example.com", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await using var context = fixture.BuildContext();
        var reader = new EmployeeUserAccountStatusReader(context);

        var statuses = await reader.GetStatusesAsync(companyId, [employeeId], CancellationToken.None);

        Assert.True(statuses.TryGetValue(employeeId, out var summary));
        Assert.Equal(EmployeeUserAccountStatus.PendingInvitation, summary!.Status);
    }
}
