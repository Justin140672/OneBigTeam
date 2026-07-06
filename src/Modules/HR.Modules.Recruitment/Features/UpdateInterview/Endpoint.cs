using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.UpdateInterview;

internal sealed class Endpoint(UpdateInterviewHandler handler)
    : Endpoint<UpdateInterviewRequest, UpdateInterviewResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/interviews/{interviewId:guid}");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        UpdateInterviewRequest request,
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
