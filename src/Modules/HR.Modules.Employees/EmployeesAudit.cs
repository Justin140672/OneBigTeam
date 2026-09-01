using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees;

internal sealed record CompensationRecordCreatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    Guid ActorEmployeeId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.created";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record created";
    object? IAuditEvent.Before => null;
    // AUD-03: Salary amount and Reason (free-text) are prohibited — record safe structured fields only.
    object? IAuditEvent.After => new { EffectiveFrom, SalaryType, Currency };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordImportedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    Guid ActorEmployeeId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    string Currency,
    string Reason,
    Guid ImportBatchId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.imported";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => ImportBatchId;
    string? IAuditEvent.Summary => "Compensation record created via import";
    object? IAuditEvent.Before => null;
    // AUD-03: Salary amount and Reason (free-text) are prohibited.
    object? IAuditEvent.After => new { EffectiveFrom, SalaryType, Currency };
    object? IAuditEvent.Metadata => new { Source = "Import", ImportBatchId };
}

internal sealed record CompensationRecordBulkAppliedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    Guid ActorEmployeeId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    decimal PreviousSalary,
    string Currency,
    string Reason,
    string AdjustmentMode,
    Guid BulkOperationId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.bulk-applied";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => BulkOperationId;
    string? IAuditEvent.Summary => "Compensation record created via bulk adjustment";
    // NFR-01: Salary amounts and Reason (free-text) are prohibited — record the direction of the
    // change (never the amount or the delta) so the audit trail still shows compensation changed.
    private string ChangeDirection =>
        Salary > PreviousSalary ? "Increase"
        : Salary < PreviousSalary ? "Decrease"
        : "NoChange";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { EffectiveFrom, SalaryType, Currency, AdjustmentMode, Direction = ChangeDirection };
    object? IAuditEvent.Metadata => new { BulkOperationId };
}

internal sealed record CompensationRecordClosedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    Guid ActorEmployeeId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.closed";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record closed";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { EffectiveFrom, EffectiveTo };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    Guid ActorEmployeeId,
    DateOnly EffectiveFrom,
    string SalaryType,
    decimal Salary,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.updated";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record updated";
    object? IAuditEvent.Before => null;
    // AUD-03: Salary amount and Reason (free-text) are prohibited.
    object? IAuditEvent.After => new { EffectiveFrom, SalaryType, Currency };
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordDeletedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    Guid ActorEmployeeId,
    DateOnly EffectiveFrom,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.deleted";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record deleted";
    object? IAuditEvent.Before => new { EffectiveFrom };
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordReopenedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    Guid ActorEmployeeId,
    DateOnly EffectiveFrom,
    DateOnly PreviousEffectiveTo,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.reopened";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record reopened after deletion of its successor";
    object? IAuditEvent.Before => new { EffectiveTo = PreviousEffectiveTo };
    object? IAuditEvent.After => new { EffectiveTo = (DateOnly?)null };
    object? IAuditEvent.Metadata => null;
}

// Employee notes are confidential HR content (performance/conduct/wellbeing notes) and audit
// history is readable more broadly than the employee:manage-gated notes feature itself
// (see GetEmployeeAuditHistory/AuditHistoryReader), so the raw NoteText is deliberately excluded
// from both audit payloads below — only a safe category/importance summary is recorded.
internal sealed record EmployeeNoteCreatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid NoteId,
    string Category,
    bool IsImportant,
    Guid? ActorUserId,
    Guid? ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.note.created";
    string IAuditEvent.EntityType => "EmployeeNote";
    Guid IAuditEvent.EntityId => NoteId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee note created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Category, IsImportant };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmployeeNoteSupersededAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid OriginalNoteId,
    Guid NewNoteId,
    Guid? ActorUserId,
    Guid? ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.note.superseded";
    string IAuditEvent.EntityType => "EmployeeNote";
    Guid IAuditEvent.EntityId => OriginalNoteId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee note superseded";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { NewNoteId };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmployeeCreatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string Source,
    Guid? ImportSessionId) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.created";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => ImportSessionId;
    string? IAuditEvent.Summary => Source == "Import" ? "Employee created via import" : "Employee created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Source, ImportSessionId };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmergencyContactAddedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    Guid ContactId,
    string Name,
    string Relationship) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.emergency-contact.added";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Emergency contact added";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ContactId, Name, Relationship };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmergencyContactSnapshot(
    string Name,
    string Relationship,
    string PhoneNumber,
    string? Email);

