namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Marks whether a <see cref="RecruitmentStage"/> is a terminal stage in the pipeline and, if so,
/// what outcome it represents. Exactly one active stage per company must have TerminalOutcome ==
/// Hired, and exactly one active stage per company must have TerminalOutcome == Rejected (enforced
/// by the CreateRecruitmentStage/UpdateRecruitmentStage/SetRecruitmentStageActiveStatus validators).
/// </summary>
internal enum RecruitmentStageTerminalOutcome
{
    None,
    Hired,
    Rejected,
}
