using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class PlatformAdministratorTestHelpers
{
    public static async Task<(Guid Id, string Email)> SeedAdministratorAsync(
        ApiWebApplicationFactory factory,
        PlatformAdministratorRole role,
        bool isEnabled = true,
        string? email = null,
        Guid? supabaseAuthUserId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var normalizedEmail = (email ?? $"platform-admin-{Guid.NewGuid():N}@test.example").ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var administrator = PlatformAdministrator.Create(
            normalizedEmail,
            role,
            now,
            createdByUserId: null,
            supabaseAuthUserId: supabaseAuthUserId);
        if (!isEnabled)
            administrator.Disable(now, actorUserId: null);

        db.PlatformAdministrators.Add(administrator);
        await db.SaveChangesAsync();

        return (administrator.Id, administrator.Email);
    }

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
