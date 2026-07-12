using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetMyProfilePhoto;

internal sealed class Endpoint(GetMyProfilePhotoHandler handler) : EndpointWithoutRequest<GetMyProfilePhotoResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/profile-photo");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(Route<string>("companyId"), out var companyId))
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        var response = await handler.HandleAsync(companyId, employeeId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
