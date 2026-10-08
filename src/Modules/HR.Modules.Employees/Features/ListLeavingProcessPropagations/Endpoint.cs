using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListLeavingProcessPropagations;

internal sealed class Endpoint(ListLeavingProcessPropagationsHandler handler)
    : Endpoint<ListLeavingProcessPropagationsRequest, ListLeavingProcessPropagationsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/leaving-process-propagations");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        ListLeavingProcessPropagationsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
