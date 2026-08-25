using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Features.NotifyOnCandidateHired;

/// <summary>
/// NOT-07: raises a CandidateHired notification when a candidate is hired and provisioned as an
/// employee. Before this handler, CandidateHiredIntegrationEvent was only consumed by
/// HR.Modules.Tasks (NotifyHrOfCandidateHiredHandler, which creates an unassigned HR-inbox task) —
/// CandidateHired has had a registered NotificationTemplateCatalogue entry since NOT-03 with no live
/// call site. This wires the first one.
///
/// Recipient rule: the newly hired employee's manager, resolved via IManagerReader against the
/// employee id the event already carries (by the time this handler runs, HireCandidateHandler has
/// already saved the provisioned Employee row with its ManagerId, so the read is safe). The hiring
/// manager is the most relevant recipient — they requested/approved the hire and need to know
/// provisioning completed. There is deliberately no HR-administrator fallback here (unlike
/// NotifyOnEmployeeCreatedHandler): Tasks' NotifyHrOfCandidateHiredHandler already guarantees HR
/// visibility via the unassigned HR-inbox task for every hire regardless of manager, so a second
/// "notify HR" path here would be redundant. If the new employee has no manager, this is logged as
/// a warning and no notification is written (missing recipient information is handled and
/// observable, per NOT-07's acceptance criteria).
///
/// CandidateName/VacancyTitle tokens: the CandidateHired template (NOT-03) was authored for
/// candidate-facing wording, but there is no Recruitment.Contracts project exposing candidate/vacancy
/// display names to other modules (Recruitment has no Contracts assembly at all yet — see NOT-07
/// investigation), and adding one is out of scope for this ticket's conservative, non-rewrite goal.
/// The newly hired employee's own name (available via Employees.Contracts, which Notifications
/// already depends on) is used as CandidateName instead — by hire time the candidate and the
/// employee are the same person, so this is an accurate substitute without a new cross-module
/// surface. VacancyTitle is an optional template token and is simply omitted.
///
/// SourceEntityId is CandidateId (not ApplicationId) so the action URL NotificationActionRouteBuilder
/// (NOT-04) computes for CandidateHired ("/companies/{companyId}/candidates/{sourceEntityId}")
/// resolves correctly — that builder was authored expecting the candidate id.
///
/// Idempotent: CandidateHiredIntegrationEvent delivery may repeat, so this checks
/// INotificationWriter.ExistsAsync keyed on (manager, candidateId, CandidateHired) before writing.
/// </summary>
internal sealed class NotifyOnCandidateHiredHandler(
    INotificationWriter notificationWriter,
    IManagerReader managerReader,
    IEmployeeNameReader employeeNameReader,
    ILogger<NotifyOnCandidateHiredHandler> logger)
    : IIntegrationEventHandler<CandidateHiredIntegrationEvent>
{
    public async Task HandleAsync(CandidateHiredIntegrationEvent e, CancellationToken cancellationToken)
    {
        var managerId = await managerReader.GetManagerIdAsync(e.CompanyId, e.EmployeeId, cancellationToken);

        if (managerId is null)
        {
            logger.LogWarning(
                "Skipping CandidateHired notification for candidate {CandidateId}: newly hired employee {EmployeeId} in company {CompanyId} has no manager to notify.",
                e.CandidateId, e.EmployeeId, e.CompanyId);
            return;
        }

        var alreadySent = await notificationWriter.ExistsAsync(
            managerId.Value, e.CandidateId, NotificationType.CandidateHired, cancellationToken);
        if (alreadySent)
            return;

        var names = await employeeNameReader.GetNamesAsync(e.CompanyId, [e.EmployeeId], cancellationToken);
        var candidateName = names.GetValueOrDefault(e.EmployeeId, "A new hire");

        var writeResult = await notificationWriter.WriteTemplatedAsync(
            Guid.NewGuid(), e.CompanyId, managerId.Value,
            NotificationType.CandidateHired,
            new Dictionary<string, string> { ["CandidateName"] = candidateName },
            e.CandidateId,
            NotificationPriority.Normal,
            e.OccurredAt,
            cancellationToken);

        if (writeResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to write CandidateHired notification for candidate {CandidateId}: {Error}",
                e.CandidateId, writeResult.Error.Message);
        }
    }
}
