using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed class Endpoint(GetRecruitmentKanbanHandler handler)
    : Endpoint<GetRecruitmentKanbanRequest, GetRecruitmentKanbanResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/kanban");
        Policies("recruitment:view");
    }

    public override async Task HandleAsync(
        GetRecruitmentKanbanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
