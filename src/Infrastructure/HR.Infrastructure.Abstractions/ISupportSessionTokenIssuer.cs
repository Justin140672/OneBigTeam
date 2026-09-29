namespace HR.Infrastructure.Abstractions;

public interface ISupportSessionTokenIssuer
{
    string IssueToken(Guid supportSessionId, Guid companyId, Guid adminUserId, string adminEmail, DateTimeOffset expiresAt);
}
