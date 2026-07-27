using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.PreviewBackfillEmployeeNumbers;

internal sealed class Endpoint(PreviewBackfillEmployeeNumbersHandler handler)
    : Endpoint<PreviewBackfillEmployeeNumbersRequest, PreviewBackfillEmployeeNumbersResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/backfill-employee-numbers/preview");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        PreviewBackfillEmployeeNumbersRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
