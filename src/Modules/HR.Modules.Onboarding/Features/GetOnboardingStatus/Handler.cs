using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Features.GetOnboardingStatus;

internal sealed class GetOnboardingStatusHandler(IOnboardingStatusReader statusReader)
{
    public async Task<GetOnboardingStatusResponse> HandleAsync(
        GetOnboardingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var status = await statusReader.GetStatusAsync(request.CompanyId, request.EmployeeId, cancellationToken);

        return status is null
            ? new GetOnboardingStatusResponse(false, null)
            : new GetOnboardingStatusResponse(true, status.Status);
    }
}
