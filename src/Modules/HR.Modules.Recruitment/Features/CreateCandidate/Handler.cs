using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Features.CreateCandidate;

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