internal sealed record EmergencyContactUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    EmergencyContactSnapshot? Before,
    EmergencyContactSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.emergency-contact.updated";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Emergency contact updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmployeeProfileSnapshot(
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    string? PreferredName,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender,
    string? GenderOther,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    Guid? LocationId,
    bool HasSystemAccess);

internal sealed record EmployeeProfileUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    EmployeeProfileSnapshot Before,
    EmployeeProfileSnapshot After,
    Guid? CorrelationId = null) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.profile.updated";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => CorrelationId;
    string? IAuditEvent.Summary => "Employee profile updated";
    // NFR-01: PersonalEmail and DateOfBirth are prohibited audit fields — project only the
    // non-sensitive, diff-able profile fields into the before/after snapshots so
    // AuditPayloadRedactionGuard passes the payload through unchanged.
    object? IAuditEvent.Before => ProjectProfile(Before);
    object? IAuditEvent.After => ProjectProfile(After);
    object? IAuditEvent.Metadata => null;

    private static object ProjectProfile(EmployeeProfileSnapshot s) => new
    {
        s.FirstName,
        s.LastName,
        s.WorkEmail,
        s.StartDate,
        s.PreferredName,
        s.Nationality,
        s.Gender,
        s.GenderOther,
        s.DepartmentId,
        s.PositionProfileId,
        s.LocationId,
        s.HasSystemAccess,
    };
}

internal sealed record EmploymentDetailsSnapshot(
    string EmployeeNumber,
    Guid EmploymentTypeId,
    DateOnly StartDate,
    DateOnly? ContinuousServiceDate,
    DateOnly? ProbationEndDate,
    DateOnly? LeavingDate,
    string? Notes,
    Guid? ManagerId);

// Employee number changes made through the Employment tab are administrative corrections (per
// the Employee Number ticket) and must be visible in Employee Audit History like any other
// employment detail correction — reusing this single event rather than inventing a separate
// "employee number changed" event type, consistent with EmployeeProfileUpdatedAuditEvent's
// always-published convention for its own tab.
internal sealed record EmploymentDetailsUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    EmploymentDetailsSnapshot Before,
    EmploymentDetailsSnapshot After,
    Guid? CorrelationId = null) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.employment-details.updated";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => CorrelationId;
    string? IAuditEvent.Summary => Before.EmployeeNumber != After.EmployeeNumber
        ? "Employee number corrected"
        : "Employment details updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record ContactDetailsSnapshot(
    string? PersonalEmail,
    string? PhoneNumber,
    string? HomePhone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? County,
    string? PostCode,
    string? Country);

internal sealed record ContactDetailsUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    ContactDetailsSnapshot? Before,
    ContactDetailsSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.contact-details.updated";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee contact details updated";
    // NFR-01: PersonalEmail is a prohibited audit field — project only the non-sensitive address
    // and phone fields so AuditPayloadRedactionGuard passes the payload through unchanged.
    object? IAuditEvent.Before => ProjectContact(Before);
    object? IAuditEvent.After => ProjectContact(After);
    object? IAuditEvent.Metadata => null;

    private static object? ProjectContact(ContactDetailsSnapshot? s) => s is null ? null : new
    {
        s.PhoneNumber,
        s.HomePhone,
        s.AddressLine1,
        s.AddressLine2,
        s.City,
        s.County,
        s.PostCode,
        s.Country,
    };
}

internal sealed record PositionProfileSnapshot(
    Guid DepartmentId,
    Guid LocationId,
    string Title,
    string? Description,
    int? ProbationMonthsOverride,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    NoticePeriodUnit? NoticePeriodUnitOverride,
    int? NoticePeriodLengthOverride,
    decimal? SalaryMin,
    decimal? SalaryMax,
    SalaryType? SalaryType,
    Guid DefaultLeavePolicyId,
    Guid? OnboardingTemplateId,
    bool IsActive);

internal sealed record PositionProfileCreatedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    PositionProfileSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.created";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Position profile created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record PositionProfileUpdatedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    PositionProfileSnapshot Before,
    PositionProfileSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.updated";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Position profile updated";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => null;
}

internal sealed record LeavingProcessSnapshot(
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    NoticePeriodSource NoticeSource,
    LeavingReason LeavingReason,
    LeavingProcessStatus Status);

