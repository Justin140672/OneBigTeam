using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Features.GetCustomerDatabaseAssignment;

internal sealed record GetCustomerDatabaseAssignmentResponse(
    Guid CompanyId,
    CustomerDatabaseAssignmentStatus Status,
    string? DatabaseKey,
    uint? SchemaOid,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
