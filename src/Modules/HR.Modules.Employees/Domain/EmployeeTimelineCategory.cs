namespace HR.Modules.Employees.Domain;

// The category is passed explicitly by whoever creates an EmployeeTimelineEntry rather than
// derived from EmployeeTimelineEventType via a hardcoded mapping table — simpler to reason about
// and avoids a second source of truth that would need to stay in sync with the event type enum.
internal enum EmployeeTimelineCategory
{
    Employment,
    Compensation,
    OnboardingAndProbation,
    Documents,
    Offboarding,
    HrNotes,
}
