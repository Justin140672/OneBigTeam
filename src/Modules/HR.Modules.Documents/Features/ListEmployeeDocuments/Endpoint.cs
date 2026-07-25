using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListEmployeeDocuments;

internal sealed class Endpoint(ListEmployeeDocumentsHandler handler)
    : Endpoint<ListEmployeeDocumentsRequest, ListEmployeeDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        ListEmployeeDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
