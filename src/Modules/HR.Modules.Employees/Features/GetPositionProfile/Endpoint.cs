using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetPositionProfile;

internal sealed class Endpoint(
    GetPositionProfileHandler handler) : Endpoint<GetPositionProfileRequest, GetPositionProfileResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/position-profiles/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetPositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
