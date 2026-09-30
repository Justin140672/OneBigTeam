using HR.Modules.Recruitment.Services;
using HR.SharedKernel;

namespace HR.Modules.Recruitment.Features.CreateCandidateApplication;

/// <summary>
/// Internal recruitment Ticket 3: creates a new candidate, optional CV document and their application
/// to a vacancy as one coordinated use case. The orchestration (transaction, duplicate-email lock,
/// crash-safe CV storage) lives in <see cref="CandidateApplicationIntake"/> so other intake routes
/// (e.g. employee self-apply) share it.
/// </summary>
internal sealed class CreateCandidateApplicationHandler(CandidateApplicationIntake intake)
{
    public async Task<CreateCandidateApplicationResult> HandleAsync(
        CreateCandidateApplicationRequest request,
        Guid performedByUserId,
        CancellationToken cancellationToken)
    {
        var outcome = await intake.CreateAsync(
            new CandidateApplicationIntakeCommand(
                request.CompanyId,
                request.VacancyId,
                request.FirstName,
                request.LastName,
                request.Email,
                request.Phone,
                request.Notes,
                request.Source,
                request.SourceExternalRecruiterId,
                request.CvFile,
                performedByUserId),
            cancellationToken);

        if (outcome.DuplicateCandidate is { } match)
        {
            return CreateCandidateApplicationResult.Duplicate(new CreateCandidateApplicationDuplicateCandidateResponse(
                $"A candidate with email '{match.Email}' already exists in this company. Select the existing candidate instead.",
                CreateCandidateApplicationDuplicateCandidateResponse.ErrorCode,
                match.CandidateId,
                match.FirstName,
                match.LastName,
                match.Email,
                match.IsActive));
        }

        if (outcome.Error is { } error)
            return CreateCandidateApplicationResult.Failed(error);

        var created = outcome.Created!;
        var application = created.Application;

        return CreateCandidateApplicationResult.Success(new CreateCandidateApplicationResponse(
            created.Candidate.Id,
            application.Id,
            application.CompanyId,
            application.VacancyId,
            created.Candidate.FirstName,
            created.Candidate.LastName,
            created.Candidate.Email,
            application.CurrentStageId,
            application.CvDocumentId,
            application.Source,
            application.SourceExternalRecruiterId,
            application.AppliedAt));
    }
}

internal sealed record CreateCandidateApplicationResult(
    Result<CreateCandidateApplicationResponse> Result,
    CreateCandidateApplicationDuplicateCandidateResponse? DuplicateCandidate)
{
    public static CreateCandidateApplicationResult Success(CreateCandidateApplicationResponse response) =>
        new(HR.SharedKernel.Result.Success(response), null);

    public static CreateCandidateApplicationResult Failed(Error error) =>
        new(HR.SharedKernel.Result.Failure<CreateCandidateApplicationResponse>(error), null);

    public static CreateCandidateApplicationResult Duplicate(CreateCandidateApplicationDuplicateCandidateResponse duplicate) =>
        new(HR.SharedKernel.Result.Failure<CreateCandidateApplicationResponse>(Error.Conflict(duplicate.Error)), duplicate);
}
