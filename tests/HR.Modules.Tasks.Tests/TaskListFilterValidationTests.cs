using HR.Modules.Tasks.Features.GetEmployeeTasks;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Features.GetTeamTasks;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// OBT-REM-05: invalid string status/priority filters on the task-list endpoints must be rejected
/// (422) before the handler/DB is reached, rather than being silently ignored (which previously let
/// <c>status=Compeleted</c> return every task).
/// </summary>
public class TaskListFilterValidationTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();

    private static readonly string[] ValidStatuses =
        ["Open", "InProgress", "Completed", "Cancelled", "inprogress", "COMPLETED", "  Open  "];

    private static readonly string[] ValidPriorities =
        ["Low", "Medium", "High", "Critical", "critical", "HIGH", " Low "];

    private static readonly string?[] BlankValues = [null, "", "   "];

    private static readonly string[] InvalidValues =
        ["Compeleted", "Completedx", "7", "0", "-1", "Open,Closed", "Open Closed", "Closed", "Urgent", "None"];

    // ── GetMyTasks ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void GetMyTasks_status(string? status, bool expectValid)
        => AssertStatus(Validate(new GetMyTasksValidator(), MyRequest(status: status)), expectValid);

    [Theory]
    [MemberData(nameof(PriorityCases))]
    public void GetMyTasks_priority(string? priority, bool expectValid)
        => AssertPriority(Validate(new GetMyTasksValidator(), MyRequest(priority: priority)), expectValid);

    [Fact]
    public void GetMyTasks_valid_status_but_invalid_priority_fails()
    {
        var errors = Validate(new GetMyTasksValidator(), MyRequest(status: "Completed", priority: "Nope"));
        Assert.Contains(errors, e => e.PropertyName == "Priority");
        Assert.DoesNotContain(errors, e => e.PropertyName == "Status");
    }

    [Fact]
    public void GetMyTasks_invalid_status_but_valid_priority_fails()
    {
        var errors = Validate(new GetMyTasksValidator(), MyRequest(status: "Nope", priority: "High"));
        Assert.Contains(errors, e => e.PropertyName == "Status");
        Assert.DoesNotContain(errors, e => e.PropertyName == "Priority");
    }

    // ── GetEmployeeTasks ──────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void GetEmployeeTasks_status(string? status, bool expectValid)
        => AssertStatus(
            Validate(new GetEmployeeTasksValidator(),
                new GetEmployeeTasksRequest { CompanyId = Company, EmployeeId = Subject, Status = status }),
            expectValid);

    [Theory]
    [MemberData(nameof(PriorityCases))]
    public void GetEmployeeTasks_priority(string? priority, bool expectValid)
        => AssertPriority(
            Validate(new GetEmployeeTasksValidator(),
                new GetEmployeeTasksRequest { CompanyId = Company, EmployeeId = Subject, Priority = priority }),
            expectValid);

    // ── GetTeamTasks ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void GetTeamTasks_status(string? status, bool expectValid)
        => AssertStatus(
            Validate(new GetTeamTasksValidator(),
                new GetTeamTasksRequest { CompanyId = Company, ManagerId = Subject, Status = status }),
            expectValid);

    [Theory]
    [MemberData(nameof(PriorityCases))]
    public void GetTeamTasks_priority(string? priority, bool expectValid)
        => AssertPriority(
            Validate(new GetTeamTasksValidator(),
                new GetTeamTasksRequest { CompanyId = Company, ManagerId = Subject, Priority = priority }),
            expectValid);

    // ── data ──────────────────────────────────────────────────────────────

    public static IEnumerable<object?[]> StatusCases()
    {
        foreach (var v in ValidStatuses) yield return [v, true];
        foreach (var v in BlankValues) yield return [v, true];
        foreach (var v in InvalidValues) yield return [v, false];
    }

    public static IEnumerable<object?[]> PriorityCases()
    {
        foreach (var v in ValidPriorities) yield return [v, true];
        foreach (var v in BlankValues) yield return [v, true];
        foreach (var v in InvalidValues) yield return [v, false];
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static GetMyTasksRequest MyRequest(string? status = null, string? priority = null)
        => new() { CompanyId = Company, Status = status, Priority = priority };

    private static List<FluentValidation.Results.ValidationFailure> Validate<T>(
        FluentValidation.IValidator<T> validator, T request)
        => validator.Validate(request).Errors;

    private static void AssertStatus(List<FluentValidation.Results.ValidationFailure> errors, bool expectValid)
        => Assert.Equal(expectValid, errors.All(e => e.PropertyName != "Status"));

    private static void AssertPriority(List<FluentValidation.Results.ValidationFailure> errors, bool expectValid)
        => Assert.Equal(expectValid, errors.All(e => e.PropertyName != "Priority"));
}
