using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed class Endpoint(
    CreateLeavePolicyHandler handler, ICurrentUser currentUser) : Endpoint<CreateLeavePolicyRequest, CreateLeavePolicyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/leave-policies");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        CreateLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/leave-policies/{result.Value.Id}",
            result.Value));
    }
}
