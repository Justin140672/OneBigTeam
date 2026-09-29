namespace HR.Modules.Documents.Domain;

internal interface IScannableFile
{
    string StorageKey { get; }
    string FileName { get; }
    Guid? EmployeeId { get; }
    FileScanStatus ScanStatus { get; }

    void MarkScanning(DateTimeOffset now);
    void MarkScanClean(DateTimeOffset now);
    void MarkScanInfected(string threatName, DateTimeOffset now);
    void MarkScanFailed(string reason, DateTimeOffset now);
}
