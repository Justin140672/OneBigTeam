using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.CloseSicknessRecord;

internal sealed class Endpoint(CloseSicknessRecordHandler handler)
    : Endpoint<CloseSicknessRecordRequest, CloseSicknessRecordResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records/{id:guid}/close");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(CloseSicknessRecordRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }
            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.UnprocessableEntity(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
