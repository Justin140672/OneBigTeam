using FastEndpoints;
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
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
