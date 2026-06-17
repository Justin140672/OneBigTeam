using HR.Web.Models;

namespace HR.Web.Services;

public sealed class TaskService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<TaskListResponse?> GetMyTasksAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<TaskListResponse>(
                $"api/companies/{companyId}/tasks/mine", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TaskListResponse?> GetEmployeeTasksAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<TaskListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/tasks", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TaskDetailModel?> GetTaskAsync(Guid companyId, Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<TaskDetailModel>(
                $"api/companies/{companyId}/tasks/{taskId}", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TaskListResponse?> GetTeamTasksAsync(Guid companyId, Guid managerId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<TaskListResponse>(
                $"api/companies/{companyId}/employees/{managerId}/team-tasks", cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<UnassignedTaskListResponse?> GetUnassignedTasksAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<UnassignedTaskListResponse>(
                $"api/companies/{companyId}/tasks/unassigned", cancellationToken);
        }
        catch { return null; }
    }

    public async Task<bool> SelfAssignTaskAsync(Guid companyId, Guid taskId, Guid employeeId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/tasks/{taskId}/assignee",
                new { AssignedEmployeeId = employeeId, AssignedUserId = userId },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> CompleteTaskAsync(
        Guid companyId, Guid taskId,
        string? outcomeDecision = null, string? outcomeReason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                $"api/companies/{companyId}/tasks/{taskId}/complete",
                new { OutcomeDecision = outcomeDecision, OutcomeReason = outcomeReason },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
