using HR.Infrastructure.Abstractions;

namespace HR.Modules.Offboarding.Features.GetOffboardingStatus;

internal sealed class GetOffboardingStatusHandler(IOffboardingStatusReader statusReader)
{
    public async Task<GetOffboardingStatusResponse> HandleAsync(
        GetOffboardingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var status = await statusReader.GetStatusAsync(request.CompanyId, request.EmployeeId, cancellationToken);

        return status is null
            ? new GetOffboardingStatusResponse(false, null)
            : new GetOffboardingStatusResponse(true, status.Status);
    }
}
