namespace HR.Modules.Offboarding.Features.WaiveOffboardingTask;

internal sealed record WaiveOffboardingTaskRequest(
    Guid CompanyId,
    Guid OffboardingTaskId,
    string Reason);
