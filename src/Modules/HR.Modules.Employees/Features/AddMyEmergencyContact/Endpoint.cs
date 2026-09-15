using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed class Endpoint(AddMyEmergencyContactHandler handler, ICurrentUser currentUser)
    : Endpoint<AddMyEmergencyContactRequest, AddMyEmergencyContactResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/me/emergency-contacts");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        AddMyEmergencyContactRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            employeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
