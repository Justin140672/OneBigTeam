using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.RequestPersonalDetailsChange;
using HR.Modules.Employees.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

public class RequestPersonalDetailsChangeHandlerTests
{
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private sealed class FakeTaskCreator : ITaskCreator
    {
        public Guid? LastAssignedEmployeeId { get; private set; }
        public Guid? LastSourceEntityId { get; private set; }
        public string? LastTitle { get; private set; }
        public string? LastDescription { get; private set; }
        public int CallCount { get; private set; }

        public Task<Guid> CreateAsync(
            Guid companyId, Guid createdBy, string title, string? description,
            TaskPriority priority, TaskSource source, TaskActionType actionType,
            DateOnly? dueDate, Guid? assignedEmployeeId, Guid? assignedUserId,
            Guid? sourceEntityId, CancellationToken cancellationToken,
            bool notifyAssignee = true)
        {
            CallCount++;
            LastAssignedEmployeeId = assignedEmployeeId;
            LastSourceEntityId = sourceEntityId;
            LastTitle = title;
            LastDescription = description;
            return Task.FromResult(Guid.NewGuid());
        }
    }

    [Fact]
    public async Task HandleAsync_Creates_Task_And_Returns_TaskId_When_Requesting_Own_Change()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var handler = new RequestPersonalDetailsChangeHandler(context, taskCreator);

        var result = await handler.HandleAsync(
            new RequestPersonalDetailsChangeRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                Notes = "Please update my nationality to British."
            },
            employee.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.TaskId);
        Assert.Equal(1, taskCreator.CallCount);
        Assert.Equal(employee.Id, taskCreator.LastSourceEntityId);
        Assert.Contains("Alice Smith", taskCreator.LastTitle);
        Assert.Equal("Please update my nationality to British.", taskCreator.LastDescription);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var handler = new RequestPersonalDetailsChangeHandler(context, taskCreator);

        var employeeId = Guid.NewGuid();
        var result = await handler.HandleAsync(
            new RequestPersonalDetailsChangeRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = employeeId,
                Notes = "Some notes"
            },
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal(0, taskCreator.CallCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var now = DateTimeOffset.UtcNow;
        var employee = Employee.Create(Guid.NewGuid(), Guid.NewGuid(), "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var handler = new RequestPersonalDetailsChangeHandler(context, taskCreator);

        var result = await handler.HandleAsync(
            new RequestPersonalDetailsChangeRequest
            {
                CompanyId = Guid.NewGuid(), // different company
                EmployeeId = employee.Id,
                Notes = "Some notes"
            },
            employee.Id,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_When_Requesting_User_Is_Not_The_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@example.com", StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say", "EMP-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var handler = new RequestPersonalDetailsChangeHandler(context, taskCreator);

        var someoneElseUserId = Guid.NewGuid();
        var result = await handler.HandleAsync(
            new RequestPersonalDetailsChangeRequest
            {
                CompanyId = companyId,
                EmployeeId = employee.Id,
                Notes = "Some notes"
            },
            someoneElseUserId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(0, taskCreator.CallCount);
    }

    private static EmployeesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EmployeesDbContext(options);
    }
}
