using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetSicknessRecord;

internal sealed class Endpoint(GetSicknessRecordHandler handler)
    : Endpoint<GetSicknessRecordRequest, GetSicknessRecordResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/sickness-records/{id:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(GetSicknessRecordRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
