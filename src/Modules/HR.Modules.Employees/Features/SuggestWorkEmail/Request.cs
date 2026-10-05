namespace HR.Modules.Employees.Features.SuggestWorkEmail;

internal sealed record SuggestWorkEmailRequest
{
    public Guid CompanyId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
