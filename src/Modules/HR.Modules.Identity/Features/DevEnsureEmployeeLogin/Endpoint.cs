using FastEndpoints;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HR.Modules.Identity.Features.DevEnsureEmployeeLogin;

internal sealed class Endpoint(
    IServiceProvider serviceProvider,
    IWebHostEnvironment environment,
    IOptions<DevToolsOptions> devToolsOptions) : Endpoint<DevEnsureEmployeeLoginRequest>
{
    public override void Configure()
    {
        Post("/api/dev/ensure-employee-login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DevEnsureEmployeeLoginRequest request, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !devToolsOptions.Value.Enabled)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await serviceProvider.EnsureDevSupabaseUserAsync(
            request.EmployeeId, request.CompanyId, request.Email,
            request.FirstName, request.LastName, cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}
