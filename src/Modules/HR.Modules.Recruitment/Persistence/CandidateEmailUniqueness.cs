using HR.Modules.Recruitment.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HR.Modules.Recruitment.Persistence;

internal sealed record ExistingCandidateMatch(
    Guid CandidateId,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive);

internal static class CandidateEmailUniqueness
{
    public const string UniqueIndexName = "ux_candidates_company_id_normalised_email";

    public static string LockKey(Guid companyId, string normalisedEmail) =>
        $"recruitment:candidate-email:{companyId:N}:{normalisedEmail}";

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

    public static bool IsViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UniqueIndexName,
        };
}
