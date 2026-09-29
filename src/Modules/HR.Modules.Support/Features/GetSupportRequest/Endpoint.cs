using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.GetSupportRequest;

internal sealed class Endpoint(GetSupportRequestHandler handler, ICurrentUser currentUser)
    : Endpoint<GetSupportRequestRequest, GetSupportRequestResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/support/requests/{id:guid}");
        Policies("support:request");
    }

    public override async Task HandleAsync(GetSupportRequestRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        var supportRequest = result.Value!;
        if (currentUser.UserId is Guid userId && userId == supportRequest.SubmittedByUserId)
        {
            await Send.ResultAsync(TypedResults.Ok(result.Value));
            return;
        }

        await Send.ResultAsync(TypedResults.Forbid());
    }
}
