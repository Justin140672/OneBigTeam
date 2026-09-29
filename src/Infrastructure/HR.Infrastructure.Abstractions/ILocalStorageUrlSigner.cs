namespace HR.Infrastructure.Abstractions;

public interface ILocalStorageUrlSigner
{
    Uri CreateSignedUrl(string baseUrl, string bucket, string storageKey);

    bool IsValid(string bucket, string storageKey, string? expires, string? signature);
}
