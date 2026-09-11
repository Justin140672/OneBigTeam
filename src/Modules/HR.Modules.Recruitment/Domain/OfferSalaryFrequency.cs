namespace HR.Modules.Recruitment.Domain;

// Ticket 2: the frequency/basis the offered salary figure is expressed in. Mirrors the Employees
// module's SalaryType (Annual/Hourly/Daily) by convention — Recruitment has no reference to
// Employees, and the Position Profile employment defaults surface the same concept as a string over
// the IPositionProfileReader contract. Kept as a distinct recruitment-local enum so an offer can
// record the agreed basis independently of the role's default.
internal enum OfferSalaryFrequency
{
    Annual,
    Hourly,
    Daily
}
