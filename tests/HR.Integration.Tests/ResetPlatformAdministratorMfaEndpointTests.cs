using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// See CreatePlatformAdministratorEndpointTests for notes on the "platform:admin" policy /
/// handler-level PlatformOwner gate and the 401-anonymous / 403-non-owner behavior. ADM-06 made this
/// a real MFA reset: the handler calls ISupabaseAuthGateway.RemoveAllMfaFactorsAsync, which
/// ApiWebApplicationFactory replaces with FakeSupabaseAuthGateway so no live Supabase call is made.
/// </summary>
[Collection("Integration")]
public class ResetPlatformAdministratorMfaEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ResetPlatformAdministratorMfaEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static StringContent Body(bool confirmed = true, string? reason = "administrative reset for test") =>
        new(
            reason is null
                ? $"{{\"confirmed\":{confirmed.ToString().ToLowerInvariant()}}}"
                : $"{{\"confirmed\":{confirmed.ToString().ToLowerInvariant()},\"reason\":\"{reason}\"}}",
            Encoding.UTF8, "application/json");

    private static string Url(Guid id) => $"/api/platform-administrators/{id}/reset-mfa";

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Url(Guid.NewGuid()), Body());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_Forbidden_When_Caller_Is_Not_A_PlatformOwner()
    {
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), "not-an-owner@test.example");

        var response = await client.PostAsync(Url(Guid.NewGuid()), Body());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_NotFound_When_Administrator_Missing()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(Url(Guid.NewGuid()), Body());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_BadRequest_When_Not_Confirmed()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());
        var (targetId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, supabaseAuthUserId: Guid.NewGuid());
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(Url(targetId), Body(confirmed: false));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_BadRequest_When_Reason_Missing()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());
        var (targetId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, supabaseAuthUserId: Guid.NewGuid());
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(Url(targetId), Body(reason: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_Conflict_When_Target_Has_No_Linked_Identity()
    {
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());
        var (targetId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(Url(targetId), Body());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // The last-enabled-PlatformOwner safeguard is covered exhaustively at the handler unit-test
    // level (ResetPlatformAdministratorMfaHandlerTests). It is not reproduced here: the shared
    // integration fixture DB always carries other bootstrap-seeded enabled PlatformOwner rows, and
    // disabling them to force the scenario would corrupt state relied on by sibling test classes.

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Resets_Mfa_On_Happy_Path()
    {
        _factory.SupabaseAuthGateway.MfaFactorsRemovedToReturn = 2;
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());
        var targetSupabaseId = Guid.NewGuid();
        var (targetId, targetEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, supabaseAuthUserId: targetSupabaseId);
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(Url(targetId), Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ResetMfaPayload>();
        Assert.NotNull(payload);
        Assert.Equal(targetId, payload!.AdministratorId);
        Assert.Equal(targetEmail, payload.AdministratorEmail);
        Assert.Equal(2, payload.FactorsRemoved);

        Assert.Contains(targetSupabaseId, _factory.SupabaseAuthGateway.MfaFactorRemovals);
        Assert.Contains(_factory.EmailSender.Sent, m => m.ToEmail == targetEmail);
    }

    [Fact]
    public async Task Post_ResetPlatformAdministratorMfa_Returns_Error_Without_Provider_Internals_When_Gateway_Fails()
    {
        _factory.SupabaseAuthGateway.ShouldThrowOnRemoveMfaFactors = true;
        var (_, ownerEmail) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.PlatformOwner, supabaseAuthUserId: Guid.NewGuid());
        var (targetId, _) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff, supabaseAuthUserId: Guid.NewGuid());
        using var client = PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), ownerEmail);

        var response = await client.PostAsync(Url(targetId), Body());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Response body", raw);
        Assert.DoesNotContain("InternalServerError", raw);
    }

    private sealed record ResetMfaPayload(Guid AdministratorId, string AdministratorEmail, int FactorsRemoved, bool NotificationDelivered);
}
