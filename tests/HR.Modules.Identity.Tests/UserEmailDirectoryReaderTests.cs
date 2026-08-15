using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Exercises the real IdentityDbContext-backed UserEmailDirectoryReader (including its
/// EF.Functions.ILike-based FindUserIdsByEmailAsync search, which requires a real Postgres provider
/// — see IdentityDatabaseFixture) — the "Infrastructure.Abstractions port implemented by Identity"
/// consumed by HR.Modules.Companies' GetAuditLog feature via a fake in Companies.Tests
/// (FakeUserEmailDirectoryReader), same shape as CompanyUserEmailSearchReader.
/// </summary>
[Collection("IdentityDatabase")]
public class UserEmailDirectoryReaderTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private async Task<UserProfile> SeedUserProfileAsync(string email)
    {
        await using var db = fixture.BuildContext();
        var profile = UserProfile.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), email, "Test", "User", Now);
        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    [Fact]
    public async Task GetEmailsByUserIdsAsync_Returns_Dictionary_Keyed_By_SupabaseAuthUserId()
    {
        var profile = await SeedUserProfileAsync($"lookup-{Guid.NewGuid():N}@example.com");

        var reader = new UserEmailDirectoryReader(fixture.BuildContext());

        var result = await reader.GetEmailsByUserIdsAsync([profile.SupabaseAuthUserId], CancellationToken.None);

        Assert.True(result.ContainsKey(profile.SupabaseAuthUserId));
        Assert.Equal(profile.Email, result[profile.SupabaseAuthUserId]);
    }

    [Fact]
    public async Task GetEmailsByUserIdsAsync_Returns_Empty_Dictionary_For_Empty_Input()
    {
        var reader = new UserEmailDirectoryReader(fixture.BuildContext());

        var result = await reader.GetEmailsByUserIdsAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEmailsByUserIdsAsync_Does_Not_Return_Unrequested_Ids()
    {
        var requested = await SeedUserProfileAsync($"requested-{Guid.NewGuid():N}@example.com");
        var notRequested = await SeedUserProfileAsync($"not-requested-{Guid.NewGuid():N}@example.com");

        var reader = new UserEmailDirectoryReader(fixture.BuildContext());

        var result = await reader.GetEmailsByUserIdsAsync([requested.SupabaseAuthUserId], CancellationToken.None);

        Assert.True(result.ContainsKey(requested.SupabaseAuthUserId));
        Assert.False(result.ContainsKey(notRequested.SupabaseAuthUserId));
    }

    [Fact]
    public async Task FindUserIdsByEmailAsync_Matches_Case_Insensitively_And_By_Partial_Term()
    {
        var unique = Guid.NewGuid().ToString("N");
        var profile = await SeedUserProfileAsync($"Findable-{unique}@Example.com");

        var reader = new UserEmailDirectoryReader(fixture.BuildContext());

        var result = await reader.FindUserIdsByEmailAsync($"findable-{unique}", CancellationToken.None);

        Assert.Contains(profile.SupabaseAuthUserId, result);
    }

    [Fact]
    public async Task FindUserIdsByEmailAsync_Returns_Distinct_Ids()
    {
        var unique = Guid.NewGuid().ToString("N");
        var profile = await SeedUserProfileAsync($"distinct-{unique}@example.com");

        var reader = new UserEmailDirectoryReader(fixture.BuildContext());

        var result = await reader.FindUserIdsByEmailAsync($"distinct-{unique}", CancellationToken.None);

        var matches = result.Where(id => id == profile.SupabaseAuthUserId).ToList();
        Assert.Single(matches);
    }

    [Fact]
    public async Task FindUserIdsByEmailAsync_Excludes_NonMatching_Emails()
    {
        var unique = Guid.NewGuid().ToString("N");
        var matching = await SeedUserProfileAsync($"matching-{unique}@example.com");
        await SeedUserProfileAsync($"unrelated-{Guid.NewGuid():N}@example.com");

        var reader = new UserEmailDirectoryReader(fixture.BuildContext());

        var result = await reader.FindUserIdsByEmailAsync($"matching-{unique}", CancellationToken.None);

        Assert.Contains(matching.SupabaseAuthUserId, result);
        Assert.Single(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindUserIdsByEmailAsync_Returns_Empty_For_Empty_Or_Whitespace_SearchTerm(string searchTerm)
    {
        await SeedUserProfileAsync($"any-{Guid.NewGuid():N}@example.com");

        var reader = new UserEmailDirectoryReader(fixture.BuildContext());

        var result = await reader.FindUserIdsByEmailAsync(searchTerm, CancellationToken.None);

        Assert.Empty(result);
    }
}
