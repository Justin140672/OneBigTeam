using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed class Endpoint(GetMyNotificationsHandler handler)
    : Endpoint<GetMyNotificationsRequest, GetMyNotificationsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/notifications/my");
        Policies("authenticated");
    }

    public override async Task HandleAsync(GetMyNotificationsRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var employeeId))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            new GetMyNotificationsRequest { CompanyId = request.CompanyId, EmployeeId = employeeId },
            cancellationToken);

        await SendAsync(result, StatusCodes.Status200OK, cancellationToken);
    }
}
