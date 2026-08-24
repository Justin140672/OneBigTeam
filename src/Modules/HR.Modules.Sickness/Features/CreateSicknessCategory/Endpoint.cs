using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.CreateSicknessCategory;

internal sealed class Endpoint(CreateSicknessCategoryHandler handler, ICurrentUser currentUser)
    : Endpoint<CreateSicknessCategoryRequest, CreateSicknessCategoryResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/sickness-categories");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(CreateSicknessCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);
        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/sickness-categories/{result.Value!.Id}", result.Value));
    }
}
