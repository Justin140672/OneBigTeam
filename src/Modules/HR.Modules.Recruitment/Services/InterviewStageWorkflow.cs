using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Services;

internal sealed record InterviewStageState(
    bool CurrentStageHasPendingInterview,
    Guid? PendingInterviewId,
    InterviewOutcome? LatestOutcome,
    bool HasNextInterviewStage,
    Guid? NextInterviewStageId,
    Guid CurrentStageId = default,
    bool IsInterviewStage = false,
    string? NextInterviewStageName = null,
    bool AllRequiredInterviewStagesPassed = true);

/// <summary>
/// Single source of truth for multi-stage interview rules. Legacy interviews with no StageId never
/// count as passed for any stage (they only count as pending), so ambiguous history cannot unlock
/// a later stage or an offer.
/// </summary>
internal static class InterviewStageWorkflow
{
    public static InterviewStageState Evaluate(
        RecruitmentStage currentStage,
        IReadOnlyCollection<RecruitmentStage> activeStages,
        IEnumerable<Interview> applicationInterviews)
    {
        var interviews = applicationInterviews.ToList();
        var isInterviewStage = currentStage.Purpose == RecruitmentStagePurpose.Interview;

        var inStage = isInterviewStage
            ? interviews.Where(i => i.StageId == currentStage.Id || i.StageId is null).ToList()
            : [];

        var pending = inStage
            .Where(i => i.Outcome == InterviewOutcome.Pending)
            .OrderBy(i => i.ScheduledAt)
            .FirstOrDefault();

        var latest = inStage
            .Where(i => i.Outcome != InterviewOutcome.Pending && i.StageId == currentStage.Id)
            .OrderByDescending(i => i.ScheduledAt)
            .Select(i => (InterviewOutcome?)i.Outcome)
            .FirstOrDefault();

        var next = NextInterviewStage(currentStage, activeStages);

        return new InterviewStageState(
            pending is not null,
            pending?.Id,
            latest,
            next is not null,
            next?.Id,
            currentStage.Id,
            isInterviewStage,
            next?.Name,
            DescribeUnpassedStage(RequiredInterviewStages(activeStages), interviews) is null);
    }

    public static IReadOnlyList<RecruitmentStage> RequiredInterviewStages(IEnumerable<RecruitmentStage> stages) =>
        stages
            .Where(s => s is { IsActive: true, IsTerminal: false, Purpose: RecruitmentStagePurpose.Interview })
            .OrderBy(s => s.DisplayOrder)
            .ToList();

    public static RecruitmentStage? NextInterviewStage(
        RecruitmentStage currentStage,
        IEnumerable<RecruitmentStage> activeStages) =>
        currentStage.IsTerminal
            ? null
            : RequiredInterviewStages(activeStages)
                .FirstOrDefault(s => s.DisplayOrder > currentStage.DisplayOrder);

    public static bool IsStagePassed(RecruitmentStage stage, IEnumerable<Interview> interviews)
    {
        var inStage = interviews.Where(i => i.StageId == stage.Id).ToList();

        if (inStage.Any(i => i.Outcome == InterviewOutcome.Pending))
            return false;

        return inStage
            .OrderByDescending(i => i.ScheduledAt)
            .Select(i => (InterviewOutcome?)i.Outcome)
            .FirstOrDefault() == InterviewOutcome.Passed;
    }

    /// <summary>
    /// Returns a user-facing reason an offer cannot be made, or null when allowed. Eligibility comes from
    /// the full interview history: with interview stages configured every one must be passed and none
    /// may have a pending interview, regardless of which stage the application currently sits in.
    /// Companies with no active interview stage keep direct offers.
    /// </summary>
    public static string? DescribeOfferViolation(
        IReadOnlyCollection<RecruitmentStage> activeStages,
        IEnumerable<Interview> applicationInterviews)
    {
        var interviews = applicationInterviews.ToList();
        var required = RequiredInterviewStages(activeStages);

        if (required.Count == 0)
            return null;

        if (interviews.Any(i => i.Outcome == InterviewOutcome.Pending))
            return "Cannot make an offer while an interview is still pending. Record its outcome first.";

        var unpassed = DescribeUnpassedStage(required, interviews);
        if (unpassed is null)
            return null;

        var finalStage = required[^1];
        return unpassed.Id == finalStage.Id
            ? $"Cannot make an offer until an interview in the final interview stage '{unpassed.Name}' has been passed."
            : $"Cannot make an offer until an interview in '{unpassed.Name}' has been passed.";
    }

    /// <summary>
    /// Returns a user-facing reason a generic stage move is not allowed, or null when allowed.
    /// Backward moves (target ordered before the current stage) are permitted as corrections.
    /// Forward moves require every interview stage ordered before the target to be passed, and
    /// moving to an Offer-purpose stage requires all interview stages to be passed. Moving away
    /// from any stage while an interview is pending is blocked; reject cancels pending interviews.
    /// </summary>
    public static string? DescribeMoveViolation(
        RecruitmentStage currentStage,
        RecruitmentStage targetStage,
        IReadOnlyCollection<RecruitmentStage> activeStages,
        IEnumerable<Interview> applicationInterviews)
    {
        if (currentStage.Id == targetStage.Id)
            return null;

        var interviews = applicationInterviews.ToList();

        if (interviews.Any(i => i.Outcome == InterviewOutcome.Pending))
            return $"Cannot move the candidate out of '{currentStage.Name}' while an interview is still pending. Record its outcome first.";

        var required = RequiredInterviewStages(activeStages);

        if (targetStage.Purpose == RecruitmentStagePurpose.Offer)
        {
            var offerViolation = DescribeOfferViolation(activeStages, interviews);
            return offerViolation is null
                ? null
                : offerViolation.Replace("make an offer", $"move to '{targetStage.Name}'");
        }

        if (targetStage.DisplayOrder <= currentStage.DisplayOrder)
            return null;

        var unpassed = required
            .Where(s => s.DisplayOrder < targetStage.DisplayOrder)
            .FirstOrDefault(s => !IsStagePassed(s, interviews));

        return unpassed is null
            ? null
            : $"Cannot move to '{targetStage.Name}' until an interview in '{unpassed.Name}' has been passed.";
    }

    private static RecruitmentStage? DescribeUnpassedStage(
        IReadOnlyList<RecruitmentStage> required,
        IReadOnlyCollection<Interview> interviews) =>
        required.FirstOrDefault(s => !IsStagePassed(s, interviews));
}
