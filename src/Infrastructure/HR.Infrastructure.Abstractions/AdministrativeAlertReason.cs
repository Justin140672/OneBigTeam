namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Follow-up F: an explicit, persisted discriminator that identifies the specific underlying failure
/// behind an administrative alert, independent of the broad <see cref="AdministrativeAlertCategory"/>.
/// Used to decide whether a new alert warrants an internal-operations notification email — only
/// <see cref="MissingDocumentExport"/> does. Never derived from string-matching the summary/detail.
/// </summary>
public enum AdministrativeAlertReason
{
    /// <summary>
    /// An organisation data export failed because one or more expected documents were missing from
    /// storage. This is the only reason that queues an operations notification email.
    /// </summary>
    MissingDocumentExport = 1,
}
