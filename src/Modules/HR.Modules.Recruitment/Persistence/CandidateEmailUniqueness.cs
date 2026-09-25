using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HR.Modules.Recruitment.Persistence;

/// <summary>An existing candidate in the same company whose normalised email matches.</summary>
internal sealed record ExistingCandidateMatch(
    Guid CandidateId,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive);

/// <summary>
/// The one set of rules every candidate write path (legacy <c>CreateCandidate</c>, the combined
/// <c>CandidateApplicationIntake</c>, and <c>UpdateCandidate</c> email changes) uses to keep a
/// candidate's email unique per company:
/// <list type="number">
/// <item><description>compare on <see cref="Candidate.NormalisedEmail"/> (see
/// <see cref="CandidateEmail.Normalise"/>);</description></item>
/// <item><description>creation paths serialise on the same transaction-scoped advisory lock
/// (<see cref="AcquireCreationLockAsync"/>) and re-check under it, so a concurrent loser reports the
/// winner instead of attempting an insert;</description></item>
/// <item><description>the unique index <see cref="UniqueIndexName"/> is the final safeguard — any
/// path that still hits it translates the 23505 (<see cref="IsViolation"/>) into the same 409
/// duplicate outcome, never a 500.</description></item>
/// </list>
/// </summary>
internal static class CandidateEmailUniqueness
{
    public const string UniqueIndexName = "ux_candidates_company_id_normalised_email";

    /// <summary>Advisory-lock key shared by every candidate creation path. Changing the format
    /// would silently stop concurrent paths from serialising against each other.</summary>
    public static string LockKey(Guid companyId, string normalisedEmail) =>
        $"recruitment:candidate-email:{companyId:N}:{normalisedEmail}";

    /// <summary>
    /// Takes <c>pg_advisory_xact_lock</c> on <see cref="LockKey"/>. Must be called inside an open
    /// transaction (released at commit/rollback). A no-op for non-relational providers (unit tests
    /// on EF InMemory), which have neither transactions nor the unique index.
    /// </summary>
    public static async Task AcquireCreationLockAsync(
        RecruitmentDbContext db,
        Guid companyId,
        string normalisedEmail,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return;

        var lockKey = LockKey(companyId, normalisedEmail);
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    /// <summary>The existing candidate in <paramref name="companyId"/> with this normalised email,
    /// optionally ignoring <paramref name="excludingCandidateId"/>. Prefers an active record, then the
    /// oldest (only relevant if pre-constraint data ever held more than one).</summary>
    public static Task<ExistingCandidateMatch?> FindExistingAsync(
        RecruitmentDbContext db,
        Guid companyId,
        string normalisedEmail,
        CancellationToken cancellationToken,
        Guid? excludingCandidateId = null) =>
        db.Candidates
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.NormalisedEmail == normalisedEmail)
            .Where(c => excludingCandidateId == null || c.Id != excludingCandidateId)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.CreatedAt)
            .Select(c => new ExistingCandidateMatch(c.Id, c.FirstName, c.LastName, c.Email, c.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>True when <paramref name="exception"/> is PostgreSQL 23505 on
    /// <see cref="UniqueIndexName"/> specifically — other unique violations are not duplicates of a
    /// candidate email and must keep surfacing as unexpected failures.</summary>
    public static bool IsViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UniqueIndexName,
        };
}
