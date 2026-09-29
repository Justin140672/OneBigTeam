using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using System.Reflection;
using System.Text.Json;

namespace HR.Architecture.Tests;

/// <summary>
/// AUD-03: scans every concrete IAuditEvent implementation registered across all modules
/// and verifies that no Before/After/Metadata payload contains prohibited sensitive fields.
///
/// This test catches payloads that would be rejected at runtime by AuditPayloadRedactionGuard,
/// making violations visible during the build cycle rather than in production.
/// </summary>
public class AuditPayloadRedactionTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(HR.Modules.Employees.EmployeesModule).Assembly,
        typeof(HR.Modules.Identity.IdentityModule).Assembly,
        typeof(HR.Modules.Companies.CompaniesModule).Assembly,
        typeof(HR.Modules.Leave.LeaveModule).Assembly,
        typeof(HR.Modules.Documents.DocumentsModule).Assembly,
        typeof(HR.Modules.Tasks.TasksModule).Assembly,
        typeof(HR.Modules.Onboarding.OnboardingModule).Assembly,
        typeof(HR.Modules.Offboarding.OffboardingModule).Assembly,
        typeof(HR.Modules.Probation.ProbationModule).Assembly,
        typeof(HR.Modules.Recruitment.RecruitmentModule).Assembly,
        typeof(HR.Modules.Sickness.SicknessModule).Assembly,
        typeof(HR.Modules.Assets.AssetsModule).Assembly,
        typeof(HR.Modules.Notifications.NotificationsModule).Assembly,
        typeof(HR.Modules.Reporting.ReportingModule).Assembly,
        typeof(HR.SharedKernel.IAuditEvent).Assembly,
    ];

    [Fact]
    public void No_IAuditEvent_Implementation_Contains_Prohibited_Fields_In_Before_Or_After()
    {
        var violations = new List<string>();

        var auditEventTypes = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IAuditEvent).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(auditEventTypes);

        foreach (var type in auditEventTypes)
        {
            var instance = TryCreateInstance(type);
            if (instance is not IAuditEvent evt)
                continue;

            CheckPayload(evt.Before,    type, "Before",   violations);
            CheckPayload(evt.After,     type, "After",    violations);
            CheckPayload(evt.Metadata,  type, "Metadata", violations);
        }

        Assert.Empty(violations);
    }

    private static void CheckPayload(object? payload, Type eventType, string fieldName, List<string> violations)
    {
        if (payload is null)
            return;

        try
        {
            var json = JsonSerializer.Serialize(payload);
            AuditPayloadRedactionGuard.AssertPayloadIsSafe(json, fieldName);
        }
        catch (ProhibitedAuditFieldException ex)
        {
            violations.Add($"{eventType.FullName}.{fieldName}: {ex.Message}");
        }
    }

    private static IAuditEvent? TryCreateInstance(Type type)
    {
        try
        {
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (ctor is null)
                return null;

            var args = ctor.GetParameters()
                .Select(p => GetDefaultValue(p.ParameterType))
                .ToArray();

            return ctor.Invoke(args) as IAuditEvent;
        }
        catch
        {
            return null;
        }
    }

    private static object? GetDefaultValue(Type type)
    {
        if (type == typeof(string))   return string.Empty;
        if (type == typeof(Guid))     return Guid.Empty;
        if (type == typeof(Guid?))    return (Guid?)null;
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.MinValue;
        if (type == typeof(DateTimeOffset?)) return (DateTimeOffset?)null;
        if (type == typeof(DateOnly)) return DateOnly.MinValue;
        if (type == typeof(DateOnly?)) return (DateOnly?)null;
        if (type == typeof(bool))     return false;
        if (type == typeof(bool?))    return (bool?)null;
        if (type == typeof(int))      return 0;
        if (type == typeof(decimal))  return 0m;
        if (type == typeof(decimal?)) return (decimal?)null;
        if (type.IsValueType)         return Activator.CreateInstance(type);
        return null;
    }
}
