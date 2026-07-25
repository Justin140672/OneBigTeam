using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.ImportCompensationChanges;

internal sealed class ImportCompensationChangesRequest
{
    public Guid CompanyId { get; init; }
    public IFormFile File { get; init; } = null!;
}
