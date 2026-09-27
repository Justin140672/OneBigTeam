using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ListInternalVacancies;

internal sealed class Endpoint(ListInternalVacanciesHandler handler, ICurrentUser currentUser)
    : Endpoint<ListInternalVacanciesRequest, ListInternalVacanciesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/internal-vacancies");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListInternalVacanciesRequest request,
        CancellationToken cancellationToken)
    {
        // Internal recruitment Ticket 4: HasApplied is computed for the signed-in employee only
        // (UserId == EmployeeId convention). A support session is not an employee, so it gets none.
        var currentEmployeeId = currentUser.IsSupportSession ? null : currentUser.UserId;

        var result = await handler.HandleAsync(request, currentEmployeeId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
