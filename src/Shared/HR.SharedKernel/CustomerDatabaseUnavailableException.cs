namespace HR.SharedKernel;

public sealed class CustomerDatabaseUnavailableException : InvalidOperationException
{
    public Guid CompanyId { get; }

    public override string Message { get; }

    public CustomerDatabaseUnavailableException(Guid companyId, string message)
        : base(message)
    {
        CompanyId = companyId;
        Message = message;
    }
}
