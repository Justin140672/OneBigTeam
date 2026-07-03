using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.UpdateSicknessRecord;

internal sealed class Endpoint(UpdateSicknessRecordHandler handler)
    : Endpoint<UpdateSicknessRecordRequest, UpdateSicknessRecordResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records/{id:guid}");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(UpdateSicknessRecordRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
