using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Domain;

/// <summary>
/// NOT-03: static, source-controlled registry of NotificationTemplate keyed by NotificationType.
/// Only the six types explicitly required by NOT-03 are registered here — every other
/// NotificationType continues to be raised via INotificationWriter.WriteAsync with a pre-formatted
/// string, unchanged.
///
/// Wording migration note: LeaveApproved and TaskAssigned are the only two of the six types that
/// currently have a live call site (LeaveApprovalEffectsService.PublishApprovalOutcomeAsync and
/// TaskCreator.CreateAsync respectively). Their templates below reproduce the exact strings those
/// call sites previously built inline, parameterised with tokens, so switching those call sites to
/// WriteTemplatedAsync produces byte-for-byte identical in-app title/body text. LeaveRequested and
/// DocumentExpiring have declared NotificationType values but no call site currently raises a
/// Notification of that type (LeaveRequested only drives an integration event that the Tasks module
/// separately turns into a task; DocumentExpiring reminders are similarly delivered as tasks via
/// ProcessDocumentExpiryNotificationsHandler) — their wording below mirrors the closest existing
/// task/notification copy for consistency, but there is no prior notification text to preserve.
/// EmployeeCreated and CandidateHired are new NotificationType values added by NOT-03: no module
/// currently raises a notification for either event, so no existing wording exists to migrate.
/// Wiring a real caller to any of these last four templates is left for a future ticket.
/// </summary>
internal static class NotificationTemplateCatalogue
{
    private const string EmailBodyStyle = "font-family:sans-serif;max-width:600px;margin:auto;padding:24px";

    private static readonly IReadOnlyDictionary<NotificationType, NotificationTemplate> Templates =
        new Dictionary<NotificationType, NotificationTemplate>
        {
            [NotificationType.LeaveRequested] = new NotificationTemplate(
                Version: 1,
                InAppTitleTemplate: "New leave request awaiting approval",
                InAppBodyTemplate: "{RequesterName} requested leave from {StartDate} to {EndDate}.",
                EmailSubjectTemplate: "New leave request awaiting approval",
                EmailBodyTemplate: BuildEmailBody(
                    "New leave request awaiting approval",
                    "{RequesterName} requested leave from {StartDate} to {EndDate}."),
                RequiredTokens: new HashSet<string> { "RequesterName", "StartDate", "EndDate" },
                OptionalTokens: new HashSet<string>()),

            [NotificationType.LeaveApproved] = new NotificationTemplate(
                Version: 1,
                InAppTitleTemplate: "Your leave request has been approved",
                InAppBodyTemplate: "Your leave from {StartDate} to {EndDate} has been approved.",
                EmailSubjectTemplate: "Your leave request has been approved",
                EmailBodyTemplate: BuildEmailBody(
                    "Your leave request has been approved",
                    "Your leave from {StartDate} to {EndDate} has been approved."),
                RequiredTokens: new HashSet<string> { "StartDate", "EndDate" },
                OptionalTokens: new HashSet<string>()),

            [NotificationType.EmployeeCreated] = new NotificationTemplate(
                Version: 1,
                InAppTitleTemplate: "New employee added: {EmployeeName}",
                InAppBodyTemplate: "{EmployeeName} has joined as {JobTitle} in {Department}.",
                EmailSubjectTemplate: "New employee added: {EmployeeName}",
                EmailBodyTemplate: BuildEmailBody(
                    "New employee added: {EmployeeName}",
                    "{EmployeeName} has joined as {JobTitle} in {Department}."),
                RequiredTokens: new HashSet<string> { "EmployeeName" },
                OptionalTokens: new HashSet<string> { "JobTitle", "Department" }),

            [NotificationType.CandidateHired] = new NotificationTemplate(
                Version: 1,
                InAppTitleTemplate: "Candidate hired: {CandidateName}",
                InAppBodyTemplate: "{CandidateName} has been hired for {VacancyTitle}.",
                EmailSubjectTemplate: "Candidate hired: {CandidateName}",
                EmailBodyTemplate: BuildEmailBody(
                    "Candidate hired: {CandidateName}",
                    "{CandidateName} has been hired for {VacancyTitle}."),
                RequiredTokens: new HashSet<string> { "CandidateName" },
                OptionalTokens: new HashSet<string> { "VacancyTitle" }),

            [NotificationType.DocumentExpiring] = new NotificationTemplate(
                Version: 1,
                InAppTitleTemplate: "Document expiring soon: {DocumentTitle}",
                InAppBodyTemplate: "'{DocumentTitle}' ({DocumentTypeName}) expires in {DaysUntilExpiry} day(s) on {ExpiryDate}. Please arrange renewal.",
                EmailSubjectTemplate: "Document expiring soon: {DocumentTitle}",
                EmailBodyTemplate: BuildEmailBody(
                    "Document expiring soon: {DocumentTitle}",
                    "'{DocumentTitle}' ({DocumentTypeName}) expires in {DaysUntilExpiry} day(s) on {ExpiryDate}. Please arrange renewal."),
                RequiredTokens: new HashSet<string> { "DocumentTitle", "DocumentTypeName", "DaysUntilExpiry", "ExpiryDate" },
                OptionalTokens: new HashSet<string>()),

            [NotificationType.TaskAssigned] = new NotificationTemplate(
                Version: 1,
                InAppTitleTemplate: "New task assigned: {TaskTitle}",
                InAppBodyTemplate: "{TaskDescription}",
                EmailSubjectTemplate: "New task assigned: {TaskTitle}",
                EmailBodyTemplate: BuildEmailBody(
                    "New task assigned: {TaskTitle}",
                    "{TaskDescription}"),
                RequiredTokens: new HashSet<string> { "TaskTitle" },
                OptionalTokens: new HashSet<string> { "TaskDescription" }),
        };

    /// <summary>Every registered template, exposed only so NotificationTemplateCatalogueTests can walk
    /// the full set and assert every "{Token}" placeholder used in a template string is declared in
    /// that template's RequiredTokens/OptionalTokens — a template-authoring-time consistency check,
    /// not something evaluated per-render.</summary>
    public static IReadOnlyDictionary<NotificationType, NotificationTemplate> All => Templates;

    public static bool TryGet(NotificationType type, out NotificationTemplate? template) =>
        Templates.TryGetValue(type, out template);

    private static string BuildEmailBody(string titleTemplate, string bodyTemplate) => $"""
        <html>
        <body style="{EmailBodyStyle}">
          <h2>{titleTemplate}</h2>
          <p>{bodyTemplate}</p>
        </body>
        </html>
        """;
}
