using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetCompensationImportTemplate;

internal sealed class Endpoint(GetCompensationImportTemplateHandler handler) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/compensation/import-template");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId = Route<Guid>("companyId");

        var bytes = await handler.GenerateAsync(companyId, cancellationToken);

        await Send.ResultAsync(TypedResults.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "compensation-import-template.xlsx"));
    }
}
