using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.RecordInterviewOutcome;

internal sealed class Endpoint(RecordInterviewOutcomeHandler handler, ICurrentUser currentUser)
    : Endpoint<RecordInterviewOutcomeRequest, RecordInterviewOutcomeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/interviews/{interviewId:guid}/outcome");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        RecordInterviewOutcomeRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid recordedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, recordedBy, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
