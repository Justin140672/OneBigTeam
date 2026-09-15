using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCompany;

internal sealed class Endpoint(
    GetCompanyHandler handler) : Endpoint<GetCompanyRequest, GetCompanyResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetCompanyRequest request,
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
