using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateCandidateApplication;

internal sealed class Endpoint(CreateCandidateApplicationHandler handler, ICurrentUser currentUser)
    : Endpoint<CreateCandidateApplicationRequest, CreateCandidateApplicationResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/new-candidate");
        Policies("recruitment:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        CreateCandidateApplicationRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid performedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var outcome = await handler.HandleAsync(request, performedBy, cancellationToken);

        // A duplicate email is a 409 whose body also identifies the existing candidate, so the client
        // can offer "select existing" rather than creating a second record.
        if (outcome.DuplicateCandidate is { } duplicate)
        {
            await Send.ResultAsync(TypedResults.Conflict(duplicate));
            return;
        }

        var result = outcome.Result;
        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/vacancies/{result.Value.VacancyId}/applications/{result.Value.ApplicationId}",
            result.Value));
    }
}
