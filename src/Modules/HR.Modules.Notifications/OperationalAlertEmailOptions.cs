namespace HR.Modules.Notifications;

/// <summary>
/// Follow-up C: configuration for the internal operations notification sent when a new missing-file
/// organisation-data-export alert opens. Bound from the "OperationalAlerts" configuration section.
/// </summary>
internal sealed class OperationalAlertEmailOptions
{
    /// <summary>
    /// Internal operations mailbox that receives a one-off notification when a new missing-file
    /// export alert opens. When empty, no email is sent (the alert is still recorded).
    /// </summary>
    public string? InternalRecipientEmail { get; set; }

    /// <summary>
    /// Base URL of the internal admin app (HR.Admin.Web), used to build the deep link to the
    /// Operational Alerts page for the alert. When empty, the email omits the link.
    /// </summary>
    public string? AdminAppBaseUrl { get; set; }
}
