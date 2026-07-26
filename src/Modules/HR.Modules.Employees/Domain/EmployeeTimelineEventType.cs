namespace HR.Modules.Employees.Domain;

internal enum EmployeeTimelineEventType
{
    EmployeeJoined,
    EmployeeDetailsCorrected,
    ManagerChanged,
    LocationChanged,
    PositionChanged,
    EmployeePromoted,
    CompensationChanged,
    OnboardingCompleted,
    ProbationPassed,
    CompanyDocumentAcknowledged,
    EmployeeDocumentAdded,
    HrNoteAdded,
    OffboardingStarted,
    EmploymentEnded,
}
