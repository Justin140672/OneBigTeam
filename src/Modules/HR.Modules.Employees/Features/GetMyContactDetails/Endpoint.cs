using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyContactDetails;

internal sealed class Endpoint(GetMyContactDetailsHandler handler, ICurrentUser currentUser) : EndpointWithoutRequest<GetMyContactDetailsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/contact-details");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
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
