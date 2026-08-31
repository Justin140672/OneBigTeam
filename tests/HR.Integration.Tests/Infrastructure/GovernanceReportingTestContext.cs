using System.Net;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// ADM-08 shared setup for the governance reporting hub endpoint tests. Every governance endpoint
/// (Get and Export, all four reports) is gated by BOTH <c>reporting:view</c> and
/// <c>reporting:view-governance</c> — only <see cref="SystemRoles.HrAdministrator"/> holds the
/// second. These helpers centralise the auth matrix so each endpoint test only states its URL.
/// </summary>
internal static class GovernanceReportingTestContext
{
    /// <summary>
    /// Roles that must be rejected with 403. Manager is the load-bearing case: it grants the
    /// baseline <c>reporting:view</c> but not <c>reporting:view-governance</c>, so a 403 here proves
    /// BOTH policies are required (FastEndpoints AND semantics).
    /// </summary>
    public static IEnumerable<object[]> ForbiddenRoles() => new[]
    {
        new object[] { SystemRoles.Employee },
        new object[] { SystemRoles.Manager },
        new object[] { SystemRoles.Recruiter },
        new object[] { SystemRoles.CompanyAdministrator },
    };

    public static async Task<HttpClient> ClientForAsync(
        ApiWebApplicationFactory factory, Guid companyId, Guid userId, Guid roleId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, roleId, companyId);
        return client;
    }

    public static Task<HttpClient> HrAdminClientAsync(ApiWebApplicationFactory factory, Guid companyId) =>
        ClientForAsync(factory, companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

    private static readonly string[] SensitiveTokens =
        ["salary", "national insurance", " ni ", "nino", "bank", "sort code", "token", "password"];

    public static void AssertNoSensitiveData(string body)
    {
        var lower = body.ToLowerInvariant();
        foreach (var token in SensitiveTokens)
            Assert.DoesNotContain(token, lower);
    }
}
