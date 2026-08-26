using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

/// <summary>
/// DOC-03: evaluates every employee document with an expiry date for the given company and fires
/// whichever of the four notification stages (90/30/7 days before expiry, plus overdue/expired)
/// have newly crossed their threshold as of "today" in the company's own time zone.
///
/// Each stage is tracked by its own persisted "sent at" column on <see cref="EmployeeDocument"/>
/// (ExpiryReminder90/30/7SentAt, plus the pre-existing ExpiringSoonNotifiedAt/ExpiredNotifiedAt for
/// the overdue stage) and is only ever fired once per expiry date — a stage already sent is a safe
/// no-op on every subsequent run, including Hangfire retries and catch-up runs after a missed day.
/// Multiple stages can fire in the same run if the job has not executed for a while (e.g. a
/// document created with an expiry date already inside the 30-day window fires both the 90-day and
/// 30-day stages together the first time it is evaluated).
///
/// Called both by <see cref="DocumentExpiryReminderJob"/> (the automatic daily per-company job —
/// see DOC-03) and by the manual /expiry-notifications endpoint retained for on-demand/admin use.
/// </summary>
internal sealed class ProcessDocumentExpiryNotificationsHandler(
    DocumentsDbContext db,
    IClock clock,
    ICompanyTimeZoneReader timeZoneReader,
    IAuditEventPublisher auditPublisher,
    ITaskCreator taskCreator,
    ICompanyDocumentReminderSettingsReader documentReminderSettingsReader)
{
    public async Task<ProcessDocumentExpiryNotificationsResponse> HandleAsync(
        ProcessDocumentExpiryNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        // SET-07: the company's configured reminder schedule replaces the previously hardcoded
        // 90/30/7-day stages. ExpiryReminderStage.NinetyDays/ThirtyDays/SevenDays are now purely
        // positional slots (slot 1/2/3, furthest-out first) — the *SentAt column each maps to on
        // EmployeeDocument no longer necessarily corresponds to its historical day count once a
        // company customises the schedule, but per-slot idempotency (see IsStageAlreadySent /
        // EmployeeDocument.MarkExpiryReminderSent) is unaffected by that, since idempotency keys off
        // (document, expiry date, stage slot), never the configured day count itself. A disabled
        // reminder schedule (RemindersEnabled=false) or a null slot skips upcoming-expiry stage
        // evaluation entirely; the separate overdue/expired path below is unaffected by this setting.
        var reminderSettings = await documentReminderSettingsReader.GetDocumentReminderSettingsAsync(request.CompanyId, cancellationToken);

        var reminderStages = reminderSettings.RemindersEnabled
            ? BuildReminderStages(reminderSettings)
            : [];

        var now        = clock.UtcNowOffset();
        var timeZoneId = await timeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today      = clock.TodayIn(timeZoneId);
        var widestLookaheadDays = reminderStages.Count == 0 ? 0 : reminderStages.Max(s => s.Days);
        var widestThreshold = today.AddDays(widestLookaheadDays);

        // Read-only projection to gather event data — no change tracking needed. Superset query:
        // anything within the widest (90-day) lookahead, or already expired and not yet notified.
        // Exact per-stage evaluation happens in-memory below against the tracked entity.
        // SET-07: when reminders are disabled (or no stage is configured) the upcoming-expiry half
        // of this condition matches nothing (reminderStages.Count == 0), leaving only the
        // always-on overdue/expired half — reminders being off never suppresses the overdue alert.
        var remindersActive = reminderStages.Count > 0;

        var candidates = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.CompanyId == request.CompanyId
               && ed.ExpiryDate != null
               && ed.IsLatestVersion
               && !ed.IsArchived
               && ((remindersActive && ed.ExpiryDate >= today && ed.ExpiryDate <= widestThreshold
                    && (ed.ExpiryReminder90SentAt == null || ed.ExpiryReminder30SentAt == null || ed.ExpiryReminder7SentAt == null))
                || (ed.ExpiryDate < today && ed.ExpiredNotifiedAt == null))
            select new
            {
                EmployeeDocumentId = ed.Id,
                ed.EmployeeId,
                ed.ExpiryDate,
                d.Title,
                DocumentTypeName = dt.Name,
            }
        ).ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return new ProcessDocumentExpiryNotificationsResponse(0, 0);

        // Load entities for update with full tracking.
        var ids      = candidates.Select(c => c.EmployeeDocumentId).ToList();
        var entities = await db.EmployeeDocuments
            .Where(ed => ids.Contains(ed.Id))
            .ToDictionaryAsync(ed => ed.Id, cancellationToken);

        var reminderCounts = new Dictionary<ExpiryReminderStage, int>
        {
            [ExpiryReminderStage.NinetyDays] = 0,
            [ExpiryReminderStage.ThirtyDays] = 0,
            [ExpiryReminderStage.SevenDays]  = 0,
        };
        var expiredCount = 0;

        foreach (var c in candidates)
        {
            var entity = entities[c.EmployeeDocumentId];

            if (c.ExpiryDate >= today)
            {
                foreach (var (stage, days) in reminderStages)
                {
                    if (IsStageAlreadySent(entity, stage))
                        continue;

                    var stageThreshold = c.ExpiryDate!.Value.AddDays(-days);
                    if (today < stageThreshold)
                        continue;

                    var daysUntil = c.ExpiryDate.Value.DayNumber - today.DayNumber;

                    await auditPublisher.PublishAsync(new DocumentExpiringSoonAuditEvent(
                        request.CompanyId,
                        c.EmployeeDocumentId,
                        c.EmployeeId,
                        c.Title,
                        c.DocumentTypeName,
                        c.ExpiryDate.Value,
                        daysUntil,
                        now), cancellationToken);

                    await taskCreator.CreateAsync(
                        companyId:          request.CompanyId,
                        createdBy:          DocumentsSystemActor.Id,
                        title:              $"Document expiring soon: {c.Title}",
                        description:        $"'{c.Title}' ({c.DocumentTypeName}) expires in {daysUntil} day(s) on {c.ExpiryDate.Value:d}. Please arrange renewal.",
                        priority:           days <= 7 ? TaskPriority.Critical : TaskPriority.High,
                        source:             TaskSource.Document,
                        actionType:         TaskActionType.Upload,
                        dueDate:            c.ExpiryDate.Value,
                        assignedEmployeeId: c.EmployeeId,
                        assignedUserId:     null,
                        sourceEntityId:     c.EmployeeDocumentId,
                        cancellationToken:  cancellationToken);

                    entity.MarkExpiryReminderSent(stage, now);

                    // Keep the legacy ExpiringSoonNotifiedAt flag (consumed elsewhere, e.g. the
                    // "expiring soon" workload action) in sync with the 30-day stage — the closest
                    // equivalent of its original single-threshold meaning.
                    if (stage == ExpiryReminderStage.ThirtyDays)
                        entity.MarkExpiringSoonNotified(now);

                    reminderCounts[stage]++;
                }
            }
            else
            {
                await auditPublisher.PublishAsync(new DocumentExpiredAuditEvent(
                    request.CompanyId,
                    c.EmployeeDocumentId,
                    c.EmployeeId,
                    c.Title,
                    c.DocumentTypeName,
                    c.ExpiryDate!.Value,
                    now), cancellationToken);

                await taskCreator.CreateAsync(
                    companyId:          request.CompanyId,
                    createdBy:          DocumentsSystemActor.Id,
                    title:              $"Document expired: {c.Title}",
                    description:        $"'{c.Title}' ({c.DocumentTypeName}) expired on {c.ExpiryDate.Value:d}. Please collect an updated copy.",
                    priority:           TaskPriority.Critical,
                    source:             TaskSource.Document,
                    actionType:         TaskActionType.Upload,
                    dueDate:            today.AddDays(7),
                    assignedEmployeeId: c.EmployeeId,
                    assignedUserId:     null,
                    sourceEntityId:     c.EmployeeDocumentId,
                    cancellationToken:  cancellationToken);

                entity.MarkExpiredNotified(now);
                expiredCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var totalReminders = reminderCounts.Values.Sum();

        return new ProcessDocumentExpiryNotificationsResponse(
            ExpiringSoonCount: totalReminders,
            ExpiredCount:      expiredCount,
            Reminder90Count:   reminderCounts[ExpiryReminderStage.NinetyDays],
            Reminder30Count:   reminderCounts[ExpiryReminderStage.ThirtyDays],
            Reminder7Count:    reminderCounts[ExpiryReminderStage.SevenDays]);
    }

    /// <summary>
    /// SET-07: maps the company's configured (up to 3) day-offsets onto the fixed
    /// NinetyDays/ThirtyDays/SevenDays stage slots by position (slot 1/2/3), skipping any null slot.
    /// The stage enum member names are now purely positional/historical — see the class-level remarks.
    /// </summary>
    private static IReadOnlyList<(ExpiryReminderStage Stage, int Days)> BuildReminderStages(
        CompanyDocumentReminderSettings settings)
    {
        var stages = new List<(ExpiryReminderStage Stage, int Days)>(3);

        if (settings.OffsetDays1 is int day1) stages.Add((ExpiryReminderStage.NinetyDays, day1));
        if (settings.OffsetDays2 is int day2) stages.Add((ExpiryReminderStage.ThirtyDays, day2));
        if (settings.OffsetDays3 is int day3) stages.Add((ExpiryReminderStage.SevenDays, day3));

        return stages;
    }

    private static bool IsStageAlreadySent(EmployeeDocument entity, ExpiryReminderStage stage) => stage switch
    {
        ExpiryReminderStage.NinetyDays => entity.ExpiryReminder90SentAt != null,
        ExpiryReminderStage.ThirtyDays => entity.ExpiryReminder30SentAt != null,
        ExpiryReminderStage.SevenDays  => entity.ExpiryReminder7SentAt != null,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };
}

/// <summary>System actor id used for automated task creation, mirroring the SystemActor pattern
/// used elsewhere in this module (e.g. the pre-existing local const of the same value).</summary>
internal static class DocumentsSystemActor
{
    public static readonly Guid Id = Guid.Empty;
}
