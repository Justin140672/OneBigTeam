using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCustomerDatabaseAssignment;

internal sealed class Endpoint(
    GetCustomerDatabaseAssignmentHandler handler) : EndpointWithoutRequest<GetCustomerDatabaseAssignmentResponse>
{
    public override void Configure()
    {
        Get("/api/platform-admin/companies/{companyId}/database-assignment");
        Policies("platform:admin");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var companyId = Route<Guid>("companyId");
        var result = await handler.HandleAsync(companyId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
