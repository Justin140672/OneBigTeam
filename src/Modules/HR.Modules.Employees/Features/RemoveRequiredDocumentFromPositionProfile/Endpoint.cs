using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.RemoveRequiredDocumentFromPositionProfile;

internal sealed class Endpoint(RemoveRequiredDocumentHandler handler, ICurrentUser currentUser) : Endpoint<RemoveRequiredDocumentRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/required-documents/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        RemoveRequiredDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
