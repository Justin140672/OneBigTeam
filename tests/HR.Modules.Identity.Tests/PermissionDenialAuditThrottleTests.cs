using HR.Modules.Identity.Authorization;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests;

public class PermissionDenialAuditThrottleTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PermissionId = Guid.NewGuid();

    private sealed class MutableFakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    [Fact]
    public void ShouldAudit_Returns_True_And_Not_Escalated_For_The_First_Denial()
    {
        var throttle = new PermissionDenialAuditThrottle(new MutableFakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var shouldAudit = throttle.ShouldAudit(UserId, PermissionId, out var isEscalation, out var count);

        Assert.True(shouldAudit);
        Assert.False(isEscalation);
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ShouldAudit_Suppresses_Denials_Two_Through_Four_In_The_Same_Window(int callNumber)
    {
        var throttle = new PermissionDenialAuditThrottle(new MutableFakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        bool shouldAudit = true;
        bool isEscalation = false;
        int count = 0;
        for (var i = 0; i < callNumber; i++)
            shouldAudit = throttle.ShouldAudit(UserId, PermissionId, out isEscalation, out count);

        Assert.False(shouldAudit);
        Assert.False(isEscalation);
        Assert.Equal(callNumber, count);
    }

    [Fact]
    public void ShouldAudit_Returns_True_And_Escalated_On_The_Fifth_Denial_In_The_Same_Window()
    {
        var throttle = new PermissionDenialAuditThrottle(new MutableFakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        bool shouldAudit = false;
        bool isEscalation = false;
        int count = 0;
        for (var i = 0; i < 5; i++)
            shouldAudit = throttle.ShouldAudit(UserId, PermissionId, out isEscalation, out count);

        Assert.True(shouldAudit);
        Assert.True(isEscalation);
        Assert.Equal(5, count);
    }

    [Fact]
    public void ShouldAudit_Suppresses_Denials_After_The_Escalation_In_The_Same_Window()
    {
        var throttle = new PermissionDenialAuditThrottle(new MutableFakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        for (var i = 0; i < 5; i++)
            throttle.ShouldAudit(UserId, PermissionId, out _, out _);

        var shouldAudit = throttle.ShouldAudit(UserId, PermissionId, out var isEscalation, out var count);

        Assert.False(shouldAudit);
        Assert.False(isEscalation);
        Assert.Equal(6, count);
    }

    [Fact]
    public void ShouldAudit_Resets_And_Treats_The_Next_Denial_As_First_After_The_Window_Elapses()
    {
        var clock = new MutableFakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var throttle = new PermissionDenialAuditThrottle(clock);

        for (var i = 0; i < 6; i++)
            throttle.ShouldAudit(UserId, PermissionId, out _, out _);

        clock.UtcNow = clock.UtcNow.AddMinutes(15).AddSeconds(1);

        var shouldAudit = throttle.ShouldAudit(UserId, PermissionId, out var isEscalation, out var count);

        Assert.True(shouldAudit);
        Assert.False(isEscalation);
        Assert.Equal(1, count);
    }

    [Fact]
    public void ShouldAudit_Does_Not_Reset_Exactly_At_The_Window_Boundary()
    {
        var clock = new MutableFakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var throttle = new PermissionDenialAuditThrottle(clock);

        throttle.ShouldAudit(UserId, PermissionId, out _, out _);

        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        var shouldAudit = throttle.ShouldAudit(UserId, PermissionId, out var isEscalation, out var count);

        Assert.False(shouldAudit);
        Assert.False(isEscalation);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ShouldAudit_Tracks_Independent_Windows_Per_UserId_PermissionId_Pair()
    {
        var throttle = new PermissionDenialAuditThrottle(new MutableFakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var otherUserId = Guid.NewGuid();
        var otherPermissionId = Guid.NewGuid();

        throttle.ShouldAudit(UserId, PermissionId, out _, out _);
        throttle.ShouldAudit(UserId, PermissionId, out var suppressedForFirstPair, out _);
        Assert.False(suppressedForFirstPair);

        var shouldAuditOther = throttle.ShouldAudit(otherUserId, otherPermissionId, out var isEscalationOther, out var countOther);

        Assert.True(shouldAuditOther);
        Assert.False(isEscalationOther);
        Assert.Equal(1, countOther);

        var otherPermissionSameUser = Guid.NewGuid();
        var shouldAuditSameUserOtherPermission = throttle.ShouldAudit(UserId, otherPermissionSameUser, out var isEscalationSameUser, out var countSameUser);
        Assert.True(shouldAuditSameUserOtherPermission);
        Assert.False(isEscalationSameUser);
        Assert.Equal(1, countSameUser);
    }
}
