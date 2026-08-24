using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.MarkProbationNotApplicable;

internal sealed class Endpoint(
    MarkProbationNotApplicableHandler handler) : Endpoint<MarkProbationNotApplicableRequest, MarkProbationNotApplicableResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/probation/employees/{employeeId:guid}/not-applicable");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        MarkProbationNotApplicableRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
