using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed class Endpoint(GetRecruitmentKanbanHandler handler)
    : Endpoint<GetRecruitmentKanbanRequest, GetRecruitmentKanbanResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/kanban");
        // Recruiter-only — the Kanban board is an operational recruiting tool, not general vacancy
        // visibility (that's what recruitment:view covers elsewhere), and MoveApplicationStage
        // already requires recruitment:manage, so a broader read policy here didn't match the
        // write side anyway.
        Policies("recruitment:manage");
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
