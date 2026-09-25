using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Features.CreateCandidate;

/// <summary>
/// Creates a candidate with no application. Email uniqueness follows
/// <see cref="CandidateEmailUniqueness"/> — the same normaliser, advisory lock and unique-index
/// translation as the combined CandidateApplicationIntake — so a concurrent create through either
/// path yields exactly one candidate and a stable 409 for the loser.
/// </summary>
internal sealed class CreateCandidateHandler(
    RecruitmentDbContext db,
    IClock clock,
    ILogger<CreateCandidateHandler>? logger = null)
{
    public async Task<CreateCandidateResult> HandleAsync(
        CreateCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;
        var email = request.Email.Trim();
        var normalisedEmail = CandidateEmail.Normalise(email);

        // Cheap pre-check; re-checked under the creation lock below.
        var existing = await CandidateEmailUniqueness.FindExistingAsync(db, companyId, normalisedEmail, cancellationToken);
        if (existing is not null)
            return CreateCandidateResult.Duplicate(existing);

        Candidate? candidate = null;

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await CandidateEmailUniqueness.AcquireCreationLockAsync(db, companyId, normalisedEmail, cancellationToken);

            existing = await CandidateEmailUniqueness.FindExistingAsync(db, companyId, normalisedEmail, cancellationToken);
            if (existing is not null)
                return CreateCandidateResult.Duplicate(existing);

            var now = clock.UtcNowOffset();

            candidate = Candidate.Create(
                Guid.NewGuid(),
                companyId,
                request.FirstName,
                request.LastName,
                email,
                request.Phone,
                request.ResumeUrl,
                now);

            db.Candidates.Add(candidate);
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CandidateEmailUniqueness.IsViolation(ex))
        {
            // Final safeguard: a writer that does not take the creation lock (e.g. an UpdateCandidate
            // email change) claimed this email first. Never surface this as a 500.
            db.ChangeTracker.Clear();
            logger?.LogInformation(
                "Candidate creation in company {CompanyId} hit the candidate email unique index; reporting the existing candidate.",
                companyId);

            existing = await CandidateEmailUniqueness.FindExistingAsync(db, companyId, normalisedEmail, cancellationToken);
            return existing is not null
                ? CreateCandidateResult.Duplicate(existing)
                : CreateCandidateResult.Failed(Error.Conflict("A candidate with this email already exists in this company."));
        }

        return CreateCandidateResult.Success(new CreateCandidateResponse(
            candidate.Id,
            candidate.CompanyId,
            candidate.FirstName,
            candidate.LastName,
            candidate.Email,
            candidate.Phone,
            candidate.ResumeUrl,
            candidate.CreatedAt,
            candidate.UpdatedAt));
    }
}

/// <summary>
/// Success, a duplicate-email match (409 carrying the existing candidate), or any other error. Exposes
/// the inner <see cref="Result{T}"/> members directly so callers can treat it like a result.
/// </summary>
internal sealed record CreateCandidateResult(
    Result<CreateCandidateResponse> Result,
    CreateCandidateDuplicateCandidateResponse? DuplicateCandidate)
{
    public bool IsSuccess => Result.IsSuccess;
    public bool IsFailure => Result.IsFailure;
    public CreateCandidateResponse? Value => Result.Value;
    public Error Error => Result.Error;

    public static CreateCandidateResult Success(CreateCandidateResponse response) =>
        new(HR.SharedKernel.Result.Success(response), null);

    public static CreateCandidateResult Failed(Error error) =>
        new(HR.SharedKernel.Result.Failure<CreateCandidateResponse>(error), null);

    public static CreateCandidateResult Duplicate(ExistingCandidateMatch match)
    {
        var duplicate = new CreateCandidateDuplicateCandidateResponse(
            $"A candidate with email '{match.Email}' already exists in this company. Select the existing candidate instead.",
            CreateCandidateDuplicateCandidateResponse.ErrorCode,
            match.CandidateId,
            match.FirstName,
            match.LastName,
            match.Email,
            match.IsActive);

        return new(HR.SharedKernel.Result.Failure<CreateCandidateResponse>(Error.Conflict(duplicate.Error)), duplicate);
    }
}
