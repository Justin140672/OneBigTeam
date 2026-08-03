namespace HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;

internal sealed record GetOverdueReturnToWorkReviewsResponse(IReadOnlyList<OverdueReturnToWorkReviewItem> Items);

internal sealed record OverdueReturnToWorkReviewItem(
    Guid ReviewId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    DateOnly DueDate,
    Guid? TaskId);
