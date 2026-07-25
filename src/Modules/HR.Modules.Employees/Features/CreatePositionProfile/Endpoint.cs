using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreatePositionProfile;

internal sealed class Endpoint(
    CreatePositionProfileHandler handler) : Endpoint<CreatePositionProfileRequest, CreatePositionProfileResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/position-profiles");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreatePositionProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var actorEmployeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/position-profiles/{result.Value.Id}",
            result.Value));
    }
}
