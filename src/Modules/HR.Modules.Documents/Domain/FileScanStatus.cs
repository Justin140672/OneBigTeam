namespace HR.Modules.Documents.Domain;

/// <summary>
/// Virus-scan lifecycle for an uploaded file. New uploads always start at Pending; a Hangfire job
/// (see Jobs/ScanUploadedFileJob.cs) transitions them to Clean or Infected, or to Failed once
/// Hangfire's retries are exhausted for a scanner that could not be reached.
/// </summary>
internal enum FileScanStatus
{
    Pending = 0,
    Scanning = 1,
    Clean = 2,
    Infected = 3,
    Failed = 4,
}
