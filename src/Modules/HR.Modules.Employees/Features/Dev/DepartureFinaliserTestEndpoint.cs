using FastEndpoints;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Jobs;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.DevEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Features.Dev;

/// <summary>
/// E2E-only trigger for the departure finaliser. Gates, in order:
///  1. Development environment: the route is not registered otherwise, and the shared per-request gate
///     returns 404 as a second layer ([DevOnlyEndpoint], HR.SharedKernel.DevEndpoints).
///  2. E2E_TESTING=true (same gate; 404 when off, even in Development).
///  3. Authenticated user (401), HR-administrator policy "role:hr-administrator" (403).
///  4. Caller's tenant equals the route company (403).
///  5. Target employee belongs to that company (404 otherwise, nothing mutated).
/// AllowAnonymous is only so the gate can answer 404 (not 401) when the endpoint is unavailable;
/// steps 3-5 are enforced explicitly below.
/// </summary>
[DevOnlyEndpoint(DevEndpointKind.E2E)]
internal sealed class DepartureFinaliserTestEndpoint(
    ProcessLeavingEmployeesJob departureFinaliserJob,
    EmployeesDbContext db,
    ICurrentTenant currentTenant,
    Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/dev/departure-finaliser/{companyId:guid}/{employeeId:guid}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!HttpContext.User.Identity?.IsAuthenticated ?? true)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var roleAuth = await authorizationService.AuthorizeAsync(HttpContext.User, "role:hr-administrator");
        if (!roleAuth.Succeeded)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var companyId = Route<Guid>("companyId");
        var employeeId = Route<Guid>("employeeId");


        if (!Guid.TryParse(currentTenant.TenantId, out var userCompanyId) || userCompanyId != companyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var employee = await db.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct);

        if (employee is null)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        var inProgressProcess = await db.EmployeeLeavingProcesses
            .Where(p => p.EmployeeId == employeeId && p.CompanyId == companyId &&
                        p.Status == LeavingProcessStatus.InProgress)
            .OrderByDescending(p => p.StartedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        if (inProgressProcess is not null)
        {
        }
        else
        {
            var completedProcess = await db.EmployeeLeavingProcesses
                .Where(p => p.EmployeeId == employeeId && p.CompanyId == companyId &&
                            p.Status == LeavingProcessStatus.Completed)
                .OrderByDescending(p => p.StartedAt)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);

            if (completedProcess is not null && employee!.Status == EmploymentStatus.FormerEmployee)
            {
                await Send.ResultAsync(TypedResults.Ok());
                return;
            }

            if (completedProcess is null)
            {
                await Send.ResultAsync(TypedResults.BadRequest("Employee has no leaving process to finalize"));
            }
            else
            {
                await Send.ResultAsync(TypedResults.BadRequest("Process is completed but employee status is not FormerEmployee"));
            }
            return;
        }

        var process = inProgressProcess;


        try
        {
            await departureFinaliserJob.ExecuteForEmployeeAsync(companyId, employeeId);

            await Send.ResultAsync(TypedResults.Ok());
        }
        catch (Exception ex)
        {
            var logger = HttpContext.RequestServices.GetService<ILogger<DepartureFinaliserTestEndpoint>>();
            if (logger is not null)
            {
                logger.LogError(ex, "DepartureFinaliserTestEndpoint failed for employee {EmployeeId} in company {CompanyId}", employeeId, companyId);
            }

            await Send.ResultAsync(TypedResults.StatusCode(StatusCodes.Status500InternalServerError));
        }
    }
}
