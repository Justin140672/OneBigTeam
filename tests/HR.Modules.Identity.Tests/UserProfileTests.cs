using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests;

public class UserProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static UserProfile NewProfile() =>
        UserProfile.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "user@test.com", "Test", "User", Now);

    [Fact]
    public void Create_Defaults_To_Active_With_No_Login()
    {
        var profile = NewProfile();

        Assert.True(profile.IsActive);
        Assert.Null(profile.DisabledAt);
        Assert.Null(profile.LastLoginAt);
    }

    [Fact]
    public void Deactivate_Sets_IsActive_False_DisabledAt_And_UpdatedAt()
    {
        var profile = NewProfile();

        var later = Now.AddDays(1);
        profile.Deactivate(later);

        Assert.False(profile.IsActive);
        Assert.Equal(later, profile.DisabledAt);
        Assert.Equal(later, profile.UpdatedAt);
    }

    [Fact]
    public void Reactivate_Sets_IsActive_True_Clears_DisabledAt_And_Updates_UpdatedAt()
    {
        var profile = NewProfile();
        profile.Deactivate(Now);

        var later = Now.AddDays(1);
        profile.Reactivate(later);

        Assert.True(profile.IsActive);
        Assert.Null(profile.DisabledAt);
        Assert.Equal(later, profile.UpdatedAt);
    }

    [Fact]
    public void RecordLogin_Sets_LastLoginAt()
    {
        var profile = NewProfile();

        var loginTime = Now.AddHours(2);
        profile.RecordLogin(loginTime);

        Assert.Equal(loginTime, profile.LastLoginAt);
    }
}
