using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ApplyForInternalVacancy;

/// <summary>
/// Internal recruitment Ticket 4: POST (multipart/form-data, field <c>CvFile</c>) by which the signed-in
/// employee applies for an internally advertised vacancy. Same "role:employee" floor as the Internal
/// Vacancies list/detail endpoints, so no recruitment permission is needed; the route company is
/// matched to the caller's tenant by TenantRouteAuthorizationMiddleware (cross-company → 403).
/// </summary>
internal sealed class Endpoint(ApplyForInternalVacancyHandler handler, ICurrentUser currentUser)
    : Endpoint<ApplyForInternalVacancyRequest, ApplyForInternalVacancyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/internal-vacancies/{vacancyId:guid}/applications");
        Policies("role:employee");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        ApplyForInternalVacancyRequest request,
        CancellationToken cancellationToken)
    {
        // The applicant is always the authenticated user — ICurrentUser.UserId is this app's resolved
        // user id, which is the employee's id (see GetMyEmployee/Endpoint.cs). No employee id is ever
        // accepted from the request.
        if (currentUser.UserId is not Guid applicantEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // A platform "Login as Customer" support session satisfies the role:employee floor but is not
        // an employee and must never apply on anyone's behalf.
        if (currentUser.IsSupportSession)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var outcome = await handler.HandleAsync(request, applicantEmployeeId, cancellationToken);

        if (outcome.Rejection is { } rejection)
        {
            await Send.ResultAsync(TypedResults.Json(
                new { error = rejection.Error, code = rejection.Code },
                statusCode: rejection.StatusCode));
            return;
        }

        var result = outcome.Result;
        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/internal-vacancies/{result.Value.VacancyId}",
            result.Value));
    }
}
