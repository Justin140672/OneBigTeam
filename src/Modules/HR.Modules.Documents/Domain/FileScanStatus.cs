namespace HR.Modules.Documents.Domain;

internal enum FileScanStatus
{
    Pending = 0,
    Scanning = 1,
    Clean = 2,
    Infected = 3,
    Failed = 4,
}
