namespace HR.Modules.Employees.Domain;

internal enum EmploymentStatus
{
    Draft = 0,
    Active = 1,
    // 2 was OnLeave, retired — it duplicated the live, leave-request-derived "on leave today"
    // indicator (IEmployeeLeaveStatusReader, used by the direct-reports widget) with a second,
    // manually-set flag that had no connection to actual leave data and drifted out of sync.
    // Deliberately not reused so any stray old data or serialized value doesn't silently collide
    // with a new meaning.
    Suspended = 3,
    // 4 was Terminated, retired — deliberately not reused so any stray old data or serialized
    // value doesn't silently collide with a new meaning.
    Leaving = 5,
    FormerEmployee = 6,
}
