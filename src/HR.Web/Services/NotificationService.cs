using HR.Web.Models;

namespace HR.Web.Services;

public sealed class NotificationService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<NotificationsResponse?> GetAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<NotificationsResponse>(
                $"api/companies/{companyId}/employees/{employeeId}/notifications",
                cancellationToken);
        }
        catch { return null; }
    }

    public async Task MarkReadAsync(
        Guid companyId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/notifications/{notificationId}/read",
                new { companyId, notificationId }, cancellationToken);
        }
        catch { }
    }

    public async Task MarkAllReadAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            await Http.PutAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/notifications/read-all",
                new { companyId, employeeId }, cancellationToken);
        }
        catch { }
    }
}
