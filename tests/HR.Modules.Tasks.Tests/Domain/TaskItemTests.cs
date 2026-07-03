using HR.Modules.Tasks.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Domain;

public class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    private static TaskItem CreateOpen(
        string title = "Test task",
        TaskPriority priority = TaskPriority.Medium,
        TaskSource source = TaskSource.Manual,
        DateOnly? dueDate = null,
        Guid? assignedEmployeeId = null,
        Guid? assignedUserId = null) =>
        TaskItem.Create(Guid.NewGuid(), CompanyId, CreatedBy, title, null, priority, source, TaskActionType.Complete, dueDate, assignedEmployeeId, assignedUserId, Now);

    [Fact]
    public void Create_Sets_Status_To_Open()
    {
        var task = CreateOpen();

        Assert.Equal(TaskItemStatus.Open, task.Status);
    }

    [Fact]
    public void Create_Sets_All_Properties()
    {
        var id = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var assignedUser = Guid.NewGuid();
        var due = new DateOnly(2026, 6, 30);

        var task = TaskItem.Create(id, CompanyId, CreatedBy, "My Task", "Details", TaskPriority.High, TaskSource.Onboarding, TaskActionType.Complete, due, assignedEmployee, assignedUser, Now);

        Assert.Equal(id, task.Id);
        Assert.Equal(CompanyId, task.CompanyId);
        Assert.Equal(CreatedBy, task.CreatedBy);
        Assert.Equal("My Task", task.Title);
        Assert.Equal("Details", task.Description);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(TaskSource.Onboarding, task.Source);
        Assert.Equal(due, task.DueDate);
        Assert.Equal(assignedEmployee, task.AssignedEmployeeId);
        Assert.Equal(assignedUser, task.AssignedUserId);
        Assert.Null(task.CompletedBy);
        Assert.Null(task.CompletedAt);
        Assert.Equal(Now, task.CreatedAt);
        Assert.Equal(Now, task.UpdatedAt);
    }

    [Fact]
    public void Start_Transitions_Open_To_InProgress()
    {
        var task = CreateOpen();
        var later = Now.AddMinutes(5);

        task.Start(later);

        Assert.Equal(TaskItemStatus.InProgress, task.Status);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Start_Throws_When_Not_Open()
    {
        var task = CreateOpen();
        task.Start(Now);

        Assert.Throws<InvalidOperationException>(() => task.Start(Now.AddMinutes(1)));
    }

    [Fact]
    public void Complete_From_Open_Sets_Status_CompletedBy_And_CompletedAt()
    {
        var task = CreateOpen();
        var completer = Guid.NewGuid();
        var later = Now.AddHours(1);

        task.Complete(completer, later);

        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Equal(completer, task.CompletedBy);
        Assert.Equal(later, task.CompletedAt);
    }

    [Fact]
    public void Complete_From_InProgress_Sets_Status_And_CompletedBy()
    {
        var task = CreateOpen();
        task.Start(Now);
        var completer = Guid.NewGuid();
        var later = Now.AddHours(2);

        task.Complete(completer, later);

        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Equal(completer, task.CompletedBy);
        Assert.Equal(later, task.CompletedAt);
    }

    [Fact]
    public void Complete_Is_Idempotent_When_Already_Completed()
    {
        var task = CreateOpen();
        var completer = Guid.NewGuid();
        task.Complete(completer, Now);
        var completedAt = task.CompletedAt;

        task.Complete(Guid.NewGuid(), Now.AddMinutes(5));

        Assert.Equal(completedAt, task.CompletedAt);
        Assert.Equal(completer, task.CompletedBy);
    }

    [Fact]
    public void Complete_Throws_When_Cancelled()
    {
        var task = CreateOpen();
        task.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => task.Complete(Guid.NewGuid(), Now.AddMinutes(1)));
    }

    [Fact]
    public void Cancel_Transitions_Open_To_Cancelled()
    {
        var task = CreateOpen();
        var later = Now.AddMinutes(10);

        task.Cancel(later);

        Assert.Equal(TaskItemStatus.Cancelled, task.Status);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Cancel_Is_Idempotent_When_Already_Cancelled()
    {
        var task = CreateOpen();
        task.Cancel(Now);

        task.Cancel(Now.AddMinutes(5));

        Assert.Equal(TaskItemStatus.Cancelled, task.Status);
    }

    [Fact]
    public void Cancel_Throws_When_Completed()
    {
        var task = CreateOpen();
        task.Complete(Guid.NewGuid(), Now);

        Assert.Throws<InvalidOperationException>(() => task.Cancel(Now.AddMinutes(1)));
    }

    [Fact]
    public void Reassign_Updates_Both_AssignedEmployeeId_And_AssignedUserId()
    {
        var task = CreateOpen();
        var newEmployee = Guid.NewGuid();
        var newUser = Guid.NewGuid();
        var later = Now.AddMinutes(3);

        task.Reassign(newEmployee, newUser, later);

        Assert.Equal(newEmployee, task.AssignedEmployeeId);
        Assert.Equal(newUser, task.AssignedUserId);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Reassign_To_Nulls_Unassigns_Task()
    {
        var task = CreateOpen(assignedEmployeeId: Guid.NewGuid(), assignedUserId: Guid.NewGuid());

        task.Reassign(null, null, Now);

        Assert.Null(task.AssignedEmployeeId);
        Assert.Null(task.AssignedUserId);
    }

    [Fact]
    public void UpdateDetails_Overwrites_Properties()
    {
        var task = CreateOpen("Original", TaskPriority.Low);
        var later = Now.AddMinutes(5);

        task.UpdateDetails("Updated", "New description", TaskPriority.Critical, new DateOnly(2026, 12, 31), later);

        Assert.Equal("Updated", task.Title);
        Assert.Equal("New description", task.Description);
        Assert.Equal(TaskPriority.Critical, task.Priority);
        Assert.Equal(new DateOnly(2026, 12, 31), task.DueDate);
        Assert.Equal(later, task.UpdatedAt);
    }
}
