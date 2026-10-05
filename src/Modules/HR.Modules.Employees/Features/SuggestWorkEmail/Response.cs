namespace HR.Modules.Employees.Features.SuggestWorkEmail;

internal enum WorkEmailSuggestionStatus
{
    Disabled,
    NotConfigured,
    NameIncomplete,
    Available,
    Unavailable,
}

internal sealed record SuggestWorkEmailResponse(
    WorkEmailSuggestionStatus Status,
    string? Suggestion,
    string? SelectedDomain,
    IReadOnlyList<string> Domains);
