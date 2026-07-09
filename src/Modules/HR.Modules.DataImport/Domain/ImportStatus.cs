namespace HR.Modules.DataImport.Domain;

internal enum ImportStatus
{
    Pending,
    Processing,
    Completed,
    CompletedWithErrors,
    Failed,
    Cancelled,
    Validated,
    Imported
}
