using HR.SharedKernel.Http;
using HR.Web.Models;

namespace HR.Web.Services;

/// <summary>
/// Notification API client. Every method returns an <see cref="ApiResult{T}"/> so callers can tell an
/// empty inbox apart from an auth failure, server failure or network failure; mutations never appear
/// successful when the API call failed. Caller cancellation propagates as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class NotificationService(
    HrApiHttpClientFactory httpClientFactory,
    ILogger<NotificationService>? logger = null)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    public async Task<ApiResult<NotificationsResponse>> GetAsync(
        Guid companyId, int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<NotificationsResponse>(
            ct => Http.GetAsync(
                $"api/companies/{companyId}/notifications/my?pageNumber={pageNumber}&pageSize={pageSize}", ct),
            HrApiJsonOptions.Default, cancellationToken);
        return result.LogFailure(logger, "Notifications.List");
    }

    public async Task<ApiResult<int>> GetUnreadCountAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteAsync<UnreadCountResponse>(
            ct => Http.GetAsync($"api/companies/{companyId}/notifications/unread-count", ct),
            HrApiJsonOptions.Default, cancellationToken);

        // A 200 with an empty/null body is a malformed response, not "zero unread".
        var mapped = result.Success && result.Value is null
            ? ApiResult<int>.Fail(ApiFailureKind.InvalidResponse, "The server returned an unreadable response.")
            : result.Map(v => v?.Count ?? 0);
        return mapped.LogFailure(logger, "Notifications.UnreadCount");
    }

    private sealed record UnreadCountResponse(int Count);

    public async Task<ApiResult<Unit>> MarkReadAsync(
        Guid companyId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.PutAsJsonAsync(
                $"api/companies/{companyId}/notifications/{notificationId}/read",
                new { companyId, notificationId }, ct),
            cancellationToken);
        return result.LogFailure(logger, "Notifications.MarkRead");
    }

    public async Task<ApiResult<Unit>> MarkAllReadAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var result = await ApiResponseReader.ExecuteNoContentAsync(
            ct => Http.PutAsJsonAsync(
                $"api/companies/{companyId}/employees/{employeeId}/notifications/read-all",
                new { companyId, employeeId }, ct),
            cancellationToken);
        return result.LogFailure(logger, "Notifications.MarkAllRead");
    }
}
