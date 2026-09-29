using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.SubmitSupportRequest;

internal sealed class Endpoint(SubmitSupportRequestHandler handler, ICurrentUser currentUser)
    : Endpoint<SubmitSupportRequestRequest, SubmitSupportRequestResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/support/requests");
        Policies("support:request");
        AllowFileUploads();
    }

    public override async Task HandleAsync(SubmitSupportRequestRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, userId, userId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status409Conflict));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/support/requests/{result.Value!.Id}", result.Value));
    }
}
