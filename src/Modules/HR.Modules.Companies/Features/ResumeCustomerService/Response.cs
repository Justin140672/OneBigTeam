namespace HR.Modules.Companies.Features.ResumeCustomerService;

internal sealed record ResumeCustomerServiceResponse(Guid CompanyId, bool AdminForcedReadOnly);
