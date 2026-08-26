namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

internal sealed record RetryEmployeeRenumberSideEffectRequest
{
    public Guid CompanyId { get; init; }
    public Guid OutboxMessageId { get; init; }
}
