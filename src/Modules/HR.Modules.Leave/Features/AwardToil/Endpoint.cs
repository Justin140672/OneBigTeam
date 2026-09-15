using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.AwardToil;

internal sealed class Endpoint(
    AwardToilHandler handler) : Endpoint<AwardToilRequest, AwardToilResponse>
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
        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            cancellationToken);

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
