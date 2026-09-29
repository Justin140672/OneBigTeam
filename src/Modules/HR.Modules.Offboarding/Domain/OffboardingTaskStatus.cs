namespace HR.Modules.Offboarding.Domain;

internal enum OffboardingTaskStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,

    Skipped = 4,

    Waived = 5,

    Cancelled = 6
}
