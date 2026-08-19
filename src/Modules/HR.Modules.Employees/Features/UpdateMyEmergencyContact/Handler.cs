using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.UpdateMyEmergencyContact;

internal sealed class UpdateMyEmergencyContactHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ICompanyContactValidationReader contactValidationReader)
{
    public async Task<Result<UpdateMyEmergencyContactResponse>> HandleAsync(
        UpdateMyEmergencyContactRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var contact = await dbContext.EmergencyContacts
            .SingleOrDefaultAsync(
                c => c.CompanyId == request.CompanyId &&
                     c.EmployeeId == employeeId &&
                     c.Id == request.ContactId,
                cancellationToken);

        if (contact is null)
            return Result.Failure<UpdateMyEmergencyContactResponse>(
                Error.NotFound("Emergency contact not found."));

        var contactRules = await contactValidationReader.GetContactValidationRulesAsync(request.CompanyId, cancellationToken);
        var phone = request.PhoneNumber.Trim();

        if (!Regex.IsMatch(phone, contactRules.MobileRegex, RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(phone, contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Result.Failure<UpdateMyEmergencyContactResponse>(Error.Validation($"'{phone}' is not a valid phone number."));

        var before = new EmergencyContactSnapshot(
            contact.Name, contact.Relationship, contact.PhoneNumber, contact.Email);

        var now = clock.UtcNowOffset();

        contact.Update(request.Name, request.Relationship, request.PhoneNumber, request.Email, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        var after = new EmergencyContactSnapshot(
            contact.Name, contact.Relationship, contact.PhoneNumber, contact.Email);

        await auditEventPublisher.PublishAsync(
            new EmergencyContactUpdatedAuditEvent(
                request.CompanyId, employeeId, employeeId, now, before, after),
            cancellationToken);

        return Result.Success(new UpdateMyEmergencyContactResponse(
            contact.Id, contact.Name, contact.Relationship, contact.PhoneNumber, contact.Email));
    }
}
