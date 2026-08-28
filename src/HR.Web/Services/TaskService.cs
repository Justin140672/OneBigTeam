using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Web.Models;

namespace HR.Web.Services;

public sealed class TaskService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<TaskListResponse?> GetMyTasksAsync(Guid companyId, int pageNumber = 1, int pageSize = 20, string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var qs = $"pageNumber={pageNumber}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(status)) qs += $"&status={Uri.EscapeDataString(status)}";
            return await Http.GetFromJsonAsync<TaskListResponse>(
                $"api/companies/{companyId}/tasks/my?{qs}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TaskListResponse?> GetEmployeeTasksAsync(Guid companyId, Guid employeeId, int pageNumber = 1, int pageSize = 20, string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var qs = $"pageNumber={pageNumber}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(status)) qs += $"&status={Uri.EscapeDataString(status)}";
            return await Http.GetFromJsonAsync<TaskListResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/tasks?{qs}", HrApiJsonOptions.Default, cancellationToken);
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
                $"api/companies/{companyId}/tasks/{taskId}", HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TaskListResponse?> GetTeamTasksAsync(Guid companyId, Guid managerId, int pageNumber = 1, int pageSize = 20, string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var qs = $"pageNumber={pageNumber}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(status)) qs += $"&status={Uri.EscapeDataString(status)}";
            return await Http.GetFromJsonAsync<TaskListResponse>(
                $"api/companies/{companyId}/employees/{managerId}/team-tasks?{qs}", HrApiJsonOptions.Default, cancellationToken);
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
                $"api/companies/{companyId}/tasks/unassigned", HrApiJsonOptions.Default, cancellationToken);
        }
        catch { return null; }
    }

    public async Task<GetOutstandingTaskCountResponse?> GetOutstandingTaskCountAsync(
        Guid companyId, TaskSource? source = null, TaskActionType? actionType = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new List<string>();
            if (source is not null) query.Add($"source={source}");
            if (actionType is not null) query.Add($"actionType={actionType}");
            var url = $"api/companies/{companyId}/tasks/outstanding-count";
            if (query.Count > 0) url += "?" + string.Join("&", query);

            return await Http.GetFromJsonAsync<GetOutstandingTaskCountResponse>(url, HrApiJsonOptions.Default, cancellationToken);
        }
        catch
        {
            return null;
        }
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
