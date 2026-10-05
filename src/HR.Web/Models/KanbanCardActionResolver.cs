namespace HR.Web.Models;

public enum KanbanCardAction
{
    None,
    RecordOfferResponse,
    Hire,
    Appoint,
    MakeOffer,
    RecordOutcome,
    MoveToNextInterviewStage,
    ScheduleInterview,
    ReviewCv,
}

public interface IKanbanActionSource
{
    bool IsInternal { get; }
    string? OfferResponseStatus { get; }
    string? InternalAppointmentStatus { get; }
    bool CurrentStageHasPendingInterview { get; }
    string? CurrentStageInterviewOutcome { get; }
    bool HasNextInterviewStage { get; }
    Guid? NextInterviewStageId { get; }
    bool AllRequiredInterviewStagesPassed { get; }
}

public static class KanbanCardActionResolver
{
    public static bool CanScheduleInterview(IKanbanActionSource candidate, KanbanCardAction action) =>
        action == KanbanCardAction.ScheduleInterview
        || (action == KanbanCardAction.ReviewCv && candidate.HasNextInterviewStage);

    public static bool ShowNoFurtherInterviewStage(IKanbanActionSource candidate, bool isInterviewStage) =>
        isInterviewStage
        && !candidate.CurrentStageHasPendingInterview
        && candidate.CurrentStageInterviewOutcome == "Passed"
        && !candidate.HasNextInterviewStage;

    public static bool ShowAddInterviewStageLink(IKanbanActionSource candidate, bool isInterviewStage, bool canManageStages) =>
        canManageStages && ShowNoFurtherInterviewStage(candidate, isInterviewStage);

    public static KanbanCardAction Resolve(
        IKanbanActionSource candidate, bool isInterviewStage, bool isOfferStage, bool canAppointInternal)
    {
        if (isOfferStage)
        {
            return candidate.OfferResponseStatus switch
            {
                "AwaitingResponse" => KanbanCardAction.RecordOfferResponse,
                "Accepted" when candidate.IsInternal =>
                    canAppointInternal && candidate.InternalAppointmentStatus != "Completed"
                        ? KanbanCardAction.Appoint
                        : KanbanCardAction.None,
                "Accepted" => KanbanCardAction.Hire,
                null => candidate.AllRequiredInterviewStagesPassed
                    ? KanbanCardAction.MakeOffer
                    : KanbanCardAction.None,
                _ => KanbanCardAction.None,
            };
        }

        if (isInterviewStage)
        {
            if (candidate.CurrentStageHasPendingInterview)
                return KanbanCardAction.RecordOutcome;

            if (candidate.CurrentStageInterviewOutcome == "Passed")
            {
                if (candidate.HasNextInterviewStage && candidate.NextInterviewStageId is not null)
                    return KanbanCardAction.MoveToNextInterviewStage;

                return candidate.AllRequiredInterviewStagesPassed
                    ? KanbanCardAction.MakeOffer
                    : KanbanCardAction.None;
            }

            return KanbanCardAction.ScheduleInterview;
        }

        return KanbanCardAction.ReviewCv;
    }
}
