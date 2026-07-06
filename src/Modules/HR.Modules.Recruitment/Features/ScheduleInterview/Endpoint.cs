using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ScheduleInterview;

internal sealed class Endpoint(ScheduleInterviewHandler handler)
    : Endpoint<ScheduleInterviewRequest, ScheduleInterviewResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/interviews");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        ScheduleInterviewRequest request,
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

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/vacancies/{request.VacancyId}/applications/{request.ApplicationId}/interviews/{result.Value!.Id}",
            result.Value));
    }
}
