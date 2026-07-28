using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiter;

internal sealed class Endpoint(GetExternalRecruiterHandler handler)
    : Endpoint<GetExternalRecruiterRequest, GetExternalRecruiterResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/external-recruiters/{externalRecruiterId:guid}");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        GetExternalRecruiterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
