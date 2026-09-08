using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ListDirectoryEmployees;

/// <summary>
/// The employee-facing "Employee Directory" list — any authenticated employee ("role:employee")
/// can browse active colleagues' names, job titles, department/location and work email. It
/// deliberately exposes far less than the HR administration list at
/// <c>GET /api/companies/{companyId}/employees</c> (no employee number, manager, account status,
/// salary or personal/contact fields).
/// </summary>
internal sealed class Endpoint(
    ListDirectoryEmployeesHandler handler) : Endpoint<ListDirectoryEmployeesRequest, ListDirectoryEmployeesResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/directory");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListDirectoryEmployeesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
