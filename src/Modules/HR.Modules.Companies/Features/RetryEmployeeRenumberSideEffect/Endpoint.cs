using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

internal sealed class Endpoint(RetryEmployeeRenumberSideEffectHandler handler)
    : Endpoint<RetryEmployeeRenumberSideEffectRequest, RetryEmployeeRenumberSideEffectResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employee-renumber-side-effects/{outboxMessageId:guid}/retry");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        RetryEmployeeRenumberSideEffectRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
