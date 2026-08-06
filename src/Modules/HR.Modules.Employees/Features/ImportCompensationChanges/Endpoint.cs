using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ImportCompensationChanges;

internal sealed class Endpoint(ImportCompensationChangesHandler handler, ICurrentUser currentUser)
    : Endpoint<ImportCompensationChangesRequest, ImportCompensationChangesResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/compensation/import");
        Policies("employee:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        ImportCompensationChangesRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        await using var stream = request.File.OpenReadStream();

        var outcome = await handler.HandleAsync(request.CompanyId, stream, actorEmployeeId, cancellationToken);

        switch (outcome.Type)
        {
            case ImportCompensationOutcomeType.InvalidFile:
                await Send.ResultAsync(TypedResults.BadRequest(new { error = outcome.Error }));
                return;

            case ImportCompensationOutcomeType.ValidationFailed:
                await Send.ResultAsync(TypedResults.UnprocessableEntity(new { errors = outcome.RowErrors }));
                return;

            default:
                await Send.ResultAsync(TypedResults.Ok(outcome.Response));
                return;
        }
    }
}
