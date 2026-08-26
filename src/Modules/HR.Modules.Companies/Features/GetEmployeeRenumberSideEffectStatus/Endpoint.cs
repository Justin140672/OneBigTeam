using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetEmployeeRenumberSideEffectStatus;

internal sealed class Endpoint(GetEmployeeRenumberSideEffectStatusHandler handler)
    : Endpoint<GetEmployeeRenumberSideEffectStatusRequest, GetEmployeeRenumberSideEffectStatusResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employee-renumber-side-effects/{outboxMessageId:guid}");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        GetEmployeeRenumberSideEffectStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
