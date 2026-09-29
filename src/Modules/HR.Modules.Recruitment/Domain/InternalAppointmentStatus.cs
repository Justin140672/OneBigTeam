namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Internal recruitment Ticket 7: progress of completing an internal application by changing the
/// existing employee's role. Null on an application means no appointment has been started.
/// Stored as its name ("Pending"/"Completed").
/// </summary>
internal enum InternalAppointmentStatus
{
    Pending,

    Completed,
}
