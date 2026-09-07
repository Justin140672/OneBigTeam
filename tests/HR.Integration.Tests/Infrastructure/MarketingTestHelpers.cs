using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Shared setup for the Marketing platform-admin endpoint tests. The "/api/marketing/admin/*"
/// endpoints use the same "platform:admin" policy as PlatformSettings, so authentication is wired
/// exactly like PlatformSettingsAuthorizationTests: an enabled identity.platform_administrators row
/// matched by SupabaseAuthUserId.
/// </summary>
internal static class MarketingTestHelpers
{
    public static async Task<HttpClient> PlatformAdminClientAsync(ApiWebApplicationFactory factory)
    {
        var userId = Guid.NewGuid();
        await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            factory,
            PlatformAdministratorRole.SupportStaff,
            isEnabled: true,
            supabaseAuthUserId: userId);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        return client;
    }

    public static async Task<HttpClient> CompanyAdminClientAsync(ApiWebApplicationFactory factory)
    {
        var userId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.CompanyAdministrator);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        return client;
    }

    public static string UniqueSlug() => $"itest-{Guid.NewGuid():N}";

    public static object FeatureBody(string slug, string title = "Integration Feature", int displayOrder = 0,
        string deliveryStatus = "Available") => new
    {
        slug,
        iconName = "users",
        title,
        summary = "Integration test summary.",
        intro = "Integration test intro.",
        detailedContent = (string?)null,
        benefits = new[] { "Benefit one", "Benefit two" },
        youTubeId = (string?)null,
        displayOrder,
        deliveryStatus,
    };

    public static object RoadmapBody(string title, int displayOrder = 0, string deliveryStatus = "ComingSoon") => new
    {
        title,
        description = "Integration test description.",
        iconName = "chart-line",
        deliveryStatus,
        displayOrder,
    };
}
