namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Internal recruitment Ticket 7: progress of completing an internal application by changing the
/// existing employee's role. Null on an application means no appointment has been started.
/// Stored as its name ("Pending"/"Completed").
/// </summary>
internal enum InternalAppointmentStatus
{
    /// <summary>
    /// Recorded before the Employees module is asked to change the employee. An application left in
    /// this state (the request was interrupted) is completed by a retry or by
    /// InternalAppointmentReconciliationJob, whichever comes first.
    /// </summary>
    Pending,

    /// <summary>The employee change is recorded and the application is on the Hired stage.</summary>
    Completed,
}
