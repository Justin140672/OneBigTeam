using System.Text.RegularExpressions;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed class AddMyEmergencyContactHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ICompanyContactValidationReader contactValidationReader)
{
    public async Task<Result<AddMyEmergencyContactResponse>> HandleAsync(
        AddMyEmergencyContactRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employeeExists = await dbContext.Employees
            .AnyAsync(e => e.CompanyId == request.CompanyId && e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result.Failure<AddMyEmergencyContactResponse>(
                Error.NotFound("No employee record is linked to this user."));

        var contactRules = await contactValidationReader.GetContactValidationRulesAsync(request.CompanyId, cancellationToken);
        var phone = request.PhoneNumber.Trim();

        if (!Regex.IsMatch(phone, contactRules.MobileRegex, RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(phone, contactRules.TelephoneRegex, RegexOptions.IgnoreCase))
            return Result.Failure<AddMyEmergencyContactResponse>(Error.Validation($"'{phone}' is not a valid phone number."));

        var now = clock.UtcNowOffset();

        var contact = EmergencyContact.Create(
            Guid.NewGuid(),
            employeeId,
            request.CompanyId,
            request.Name,
            request.Relationship,
            request.PhoneNumber,
            request.Email,
            now);

        dbContext.EmergencyContacts.Add(contact);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmergencyContactAddedAuditEvent(
                request.CompanyId, employeeId, employeeId, now, contact.Id, contact.Name, contact.Relationship),
            cancellationToken);

        return Result.Success(new AddMyEmergencyContactResponse(
            contact.Id, contact.Name, contact.Relationship, contact.PhoneNumber, contact.Email));
    }
}
