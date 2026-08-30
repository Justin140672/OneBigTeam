using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Shared setup for the User Administration integration tests (ListUsers, GetUserDetails,
/// GetUserAuditHistory, InviteEmployeeUser, UpdateUserRoles, ResendInvite, CancelInvite,
/// DisableUser, EnableUser). Seeds a real Employee row (via EF, fastest path — mirrors
/// EmployeeReferenceDataSeeder) so IEmployeeNameReader resolves a name, and provides helpers for
/// seeding ApplicationUser/UserInvite rows directly in the identity schema.
/// </summary>
internal static class IdentityUserAdminTestHelpers
{
    public static async Task<Guid> SeedEmployeeAsync(
        ApiWebApplicationFactory factory,
        Guid companyId,
        string firstName = "Test",
        string lastName = "Employee")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);

        var employeeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var employee = Employee.Create(
            employeeId,
            companyId,
            firstName,
            lastName,
            workEmail: $"{firstName}.{lastName}.{Guid.NewGuid():N}@test.example".ToLowerInvariant(),
            startDate: new DateOnly(2026, 1, 1),
            hasSystemAccess: false,
            dateOfBirth: new DateOnly(1990, 1, 1),
            nationality: "British",
            gender: "Prefer not to say",
            employeeNumber: $"EMP-{Guid.NewGuid():N}",
            employmentTypeId: referenceData.EmploymentTypeId,
            departmentId: referenceData.DepartmentId,
            locationId: referenceData.LocationId,
            positionProfileId: referenceData.PositionProfileId,
            now: now);

        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        return employeeId;
    }

    public static async Task<Guid> SeedApplicationUserAsync(
        ApiWebApplicationFactory factory,
        Guid employeeId,
        string email,
        bool isActive = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var now = DateTimeOffset.UtcNow;
        var user = ApplicationUser.Create(employeeId, email, "not-used-in-tests", "Test", "User", now);
        if (!isActive)
            user.Deactivate(now);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    public static async Task<Guid> SeedUserProfileAsync(
        ApiWebApplicationFactory factory,
        Guid companyId,
        Guid employeeId,
        string email,
        string firstName = "Test",
        string lastName = "User")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var now = DateTimeOffset.UtcNow;
        var profile = UserProfile.Create(employeeId, Guid.NewGuid(), companyId, email, firstName, lastName, now);

        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();

        return profile.Id;
    }

    public static async Task<Guid> SeedInviteAsync(
        ApiWebApplicationFactory factory,
        Guid companyId,
        Guid employeeId,
        string email,
        bool claimed = false,
        bool cancelled = false,
        IEnumerable<Guid>? roleIds = null,
        Guid? createdByUserId = null,
        DateTimeOffset? createdAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // createdAt backdates both CreatedAt and (CreatedAt + 7 days) ExpiresAt, so passing a value
        // more than 7 days in the past yields an already-expired invite.
        var now = createdAt ?? DateTimeOffset.UtcNow;
        var invite = UserInvite.Create(employeeId, companyId, email, now, roleIds, createdByUserId);

        if (claimed)
            invite.Claim(now);
        if (cancelled)
            invite.Cancel(now);

        db.UserInvites.Add(invite);
        await db.SaveChangesAsync();

        return invite.Id;
    }

    public static async Task<Guid> SeedRoleAsync(ApiWebApplicationFactory factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var roleId = Guid.NewGuid();
        db.Roles.Add(Role.Create(roleId, name, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        return roleId;
    }
}
