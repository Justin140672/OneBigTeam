namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// The single canonical representation of a candidate's email for identity/uniqueness purposes
/// (Ticket: case-insensitive candidate email uniqueness). Two emails identify the same candidate
/// within a company when their normalised forms are equal. Persisted as
/// <c>recruitment.candidates.normalised_email</c> and enforced by the unique index
/// <c>ux_candidates_company_id_normalised_email</c>.
///
/// Normalisation is deliberately simple and must stay in step with the SQL used to backfill
/// existing rows (<c>lower(btrim(email))</c> in migration
/// <c>EnforceCandidateNormalisedEmailUniqueness</c>): trim surrounding whitespace, then lower-case
/// with the invariant culture. No provider-specific rules (dots, plus-addressing) are applied —
/// those would merge genuinely different mailboxes.
/// </summary>
internal static class CandidateEmail
{
    public static string Normalise(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }
}
