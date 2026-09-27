using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.AppointInternalCandidate;

internal sealed class Endpoint(AppointInternalCandidateHandler handler, ICurrentUser currentUser)
    : Endpoint<AppointInternalCandidateRequest, AppointInternalCandidateResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies/{vacancyId:guid}/applications/{applicationId:guid}/appoint");
        // Product decision: completing an internal appointment is a recruitment action, so
        // "recruitment:manage" alone is required (a Recruiter can appoint without also holding
        // "employee:manage"). The Employees-side IEmployeeInternalAppointmentService performs no
        // separate permission check; its tenant, status, manager and idempotency rules still apply.
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        AppointInternalCandidateRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid performedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, performedBy, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
