using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetEffectiveAccess;

internal sealed class Endpoint(GetEffectiveAccessHandler handler) : Endpoint<GetEffectiveAccessRequest, GetEffectiveAccessResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/{employeeId:guid}/effective-access");
        Policies("users:manage");
    }

    public override async Task HandleAsync(GetEffectiveAccessRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
