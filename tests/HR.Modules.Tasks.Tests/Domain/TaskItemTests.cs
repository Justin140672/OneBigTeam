using HR.Modules.Tasks.Domain;

namespace HR.Modules.Tasks.Tests.Domain;

public class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    private static TaskItem CreateOpen(
        string title = "Test task",
        TaskPriority priority = TaskPriority.Medium,
        DateOnly? dueDate = null,
        Guid? assignedTo = null) =>
        TaskItem.Create(Guid.NewGuid(), CompanyId, CreatedBy, title, null, priority, dueDate, assignedTo, Now);

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
        var assignedTo = Guid.NewGuid();
        var due = new DateOnly(2026, 6, 30);

        var task = TaskItem.Create(id, CompanyId, CreatedBy, "My Task", "Details", TaskPriority.High, due, assignedTo, Now);

        Assert.Equal(id, task.Id);
        Assert.Equal(CompanyId, task.CompanyId);
        Assert.Equal(CreatedBy, task.CreatedByEmployeeId);
        Assert.Equal("My Task", task.Title);
        Assert.Equal("Details", task.Description);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(due, task.DueDate);
        Assert.Equal(assignedTo, task.AssignedToEmployeeId);
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
    public void Complete_From_Open_Sets_Status_And_CompletedAt()
    {
        var task = CreateOpen();
        var later = Now.AddHours(1);

        task.Complete(later);

        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Equal(later, task.CompletedAt);
    }

    [Fact]
    public void Complete_From_InProgress_Sets_Status_And_CompletedAt()
    {
        var task = CreateOpen();
        task.Start(Now);
        var later = Now.AddHours(2);

        task.Complete(later);

        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Equal(later, task.CompletedAt);
    }

    [Fact]
    public void Complete_Is_Idempotent_When_Already_Completed()
    {
        var task = CreateOpen();
        task.Complete(Now);
        var completedAt = task.CompletedAt;

        task.Complete(Now.AddMinutes(5));

        Assert.Equal(completedAt, task.CompletedAt);
    }

    [Fact]
    public void Complete_Throws_When_Cancelled()
    {
        var task = CreateOpen();
        task.Cancel(Now);

        Assert.Throws<InvalidOperationException>(() => task.Complete(Now.AddMinutes(1)));
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
        task.Complete(Now);

        Assert.Throws<InvalidOperationException>(() => task.Cancel(Now.AddMinutes(1)));
    }

    [Fact]
    public void Reassign_Updates_AssignedToEmployeeId()
    {
        var task = CreateOpen();
        var newEmployee = Guid.NewGuid();
        var later = Now.AddMinutes(3);

        task.Reassign(newEmployee, later);

        Assert.Equal(newEmployee, task.AssignedToEmployeeId);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Reassign_To_Null_Unassigns_Task()
    {
        var task = CreateOpen(assignedTo: Guid.NewGuid());

        task.Reassign(null, Now);

        Assert.Null(task.AssignedToEmployeeId);
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
