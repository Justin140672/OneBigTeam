using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.GetProbationRecordByEmployee;

internal sealed class Endpoint(
    GetProbationRecordByEmployeeHandler handler) : Endpoint<GetProbationRecordByEmployeeRequest, GetProbationRecordByEmployeeResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/probation-record");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        GetProbationRecordByEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound());
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
