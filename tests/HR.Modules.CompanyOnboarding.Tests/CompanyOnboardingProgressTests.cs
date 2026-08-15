using HR.Modules.CompanyOnboarding.Domain;

namespace HR.Modules.CompanyOnboarding.Tests;

public class CompanyOnboardingProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Defaults()
    {
        var companyId = Guid.NewGuid();

        var progress = CompanyOnboardingProgress.Create(companyId, Now);

        Assert.Equal(companyId, progress.CompanyId);
        Assert.False(progress.IsDismissedEarly);
        Assert.False(progress.IsHidden);
        Assert.Null(progress.CompletedAt);
        Assert.Equal(Now, progress.CreatedAt);
        Assert.Equal(Now, progress.UpdatedAt);
    }

    [Fact]
    public void MarkDismissed_Sets_Both_Flags()
    {
        var progress = CompanyOnboardingProgress.Create(Guid.NewGuid(), Now);
        var dismissedAt = Now.AddDays(1);

        progress.MarkDismissed(dismissedAt);

        Assert.True(progress.IsDismissedEarly);
        Assert.True(progress.IsHidden);
        Assert.Equal(dismissedAt, progress.UpdatedAt);
    }

    [Fact]
    public void MarkCompleted_Sets_CompletedAt_And_IsHidden()
    {
        var progress = CompanyOnboardingProgress.Create(Guid.NewGuid(), Now);
        var completedAt = Now.AddDays(1);

        progress.MarkCompleted(completedAt);

        Assert.Equal(completedAt, progress.CompletedAt);
        Assert.True(progress.IsHidden);
        Assert.Equal(completedAt, progress.UpdatedAt);
    }

    [Fact]
    public void MarkCompleted_Is_Idempotent_Keeps_First_CompletedAt()
    {
        var progress = CompanyOnboardingProgress.Create(Guid.NewGuid(), Now);
        var firstCompletedAt = Now.AddDays(1);
        var secondCompletedAt = Now.AddDays(2);

        progress.MarkCompleted(firstCompletedAt);
        progress.MarkCompleted(secondCompletedAt);

        Assert.Equal(firstCompletedAt, progress.CompletedAt);
        // UpdatedAt still bumps on the second call even though CompletedAt is unchanged.
        Assert.Equal(secondCompletedAt, progress.UpdatedAt);
    }

    [Fact]
    public void MarkDismissed_Twice_Keeps_Flags_But_Bumps_UpdatedAt_Each_Call()
    {
        var progress = CompanyOnboardingProgress.Create(Guid.NewGuid(), Now);
        var firstDismissedAt = Now.AddDays(1);
        var secondDismissedAt = Now.AddDays(2);

        progress.MarkDismissed(firstDismissedAt);
        progress.MarkDismissed(secondDismissedAt);

        Assert.True(progress.IsDismissedEarly);
        Assert.True(progress.IsHidden);
        Assert.Equal(secondDismissedAt, progress.UpdatedAt);
    }

    [Fact]
    public void MarkDismissed_Does_Not_Set_CompletedAt()
    {
        // MarkDismissed and MarkCompleted both flip IsHidden, but only completion should
        // populate CompletedAt - dismissal must leave it null.
        var progress = CompanyOnboardingProgress.Create(Guid.NewGuid(), Now);

        progress.MarkDismissed(Now.AddDays(1));

        Assert.True(progress.IsHidden);
        Assert.Null(progress.CompletedAt);
    }
}
