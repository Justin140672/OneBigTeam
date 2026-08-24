using HR.SharedKernel;

namespace HR.Modules.Probation;

internal sealed record ProbationRecordCreatedAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    string? Notes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.created";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => null;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => "Probation record created";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { EmployeeId, ManagerEmployeeId, StartDate, ExpectedEndDate, Notes };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record ProbationReviewCreatedAuditEvent(
    Guid CompanyId,
    Guid ProbationReviewId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    string ReviewType,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-review.created";
    string IAuditEvent.EntityType       => "ProbationReview";
    Guid   IAuditEvent.EntityId         => ProbationReviewId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => null;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"{ReviewType} review created";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { ProbationRecordId, ReviewType, DueDate };
    object? IAuditEvent.Metadata        => null;
}

internal sealed record ProbationExtendedAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid DecisionMakerEmployeeId,
    DateOnly PreviousExpectedEndDate,
    DateOnly NewExpectedEndDate,
    string ExtensionReason,
    DateOnly DecisionDate,
    Guid ExtensionConfirmationReviewId,
    Guid NewFinalReviewId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.extended";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => DecisionMakerEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Probation extended to {NewExpectedEndDate:d MMM yyyy}";
    object? IAuditEvent.Before          => new { ExpectedEndDate = PreviousExpectedEndDate };
    object? IAuditEvent.After           => new { ExpectedEndDate = NewExpectedEndDate, ExtensionReason, DecisionDate };
    object? IAuditEvent.Metadata        => new { ExtensionConfirmationReviewId, NewFinalReviewId };
}

internal sealed record ProbationReviewCompletedAuditEvent(
    Guid CompanyId,
    Guid ProbationReviewId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid CompletedByEmployeeId,
    string ReviewType,
    string? Outcome,
    string? Notes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-review.completed";
    string IAuditEvent.EntityType       => "ProbationReview";
    Guid   IAuditEvent.EntityId         => ProbationReviewId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => CompletedByEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => Outcome is null ? $"{ReviewType} review completed" : $"{ReviewType} review completed: {Outcome}";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Completed", Outcome, Notes };
    object? IAuditEvent.Metadata        => null;
}
