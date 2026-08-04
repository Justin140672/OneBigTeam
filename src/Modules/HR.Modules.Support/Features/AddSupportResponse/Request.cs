using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.AddSupportResponse;

internal sealed record AddSupportResponseRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string BodyHtml { get; init; } = string.Empty;
    public IFormFileCollection? Files { get; init; }
}
