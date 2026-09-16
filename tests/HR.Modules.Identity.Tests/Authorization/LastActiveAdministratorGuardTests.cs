using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests.Authorization;

// Ticket 10 (P1): HasOtherActiveHolderAsync previously counted any UserProfile row — active or
// not — as an active protected-role holder. It now requires UserProfiles.IsActive == true.
[Collection("IdentityDatabase")]
public class LastActiveAdministratorGuardTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private LastActiveAdministratorGuard BuildGuard(IReadOnlyList<Guid> companyEmployeeIds) =>
        new(fixture.BuildContext(), new FakeEmployeeAudienceReader(companyEmployeeIds));

    private async Task<Guid> SeedRole(string name)
    {
        await using var db = fixture.BuildContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(Role.Create(roleId, name, Now));
        await db.SaveChangesAsync();
        return roleId;
    }

    [Fact]
    public async Task HasOtherActiveHolderAsync_Reports_No_Other_Active_Holder_When_Only_Other_Profile_Is_Disabled()
    {
        var companyId = Guid.NewGuid();
        var roleId = await SeedRole("CompanyAdministrator-Profile-Guard");
        var excludedUserId = Guid.NewGuid();
        var disabledProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            var profile = UserProfile.Create(
                disabledProfileId, Guid.NewGuid(), companyId, "disabled-admin@test.com", "Disabled", "Admin", Now);
            profile.Deactivate(Now);
            db.UserProfiles.Add(profile);

            db.UserRoles.Add(UserRole.Create(excludedUserId, roleId, Now));
            db.UserRoles.Add(UserRole.Create(disabledProfileId, roleId, Now));
            await db.SaveChangesAsync();
        }

        var guard = BuildGuard([excludedUserId, disabledProfileId]);

        var result = await guard.HasOtherActiveHolderAsync(
            roleId, excludedUserId, [excludedUserId, disabledProfileId], CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasOtherActiveHolderAsync_Reports_Other_Active_Holder_When_Other_Profile_Is_Active()
    {
        var companyId = Guid.NewGuid();
        var roleId = await SeedRole("CompanyAdministrator-Profile-Guard-Active");
        var excludedUserId = Guid.NewGuid();
        var activeProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(
                activeProfileId, Guid.NewGuid(), companyId, "active-admin@test.com", "Active", "Admin", Now));

            db.UserRoles.Add(UserRole.Create(excludedUserId, roleId, Now));
            db.UserRoles.Add(UserRole.Create(activeProfileId, roleId, Now));
            await db.SaveChangesAsync();
        }

        var guard = BuildGuard([excludedUserId, activeProfileId]);

        var result = await guard.HasOtherActiveHolderAsync(
            roleId, excludedUserId, [excludedUserId, activeProfileId], CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasOtherActiveHolderAsync_Reports_Other_Active_Holder_When_ApplicationUser_Is_Active()
    {
        var roleId = await SeedRole("CompanyAdministrator-User-Guard-Active");
        var excludedUserId = Guid.NewGuid();
        var activeUserId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(activeUserId, "active-user-admin@test.com", "hash", "Active", "Admin", Now));

            db.UserRoles.Add(UserRole.Create(excludedUserId, roleId, Now));
            db.UserRoles.Add(UserRole.Create(activeUserId, roleId, Now));
            await db.SaveChangesAsync();
        }

        var guard = BuildGuard([excludedUserId, activeUserId]);

        var result = await guard.HasOtherActiveHolderAsync(
            roleId, excludedUserId, [excludedUserId, activeUserId], CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasOtherActiveHolderAsync_Reports_No_Other_Active_Holder_When_Other_ApplicationUser_Is_Disabled()
    {
        var roleId = await SeedRole("CompanyAdministrator-User-Guard-Disabled");
        var excludedUserId = Guid.NewGuid();
        var disabledUserId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            var user = ApplicationUser.Create(disabledUserId, "disabled-user-admin@test.com", "hash", "Disabled", "Admin", Now);
            user.Deactivate(Now);
            db.Users.Add(user);

            db.UserRoles.Add(UserRole.Create(excludedUserId, roleId, Now));
            db.UserRoles.Add(UserRole.Create(disabledUserId, roleId, Now));
            await db.SaveChangesAsync();
        }

        var guard = BuildGuard([excludedUserId, disabledUserId]);

        var result = await guard.HasOtherActiveHolderAsync(
            roleId, excludedUserId, [excludedUserId, disabledUserId], CancellationToken.None);

        Assert.False(result);
    }
}