internal sealed record LeavingProcessStartedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    LeavingProcessSnapshot After) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.leaving-process.started";
    string IAuditEvent.EntityType => "EmployeeLeavingProcess";
    Guid IAuditEvent.EntityId => LeavingProcessId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leaving process started";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => new { EmployeeStatusChangedTo = "Leaving" };
}

internal sealed record LeavingProcessAmendedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    LeavingProcessSnapshot Before,
    LeavingProcessSnapshot After,
    bool OffboardingAlreadyStarted) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.leaving-process.amended";
    string IAuditEvent.EntityType => "EmployeeLeavingProcess";
    Guid IAuditEvent.EntityId => LeavingProcessId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leaving process amended";
    object? IAuditEvent.Before => Before;
    object? IAuditEvent.After => After;
    object? IAuditEvent.Metadata => new { OffboardingAlreadyStarted };
}

internal sealed record LeavingProcessCancelledAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    string CancellationReason,
    bool OffboardingTasksCancelled) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.leaving-process.cancelled";
    string IAuditEvent.EntityType => "EmployeeLeavingProcess";
    Guid IAuditEvent.EntityId => LeavingProcessId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Leaving process cancelled";
    object? IAuditEvent.Before => new { Status = "InProgress" };
    object? IAuditEvent.After => new { Status = "Cancelled", CancellationReason };
    object? IAuditEvent.Metadata => new { EmployeeStatusChangedTo = "Active", OffboardingTasksCancelled };
}

internal sealed record EmployeePromotionRequestedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid PromotionId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    DateOnly EffectiveDate,
    string Reason) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.promotion.requested";
    string IAuditEvent.EntityType => "EmployeePromotion";
    Guid IAuditEvent.EntityId => PromotionId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee promotion requested";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { PreviousPositionProfileId, NewPositionProfileId, EffectiveDate, Reason };
    object? IAuditEvent.Metadata => null;
}

// Published by EmployeePromotionFinalizer once a promotion has been applied to the employee
// (either immediately from PromoteEmployeeHandler for a same-day/backdated effective date, or
// later by ProcessPromotionsJob) — system-attributed (no actor), mirroring
// EmployeeDepartureFinalisedAuditEvent's convention exactly.
internal sealed record EmployeePromotionCompletedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid PromotionId,
    DateTimeOffset OccurredAt,
    Guid PreviousPositionProfileId,
    Guid NewPositionProfileId,
    DateOnly EffectiveDate) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.promotion.completed";
    string IAuditEvent.EntityType => "EmployeePromotion";
    Guid IAuditEvent.EntityId => PromotionId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee promotion completed";
    object? IAuditEvent.Before => new { PositionProfileId = PreviousPositionProfileId };
    object? IAuditEvent.After => new { PositionProfileId = NewPositionProfileId, EffectiveDate };
    object? IAuditEvent.Metadata => null;
}

// Published by BackfillEmployeeNumbers' commit endpoint, one per employee, after the whole batch
// transaction has committed successfully. Employee number is not a sensitive value in this
// codebase's classification (salary/bank/NI numbers are), so it is safe to record in full.
internal sealed record EmployeeNumberBackfilledAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt,
    string AssignedEmployeeNumber,
    Guid BackfillOperationId) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.employee-number.backfilled";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => BackfillOperationId;
    string? IAuditEvent.Summary => "Employee number backfilled";
    object? IAuditEvent.Before => new { EmployeeNumber = "" };
    object? IAuditEvent.After => new { EmployeeNumber = AssignedEmployeeNumber };
    object? IAuditEvent.Metadata => new { BackfillOperationId };
}

// Published by ProcessLeavingEmployeesJob (Hangfire) once an employee's leaving date has passed
// and their departure has been finalised. There is no ActorUserId/ActorEmployeeId — this is a
// system-driven transition, not a user action — mirroring how other unattended-job audit events
// in this codebase represent the system as the actor (null).
internal sealed record EmployeeDepartureFinalisedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavingProcessId,
    DateTimeOffset OccurredAt,
    bool AccessDisabled,
    bool OffboardingIncomplete) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.departure.finalised";
    string IAuditEvent.EntityType => "Employee";
    Guid IAuditEvent.EntityId => EmployeeId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Employee departure finalised";
    object? IAuditEvent.Before => new { Status = "Leaving" };
    object? IAuditEvent.After => new { Status = "FormerEmployee" };
    object? IAuditEvent.Metadata => new { AccessDisabled, OffboardingIncomplete };
}
