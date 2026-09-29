using FastEndpoints;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Features.DevActivateCompany;

internal sealed class Endpoint(
    ICompanyProvisioner companyProvisioner,
    IWebHostEnvironment environment,
    IOptions<DevToolsOptions> devToolsOptions) : Endpoint<DevActivateCompanyRequest>
{
    public override void Configure()
    {
        Post("/api/dev/activate-company");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DevActivateCompanyRequest request, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !devToolsOptions.Value.Enabled)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await companyProvisioner.ActivateCompanyAsync(request.CompanyId, cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}
