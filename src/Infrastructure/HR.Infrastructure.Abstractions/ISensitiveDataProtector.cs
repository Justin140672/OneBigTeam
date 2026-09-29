namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Ticket 1: application-level authenticated encryption for sensitive persisted values
/// (salary, bank details, National Insurance numbers, etc.).
///
/// Encryption and decryption happen in the application layer — before a value is written to
/// PostgreSQL and after it is read back — so that a database backup on its own never reveals
/// plaintext. Keys are supplied through environment/secret configuration and are never stored
/// in the application database.
///
/// The produced token is self-describing and carries a format version and a key id, so keys can
/// be rotated without a data migration: old values keep decrypting with their embedded key id
/// while new values are written with the active key.
/// </summary>
public interface ISensitiveDataProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);

    bool IsProtected(string? value);

    bool TryUnprotect(string? value, out string? plaintext);
}
