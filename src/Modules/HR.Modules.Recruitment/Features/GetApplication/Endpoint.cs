using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetApplication;

internal sealed class Endpoint(GetApplicationHandler handler)
    : Endpoint<GetApplicationRequest, GetApplicationResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}");
        Policies("candidate:view");
    }

    public override async Task HandleAsync(
        GetApplicationRequest request,
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
