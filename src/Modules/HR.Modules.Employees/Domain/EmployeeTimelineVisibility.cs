namespace HR.Modules.Employees.Domain;

// Single source of truth for who may see a given EmployeeTimelineEntry. Wave 2/3 implementers
// (the writers that will populate this timeline from other features' domain events) MUST read
// this comment before adding new entries.
//
// Visibility tiers:
//   - HrOnly:           Only visible to viewers who satisfy the "employee:manage" HR policy.
//   - EmployeeAndHr:    Visible to HR AND to the employee themselves
//                       (viewer's employee id == entry's EmployeeId).
//   - AuthorisedInternal: Visible to HR, the employee themselves, AND the employee's direct
//                       manager (viewer's employee id == target employee's ManagerId).
//
// IMPORTANT rules for whoever writes entries in Wave 2/3:
//   1. HR notes must always be created with Visibility = HrOnly, and their stored Title/Summary
//      must NEVER contain the actual note text or category — always a generic phrase such as
//      "HR note added". The note content itself lives only in the notes feature, never here.
//   2. Compensation-change entries must NEVER have a dollar amount (or any other salary figure)
//      persisted into the stored Summary — this applies regardless of visibility tier, forever.
//      Amounts are never written into timeline text at all. A future "view compensation history"
//      link is the correct place to view amounts, not this feature.
internal enum EmployeeTimelineVisibility
{
    HrOnly,
    EmployeeAndHr,
    AuthorisedInternal,
}
