using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListEmployees;

/// <summary>
/// The read-only "pick a person" projection of the employee list, gated to "employee:read"
/// (Manager / Recruiter / HR Administrator). It returns the same shape as
/// <see cref="Endpoint"/> but is a distinct route so the full administration list at
/// <c>GET /api/companies/{companyId}/employees</c> can stay "employee:manage" (HR Administrator
/// only) per the ADM-05 administrative-role-separation matrix.
///
/// Used by dropdown/combobox pickers in HR.Web (hiring-manager selection, manager selection,
/// review-owner selection, report filters, …) — anywhere a non-HR-admin needs employee names
/// and ids but not the administration grid itself.
/// </summary>
internal sealed class ListSelectableEmployeesEndpoint(
    ListEmployeesHandler handler) : Endpoint<ListEmployeesRequest, ListEmployeesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/selectable");
        Policies("employee:read");
    }

    public override async Task HandleAsync(
        ListEmployeesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
