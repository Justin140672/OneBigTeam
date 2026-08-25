using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeNotificationWriter : INotificationWriter
{
    public record WrittenNotification(
        Guid Id, Guid CompanyId, Guid EmployeeId,
        string Title, string? Body,
        Guid SourceEntityId, NotificationType Type,
        NotificationPriority Priority, DateTimeOffset CreatedAt);

    public List<WrittenNotification> Written { get; } = [];

    public Task WriteAsync(
        Guid id, Guid companyId, Guid employeeId,
        string title, string? body,
        Guid sourceEntityId, NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        Written.Add(new WrittenNotification(id, companyId, employeeId, title, body, sourceEntityId, type, priority, createdAt));
        return Task.CompletedTask;
    }

    /// <summary>
    /// NOT-03: mirrors NotificationTemplateCatalogue's title/body templates for the six
    /// template-backed types (duplicated here deliberately, same as this fake previously duplicated
    /// hardcoded call-site strings, so unit tests can assert on rendered wording without this test
    /// project referencing the internal Notifications module catalogue).
    /// </summary>
    public Task<Result> WriteTemplatedAsync(
        Guid id, Guid companyId, Guid employeeId, NotificationType type,
        IReadOnlyDictionary<string, string> tokens, Guid sourceEntityId,
        NotificationPriority priority, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var (renderedTitle, renderedBody) = FakeNotificationTemplateRenderer.Render(type, tokens);
        Written.Add(new WrittenNotification(id, companyId, employeeId, renderedTitle, renderedBody, sourceEntityId, type, priority, createdAt));
        return Task.FromResult(Result.Success());
    }

    public Task<bool> ExistsAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Written.Any(n =>
            n.EmployeeId == employeeId &&
            n.SourceEntityId == sourceEntityId &&
            n.Type == type));

    public Task<DateTimeOffset?> GetLastSentAtAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Written
            .Where(n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => (DateTimeOffset?)n.CreatedAt)
            .FirstOrDefault());

    public Task<int> RemoveBySourceEntityAsync(
        Guid companyId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
    {
        var removed = Written.RemoveAll(n =>
            n.CompanyId == companyId && n.SourceEntityId == sourceEntityId && n.Type == type);
        return Task.FromResult(removed);
    }
}

/// <summary>
/// NOT-03: minimal, test-local mirror of NotificationTemplateCatalogue's title/body wording for the
/// six template-backed notification types, used only so fakes in this test project can assert on
/// rendered wording without referencing the internal Notifications module.
/// </summary>
internal static class FakeNotificationTemplateRenderer
{
    public static (string Title, string? Body) Render(NotificationType type, IReadOnlyDictionary<string, string> tokens)
    {
        string Get(string key) => tokens.TryGetValue(key, out var value) ? value : string.Empty;

        return type switch
        {
            NotificationType.TaskAssigned => (
                $"New task assigned: {Get("TaskTitle")}",
                tokens.TryGetValue("TaskDescription", out var description) && !string.IsNullOrWhiteSpace(description) ? description : null),

            NotificationType.LeaveApproved => (
                "Your leave request has been approved",
                $"Your leave from {Get("StartDate")} to {Get("EndDate")} has been approved."),

            NotificationType.LeaveRequested => (
                "New leave request awaiting approval",
                $"{Get("RequesterName")} requested leave from {Get("StartDate")} to {Get("EndDate")}."),

            NotificationType.EmployeeCreated => (
                $"New employee added: {Get("EmployeeName")}",
                $"{Get("EmployeeName")} has joined as {Get("JobTitle")} in {Get("Department")}."),

            NotificationType.CandidateHired => (
                $"Candidate hired: {Get("CandidateName")}",
                $"{Get("CandidateName")} has been hired for {Get("VacancyTitle")}."),

            NotificationType.DocumentExpiring => (
                $"Document expiring soon: {Get("DocumentTitle")}",
                $"'{Get("DocumentTitle")}' ({Get("DocumentTypeName")}) expires in {Get("DaysUntilExpiry")} day(s) on {Get("ExpiryDate")}. Please arrange renewal."),

            _ => throw new InvalidOperationException(
                $"FakeNotificationWriter has no template rendering mirror for NotificationType '{type}'."),
        };
    }
}
