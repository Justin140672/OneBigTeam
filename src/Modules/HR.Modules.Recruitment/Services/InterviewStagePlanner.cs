using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Services;

internal static class InterviewStagePlanner
{
    public const string DefaultInterviewStageName = "Interview";

    private static readonly string[] Ordinals =
        ["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth"];

    public static InterviewStagePlan Plan(IReadOnlyCollection<RecruitmentStage> companyStages)
    {
        var ordered = companyStages.OrderBy(s => s.DisplayOrder).ToList();

        var activeInterviewStages = ordered
            .Where(s => s.IsActive && !s.IsTerminal && s.Purpose == RecruitmentStagePurpose.Interview)
            .ToList();

        var existingCount = activeInterviewStages.Count;
        var suggestedName = existingCount == 0 ? DefaultInterviewStageName : OrdinalName(existingCount + 1);

        var renameCandidate = existingCount == 1
            && string.Equals(activeInterviewStages[0].Name.Trim(), DefaultInterviewStageName, StringComparison.OrdinalIgnoreCase)
            && !ordered.Any(s => string.Equals(s.Name.Trim(), OrdinalName(1), StringComparison.OrdinalIgnoreCase))
                ? activeInterviewStages[0]
                : null;

        var insertAt = existingCount > 0
            ? activeInterviewStages[^1].DisplayOrder + 1
            : (ordered.FirstOrDefault(s => s.IsActive && !s.IsTerminal && s.Purpose == RecruitmentStagePurpose.Offer)
                ?? ordered.FirstOrDefault(s => s.IsTerminal))?.DisplayOrder
              ?? (ordered.Count == 0 ? 1 : ordered[^1].DisplayOrder + 1);

        return new InterviewStagePlan(suggestedName, insertAt, renameCandidate);
    }

    public static string OrdinalName(int position) =>
        position >= 1 && position <= Ordinals.Length
            ? $"{Ordinals[position - 1]} Interview"
            : $"Interview {position}";
}

internal sealed record InterviewStagePlan(string SuggestedName, int DisplayOrder, RecruitmentStage? StageToRename);
