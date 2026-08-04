using HR.Modules.CompanyOnboarding.Domain;

namespace HR.Modules.CompanyOnboarding.Tests;

public class CompanyOnboardingTaskCompletionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Defaults()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var completion = CompanyOnboardingTaskCompletion.Create(id, companyId, "complete-company-details", Now);

        Assert.Equal(id, completion.Id);
        Assert.Equal(companyId, completion.CompanyId);
        Assert.Equal("complete-company-details", completion.TaskKey);
        Assert.False(completion.IsCompleted);
        Assert.Null(completion.CompletedAt);
        Assert.Equal(Now, completion.UpdatedAt);
    }

    [Fact]
    public void SetStatus_True_Sets_CompletedAt()
    {
        var completion = CompanyOnboardingTaskCompletion.Create(Guid.NewGuid(), Guid.NewGuid(), "task-key", Now);
        var completedAt = Now.AddDays(1);

        completion.SetStatus(true, completedAt);

        Assert.True(completion.IsCompleted);
        Assert.Equal(completedAt, completion.CompletedAt);
        Assert.Equal(completedAt, completion.UpdatedAt);
    }

    [Fact]
    public void SetStatus_False_Clears_CompletedAt()
    {
        var completion = CompanyOnboardingTaskCompletion.Create(Guid.NewGuid(), Guid.NewGuid(), "task-key", Now);
        completion.SetStatus(true, Now.AddDays(1));

        completion.SetStatus(false, Now.AddDays(2));

        Assert.False(completion.IsCompleted);
        Assert.Null(completion.CompletedAt);
    }

    [Fact]
    public void SetStatus_True_Twice_Is_Idempotent_Keeps_First_CompletedAt()
    {
        var completion = CompanyOnboardingTaskCompletion.Create(Guid.NewGuid(), Guid.NewGuid(), "task-key", Now);
        var firstCompletedAt = Now.AddDays(1);
        var secondCompletedAt = Now.AddDays(2);

        completion.SetStatus(true, firstCompletedAt);
        completion.SetStatus(true, secondCompletedAt);

        Assert.True(completion.IsCompleted);
        Assert.Equal(firstCompletedAt, completion.CompletedAt);
        Assert.Equal(secondCompletedAt, completion.UpdatedAt);
    }
}
