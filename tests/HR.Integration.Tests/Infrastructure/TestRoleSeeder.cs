using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Seeds direct user-role assignments in the identity schema so integration
/// tests can exercise permission-guarded endpoints with realistic role data.
/// </summary>
internal static class TestRoleSeeder
{
    public static async Task AssignRoleAsync(
        ApiWebApplicationFactory factory,
        Guid userId,
        Guid roleId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Ensure the ApplicationUser exists (required by FK on user_roles).
        var userExists = await db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
        {
            db.Users.Add(ApplicationUser.Create(
                userId,
                email: $"testuser-{userId:N}@test.internal",
                passwordHash: "not-used-in-tests",
                firstName: "Test",
                lastName: "User",
                now: DateTimeOffset.UtcNow));
        }

        var roleExists = await db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (!roleExists)
            db.UserRoles.Add(UserRole.Create(userId, roleId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }
}
