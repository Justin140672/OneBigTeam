namespace HR.Modules.Companies.Features.GetEmployeeRenumberSideEffectStatus;

internal sealed record GetEmployeeRenumberSideEffectStatusRequest
{
    public Guid CompanyId { get; init; }
    public Guid OutboxMessageId { get; init; }
}
