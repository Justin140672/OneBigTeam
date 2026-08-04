using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests;

public class ApplicationUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reactivate_Sets_IsActive_True_And_Updates_UpdatedAt()
    {
        var user = ApplicationUser.Create(Guid.NewGuid(), "user@test.com", "hash", "Test", "User", Now);
        user.Deactivate(Now);
        Assert.False(user.IsActive);

        var later = Now.AddDays(1);
        user.Reactivate(later);

        Assert.True(user.IsActive);
        Assert.Equal(later, user.UpdatedAt);
    }

    [Fact]
    public void RecordLogin_Sets_LastLoginAt()
    {
        var user = ApplicationUser.Create(Guid.NewGuid(), "user@test.com", "hash", "Test", "User", Now);
        Assert.Null(user.LastLoginAt);

        var loginTime = Now.AddHours(2);
        user.RecordLogin(loginTime);

        Assert.Equal(loginTime, user.LastLoginAt);
    }

    [Fact]
    public void Deactivate_Sets_IsActive_False_And_Updates_UpdatedAt()
    {
        var user = ApplicationUser.Create(Guid.NewGuid(), "user@test.com", "hash", "Test", "User", Now);

        var later = Now.AddDays(1);
        user.Deactivate(later);

        Assert.False(user.IsActive);
        Assert.Equal(later, user.UpdatedAt);
    }

    [Fact]
    public void Create_Defaults_IsEmailConfirmed_True_When_Not_Specified()
    {
        var user = ApplicationUser.Create(Guid.NewGuid(), "user@test.com", "hash", "Test", "User", Now);

        Assert.True(user.IsEmailConfirmed);
    }

    [Fact]
    public void Create_Allows_Creating_An_Unconfirmed_User()
    {
        var user = ApplicationUser.Create(
            Guid.NewGuid(), "user@test.com", "hash", "Test", "User", Now, isEmailConfirmed: false);

        Assert.False(user.IsEmailConfirmed);
    }

    [Fact]
    public void ConfirmEmail_Sets_IsEmailConfirmed_True_And_Updates_UpdatedAt()
    {
        var user = ApplicationUser.Create(
            Guid.NewGuid(), "user@test.com", "hash", "Test", "User", Now, isEmailConfirmed: false);

        var later = Now.AddDays(1);
        user.ConfirmEmail(later);

        Assert.True(user.IsEmailConfirmed);
        Assert.Equal(later, user.UpdatedAt);
    }
}
