using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers the interim email-confirmation stub (ApplicationUser.IsEmailConfirmed /
/// Features/ConfirmEmail) — a self-service SignUp account starts unconfirmed, and this is the
/// dev-only bypass endpoint (404s outside Development) surfaced by the "Dev tool: confirm this
/// account" link on EmailConfirmationRequired.razor's blocking screen, since there's no real
/// confirmation email to click through yet. ApiWebApplicationFactory runs as Development, so the
/// endpoint is reachable here.
/// </summary>
[Collection("Integration")]
public class ConfirmEmailEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UnconfirmedUser = Guid.NewGuid();

    public ConfirmEmailEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            // TestRoleSeeder creates an already-confirmed ApplicationUser (matching every
            // non-SignUp path); flip it back to unconfirmed afterwards for this test's purpose.
            await TestRoleSeeder.AssignRoleAsync(_factory, UnconfirmedUser, SystemRoles.Employee);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == UnconfirmedUser);
            if (user.IsEmailConfirmed)
            {
                // EF's change tracker can write private-setter properties directly (unlike normal
                // C# code) — used here only to force the "unconfirmed" starting state that
                // TestRoleSeeder doesn't produce, without adding a test-only method to the domain.
                db.Entry(user).Property(nameof(ApplicationUser.IsEmailConfirmed)).CurrentValue = false;
                await db.SaveChangesAsync();
            }
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientFor(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, Guid.NewGuid().ToString());
        return client;
    }

    [Fact]
    public async Task Confirm_Email_Returns_NoContent_And_Marks_The_User_Confirmed()
    {
        using var client = ClientFor(UnconfirmedUser);

        var response = await client.PostAsync("/api/dev/confirm-email", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == UnconfirmedUser);
        Assert.True(user.IsEmailConfirmed);
    }

    [Fact]
    public async Task Get_Me_Reflects_IsEmailConfirmed_True_After_Confirming()
    {
        using var client = ClientFor(UnconfirmedUser);

        await client.PostAsync("/api/dev/confirm-email", null);

        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MePayload>();
        Assert.NotNull(payload);
        Assert.True(payload!.IsEmailConfirmed);
    }

    [Fact]
    public async Task Confirm_Email_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/dev/confirm-email", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record MePayload(bool IsEmailConfirmed);
}
