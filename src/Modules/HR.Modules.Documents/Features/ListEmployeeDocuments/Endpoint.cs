using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListEmployeeDocuments;

internal sealed class Endpoint(ListEmployeeDocumentsHandler handler)
    : Endpoint<ListEmployeeDocumentsRequest, ListEmployeeDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        ListEmployeeDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
