using System.Net.Http.Json;
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

    public async Task<bool> CompleteTaskAsync(Guid companyId, Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsync(
                $"api/companies/{companyId}/tasks/{taskId}/complete",
                content: null,
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
