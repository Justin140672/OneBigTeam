using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Tasks.Features.ListTaskCompletionOperationsRequiringIntervention;

internal sealed class Endpoint(ListTaskCompletionOperationsRequiringInterventionHandler handler)
    : Endpoint<ListTaskCompletionOperationsRequiringInterventionRequest, ListTaskCompletionOperationsRequiringInterventionResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/tasks/completion-operations/requiring-intervention");
        Policies("company:manage");
    }

    public override async Task HandleAsync(
        ListTaskCompletionOperationsRequiringInterventionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
