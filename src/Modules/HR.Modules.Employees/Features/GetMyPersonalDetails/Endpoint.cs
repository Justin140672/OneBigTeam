using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyPersonalDetails;

internal sealed class Endpoint(GetMyPersonalDetailsHandler handler, ICurrentUser currentUser) : EndpointWithoutRequest<GetMyPersonalDetailsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/personal-details");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(Route<string>("companyId"), out var companyId))
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        var result = await handler.HandleAsync(companyId, userId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
