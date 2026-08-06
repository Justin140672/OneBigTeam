using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

internal sealed class Endpoint(AddRequiredDocumentHandler handler, ICurrentUser currentUser)
    : Endpoint<AddRequiredDocumentRequest, AddRequiredDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/position-profiles/{positionProfileId:guid}/required-documents");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        AddRequiredDocumentRequest request,
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
            $"/api/companies/{request.CompanyId}/position-profiles/{request.PositionProfileId}/required-documents/{result.Value!.Id}",
            result.Value));
    }
}
