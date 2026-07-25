namespace HR.Modules.Employees.Domain;

/// <summary>
/// How a bulk compensation adjustment's proposed salary was derived. The API itself only ever
/// receives the final ProposedSalary per employee (already calculated client-side) — this value
/// is carried through purely for audit/traceability of which method HR used.
/// </summary>
internal enum CompensationAdjustmentMode
{
    PercentageIncrease,
    FixedAmountIncrease,
    SetDirectly
}
