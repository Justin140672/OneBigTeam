using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Probation.Features.CreateProbationRecord;

internal sealed class Endpoint(
    CreateProbationRecordHandler handler,
    ICurrentUser currentUser) : Endpoint<CreateProbationRecordRequest, CreateProbationRecordResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/probation-records");
        Policies("probation:manage");
    }

    public override async Task HandleAsync(
        CreateProbationRecordRequest request,
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
            $"/api/companies/{result.Value!.CompanyId}/probation-records/{result.Value.Id}",
            result.Value));
    }
}
