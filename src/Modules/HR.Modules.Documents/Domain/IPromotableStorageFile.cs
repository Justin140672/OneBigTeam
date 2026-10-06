namespace HR.Modules.Documents.Domain;

internal interface IPromotableStorageFile
{
    void PromoteStorageKey(string storageKey, DateTimeOffset now);
}
