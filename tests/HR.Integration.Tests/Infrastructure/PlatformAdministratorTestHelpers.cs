using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Shared setup for the Admin User Management (PlatformAdministrator) integration tests. Seeds
/// PlatformAdministrator rows directly in the identity schema and wires up an authenticated
/// TestAuthHandler client for a given caller. Unlike ApplicationUser/company-scoped callers, the
/// "platform:admin" endpoints authorize purely off ICurrentUser.Email matching an enabled
/// PlatformAdministrator row (no tenant/company header, no SystemRoles/TestRoleSeeder
/// involvement) — see CreatePlatformAdministratorHandler.IsEnabledPlatformOwnerAsync and
/// ListPlatformAdministratorsHandler.IsEnabledPlatformAdministratorAsync.
/// </summary>
internal static class PlatformAdministratorTestHelpers
{
    public static async Task<(Guid Id, string Email)> SeedAdministratorAsync(
        ApiWebApplicationFactory factory,
        PlatformAdministratorRole role,
        bool isEnabled = true,
        string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var normalizedEmail = (email ?? $"platform-admin-{Guid.NewGuid():N}@test.example").ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var administrator = PlatformAdministrator.Create(normalizedEmail, role, now);
        if (!isEnabled)
            administrator.Disable(now, actorUserId: null);

        db.PlatformAdministrators.Add(administrator);
        await db.SaveChangesAsync();

        return (administrator.Id, administrator.Email);
    }

    /// <summary>
    /// Builds an authenticated HttpClient (no tenant header — "platform:admin" is company-agnostic)
    /// carrying the given caller's email on the "email" claim, which is what the handler-level
    /// PlatformOwner/enabled-administrator gate matches against.
    /// </summary>
    public static HttpClient ClientFor(ApiWebApplicationFactory factory, Guid userId, string? email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }
}
