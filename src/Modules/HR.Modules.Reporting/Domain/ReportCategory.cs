namespace HR.Modules.Reporting.Domain;

internal enum ReportCategory
{
    Recruitment = 1,
    Hr = 2,

    // ADM-08: administrative governance reporting hub — user activity, administrative changes,
    // compliance status, security events. Surfaced in the report catalogue as "Administration".
    Administration = 3
}
