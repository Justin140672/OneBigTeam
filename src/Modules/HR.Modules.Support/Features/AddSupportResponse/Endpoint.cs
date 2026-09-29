using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Support.Features.AddSupportResponse;

internal sealed class Endpoint(AddSupportResponseHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<AddSupportResponseRequest, AddSupportResponseResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/support/requests/{id:guid}/responses");
        Policies("support:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(AddSupportResponseRequest request, CancellationToken cancellationToken)
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

        var authResult = await authorizationService.AuthorizeAsync(User, "support:manage");
        var isStaffResponse = authResult.Succeeded;

        var result = await handler.HandleAsync(request, userId, isStaffResponse, cancellationToken);

        if (result.IsFailure)
        {
            // P1 stored-XSS fix: validation failures (e.g. a body that sanitises to nothing, or a
            // rejected attachment) are 400s, not 404s — use the canonical translator.
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/support/requests/{request.Id}/responses/{result.Value!.Id}", result.Value));
    }
}
