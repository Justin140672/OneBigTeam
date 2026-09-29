namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Ticket 1: distinguishes the candidate's CV from any other supporting document held against the
/// same <see cref="Candidate"/>. A CV belongs to the candidate (not to a single application) so a
/// single uploaded CV is reused across every vacancy the candidate is considered for. Existing
/// documents recorded before this concept existed default to <see cref="Other"/>; the legacy
/// <see cref="Candidate.ResumeUrl"/> link is left untouched for historical records.
/// </summary>
internal enum CandidateDocumentKind
{
    Other = 0,

    Cv = 1,
}
