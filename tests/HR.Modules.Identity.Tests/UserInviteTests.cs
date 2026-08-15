using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests;

public class UserInviteTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cancel_Sets_CancelledAt_And_IsCancelled()
    {
        var invite = UserInvite.Create(Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now);
        Assert.False(invite.IsCancelled);

        invite.Cancel(Now.AddDays(1));

        Assert.True(invite.IsCancelled);
        Assert.Equal(Now.AddDays(1), invite.CancelledAt);
    }

    [Fact]
    public void Resend_Changes_Token_And_Extends_ExpiresAt()
    {
        var invite = UserInvite.Create(Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now);
        var originalToken = invite.Token;
        var originalExpiry = invite.ExpiresAt;

        var later = Now.AddDays(3);
        invite.Resend(later);

        Assert.NotEqual(originalToken, invite.Token);
        Assert.NotEqual(originalExpiry, invite.ExpiresAt);
        Assert.Equal(later.AddDays(7), invite.ExpiresAt);
    }

    [Fact]
    public void Create_With_RoleIds_Populates_PendingRoleIds()
    {
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();

        var invite = UserInvite.Create(
            Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now, roleIds: [roleA, roleB]);

        Assert.Equal(2, invite.PendingRoleIds.Count);
        Assert.Contains(roleA, invite.PendingRoleIds);
        Assert.Contains(roleB, invite.PendingRoleIds);
    }

    [Fact]
    public void Create_With_RoleIds_Deduplicates()
    {
        var roleA = Guid.NewGuid();

        var invite = UserInvite.Create(
            Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now, roleIds: [roleA, roleA]);

        Assert.Single(invite.PendingRoleIds);
    }

    [Fact]
    public void Create_Without_RoleIds_Leaves_PendingRoleIds_Empty()
    {
        var invite = UserInvite.Create(Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now);

        Assert.Empty(invite.PendingRoleIds);
    }

    [Fact]
    public void Create_Sets_CreatedByUserId_When_Provided()
    {
        var actorId = Guid.NewGuid();

        var invite = UserInvite.Create(
            Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now, createdByUserId: actorId);

        Assert.Equal(actorId, invite.CreatedByUserId);
    }

    [Fact]
    public void Create_CreatedByUserId_Defaults_To_Null()
    {
        var invite = UserInvite.Create(Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now);

        Assert.Null(invite.CreatedByUserId);
    }

    [Fact]
    public void IsExpired_Is_False_Immediately_After_Create()
    {
        var invite = UserInvite.Create(
            Guid.NewGuid(), Guid.NewGuid(), "test@example.com", DateTimeOffset.UtcNow);

        Assert.False(invite.IsExpired);
    }

    [Fact]
    public void IsExpired_Is_True_Once_The_7_Day_Window_Has_Elapsed()
    {
        var invite = UserInvite.Create(
            Guid.NewGuid(), Guid.NewGuid(), "test@example.com", DateTimeOffset.UtcNow.AddDays(-8));

        Assert.True(invite.IsExpired);
    }

    [Fact]
    public void IsExpired_Is_False_Just_Before_The_7_Day_Window_Elapses()
    {
        var invite = UserInvite.Create(
            Guid.NewGuid(), Guid.NewGuid(), "test@example.com", DateTimeOffset.UtcNow.AddDays(-6));

        Assert.False(invite.IsExpired);
    }

    [Fact]
    public void Resend_Resets_IsExpired_To_False()
    {
        var invite = UserInvite.Create(
            Guid.NewGuid(), Guid.NewGuid(), "test@example.com", DateTimeOffset.UtcNow.AddDays(-8));
        Assert.True(invite.IsExpired);

        invite.Resend(DateTimeOffset.UtcNow);

        Assert.False(invite.IsExpired);
    }

    [Fact]
    public void IsClaimed_Is_False_Before_Claim_And_True_After()
    {
        var invite = UserInvite.Create(Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now);
        Assert.False(invite.IsClaimed);

        invite.Claim(Now.AddHours(1));

        Assert.True(invite.IsClaimed);
        Assert.Equal(Now.AddHours(1), invite.ClaimedAt);
    }

    [Fact]
    public void IsCancelled_Is_False_When_Never_Cancelled()
    {
        var invite = UserInvite.Create(Guid.NewGuid(), Guid.NewGuid(), "test@example.com", Now);

        Assert.False(invite.IsCancelled);
        Assert.Null(invite.CancelledAt);
    }
}
