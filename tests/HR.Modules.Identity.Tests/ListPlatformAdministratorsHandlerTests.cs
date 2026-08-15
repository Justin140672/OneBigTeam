using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.ListPlatformAdministrators;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ListPlatformAdministratorsHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private ListPlatformAdministratorsHandler BuildHandler() => new(fixture.BuildContext());

    private async Task<string> SeedSupportStaffAsync(bool isEnabled = true)
    {
        var email = $"support-{Guid.NewGuid():N}@test.com";
        await using var db = fixture.BuildContext();
        var admin = PlatformAdministrator.Create(email, PlatformAdministratorRole.SupportStaff, Now);
        if (!isEnabled)
            admin.Disable(Now, actorUserId: null);
        db.PlatformAdministrators.Add(admin);
        await db.SaveChangesAsync();
        return email;
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_Not_An_Enabled_Administrator()
    {
        var handler = BuildHandler();
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), "not-an-admin@test.com");

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unauthorized_When_Caller_Is_A_Disabled_Administrator()
    {
        var email = await SeedSupportStaffAsync(isEnabled: false);
        var handler = BuildHandler();
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), email);

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_For_Enabled_SupportStaff_Caller_Not_Just_PlatformOwner()
    {
        var email = await SeedSupportStaffAsync(isEnabled: true);
        await SeedSupportStaffAsync(isEnabled: true);
        var handler = BuildHandler();
        var currentUser = new FakeCurrentUser(Guid.NewGuid(), email);

        var result = await handler.HandleAsync(currentUser, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Administrators.Count >= 2);
        Assert.Equal(
            result.Value.Administrators.Select(a => a.Email).OrderBy(e => e, StringComparer.Ordinal),
            result.Value.Administrators.Select(a => a.Email));
    }
}
