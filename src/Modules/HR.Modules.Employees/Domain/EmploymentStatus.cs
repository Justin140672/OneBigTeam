namespace HR.Modules.Employees.Domain;

internal enum EmploymentStatus
{
    Draft = 0,
    Active = 1,
    OnLeave = 2,
    Suspended = 3,
    // 4 was Terminated, retired — deliberately not reused so any stray old data or serialized
    // value doesn't silently collide with a new meaning.
    Leaving = 5,
    FormerEmployee = 6,
}
