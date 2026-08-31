namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// DSH-04: an explicit, machine-readable role a <see cref="RecruitmentStage"/> plays in the pipeline,
/// independent of its name or <see cref="RecruitmentStage.DisplayOrder"/>. This exists so authoritative
/// recruitment dashboard metrics ("New applications", "Offers awaiting response", ...) can be computed
/// from a deliberate configuration choice rather than inferred from where a stage happens to sit in a
/// fully customisable ordering.
///
/// A purpose is optional (<c>null</c> = the stage carries no special metric meaning) and is only valid
/// on non-terminal stages — terminal stages already express their meaning through
/// <see cref="RecruitmentStage.TerminalOutcome"/>. More than one stage may share a purpose (for
/// example a company running "Verbal offer" and "Written offer" as two distinct
/// <see cref="Offer"/> stages): metrics count applications across every stage with the relevant purpose.
/// </summary>
internal enum RecruitmentStagePurpose
{
    /// <summary>Newly received applications land here and are awaiting first triage/review.</summary>
    NewApplication,

    /// <summary>The candidate is at an interview step (scheduling, attending, or awaiting outcome).</summary>
    Interview,

    /// <summary>An offer has been / is being extended and the company is awaiting the candidate's response.</summary>
    Offer,
}
