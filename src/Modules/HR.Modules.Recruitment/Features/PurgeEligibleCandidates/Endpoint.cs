using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.PurgeEligibleCandidates;

/// <summary>SET-05: "role:company-administrator" — a distinctly stronger/narrower boundary than
/// "recruitment:manage" (which Recruiters hold), mirroring DOC-04's
/// PurgeEligibleArchivedEmployeeDocuments — this is real, unrecoverable data redaction, not a
/// silently scheduled recurring job.</summary>
internal sealed class Endpoint(PurgeEligibleCandidatesHandler handler, ICurrentUser currentUser)
    : Endpoint<PurgeEligibleCandidatesRequest, PurgeEligibleCandidatesResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/candidates/purge-eligible");
        Policies("role:company-administrator");
    }

    public override async Task HandleAsync(
        PurgeEligibleCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid purgedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, purgedBy, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
