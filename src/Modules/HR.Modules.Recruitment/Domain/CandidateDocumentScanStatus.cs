namespace HR.Modules.Recruitment.Domain;

internal enum CandidateDocumentScanStatus
{
    Pending = 0,

    Scanning = 1,

    Clean = 2,

    Infected = 3,

    Failed = 4,
}
