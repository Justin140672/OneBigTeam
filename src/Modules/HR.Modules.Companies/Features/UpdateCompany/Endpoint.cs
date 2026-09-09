using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateCompany;

internal sealed class Endpoint(
    UpdateCompanyHandler handler) : Endpoint<UpdateCompanyRequest, UpdateCompanyResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}");
        Policies("company:manage");
    }

    public override async Task HandleAsync(
        UpdateCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
