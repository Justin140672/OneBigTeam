using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.UpdatePositionProfile;

internal sealed class Endpoint(
    UpdatePositionProfileHandler handler) : Endpoint<UpdatePositionProfileRequest, UpdatePositionProfileResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/position-profiles/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        UpdatePositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await SendResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
