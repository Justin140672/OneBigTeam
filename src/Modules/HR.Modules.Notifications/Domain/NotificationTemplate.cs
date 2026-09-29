namespace HR.Modules.Notifications.Domain;

internal sealed record NotificationTemplate(
    int Version,
    string InAppTitleTemplate,
    string? InAppBodyTemplate,
    string EmailSubjectTemplate,
    string EmailBodyTemplate,
    IReadOnlySet<string> RequiredTokens,
    IReadOnlySet<string> OptionalTokens);
