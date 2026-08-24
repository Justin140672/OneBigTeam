namespace HR.Modules.Employees.Domain;

internal enum CompensationChangeReason
{
    NewHire,
    AnnualReview,
    Promotion,
    MarketAdjustment,
    RoleChange,
    Correction,
    Other,

    // System-only reason set exclusively by employee import (EmployeeImportWriter). Deliberately
    // excluded from the regular Add Compensation reason dropdown/validation — see
    // CreateCompensationRecordValidator's Reason rule.
    DataImported
}
