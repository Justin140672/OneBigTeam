using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.SuggestWorkEmail;

internal sealed class Endpoint(
    SuggestWorkEmailHandler handler) : Endpoint<SuggestWorkEmailRequest, SuggestWorkEmailResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/work-email-suggestion");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        SuggestWorkEmailRequest request,
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
