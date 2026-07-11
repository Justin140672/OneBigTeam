using HR.Modules.Offboarding.Domain;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingTaskTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Initial_State_To_Pending()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 7, 1);

        var task = OffboardingTask.Create(
            id, companyId, planId, "Return laptop", "Return company laptop before last day.",
            OffboardingTaskAssignTo.Employee, dueDate, FixedNow);

        Assert.Equal(id, task.Id);
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(planId, task.OffboardingPlanId);
        Assert.Equal("Return laptop", task.Title);
        Assert.Equal("Return company laptop before last day.", task.Description);
        Assert.Equal(OffboardingTaskAssignTo.Employee, task.AssignTo);
        Assert.Equal(dueDate, task.DueDate);
        Assert.Equal(OffboardingTaskStatus.Pending, task.Status);
        Assert.Null(task.CompletedAt);
        Assert.Equal(FixedNow, task.CreatedAt);
        Assert.Equal(FixedNow, task.UpdatedAt);
    }

    [Fact]
    public void Create_Allows_Null_Description_And_Null_DueDate()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Conduct exit interview", null,
            OffboardingTaskAssignTo.Manager, null, FixedNow);

        Assert.Null(task.Description);
        Assert.Null(task.DueDate);
    }

    [Fact]
    public void Complete_Sets_CompletedAt_Status_And_UpdatedAt()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);

        task.Complete(later);

        Assert.Equal(OffboardingTaskStatus.Completed, task.Status);
        Assert.Equal(later, task.CompletedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Skip_Sets_Status_And_UpdatedAt_But_Leaves_CompletedAt_Null()
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Return laptop", null,
            OffboardingTaskAssignTo.Employee, null, FixedNow);
        var later = FixedNow.AddDays(1);

        task.Skip(later);

        Assert.Equal(OffboardingTaskStatus.Skipped, task.Status);
        Assert.Null(task.CompletedAt);
        Assert.Equal(later, task.UpdatedAt);
    }
}
