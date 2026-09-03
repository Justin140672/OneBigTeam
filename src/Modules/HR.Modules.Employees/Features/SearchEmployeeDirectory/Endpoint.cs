using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.SearchEmployeeDirectory;

internal sealed class Endpoint(
    SearchEmployeeDirectoryHandler handler) : Endpoint<SearchEmployeeDirectoryRequest, SearchEmployeeDirectoryResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/directory-search");
        // HR-only "find a person" quick search for the top-bar palette. Explicitly gated to
        // HR Administrators — Manager / Recruiter (who hold employee:read / employee:manage)
        // must not use this palette.
        Policies("role:hr-administrator");
    }

    public override async Task HandleAsync(
        SearchEmployeeDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
