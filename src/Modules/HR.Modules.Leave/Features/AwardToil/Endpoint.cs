using FastEndpoints;
using HR.Modules.Leave.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.AwardToil;

internal sealed class Endpoint(
    AwardToilHandler handler,
    ICurrentUser currentUser,
    LeaveResourceAuthorizer authorizer) : Endpoint<AwardToilRequest, AwardToilResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/toil");
        Policies("leave:approve");
    }

    public override async Task HandleAsync(
        AwardToilRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } awarderId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // SEC: Ticket 4 - the acting awarder must always be the authenticated caller, never
        // trusted from request data. Any client-supplied AwardedByEmployeeId is discarded here
        // and replaced with the server-resolved identity before authorization or persistence.
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();
        request = request with
        {
            AwardedByEmployeeId = awarderId,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
        };

        // Ticket 4: only HR Administrators or a manager anywhere above the target employee in
        // the reporting hierarchy may award TOIL.
        if (!await authorizer.CanAwardToilAsync(request.CompanyId, awarderId, request.EmployeeId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            // P1 #4: routes "concurrency" (as well as "conflict") to 409, matching every other
            // versioned-aggregate endpoint (see ProblemResults.FromError).
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created((string?)null, result.Value!));
    }
}
