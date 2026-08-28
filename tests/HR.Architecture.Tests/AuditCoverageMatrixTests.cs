using HR.SharedKernel;
using System.Reflection;

namespace HR.Architecture.Tests;

/// <summary>
/// AUD-08: Enforces the audit coverage matrix across all business modules.
///
/// Two invariants:
/// 1. Naming convention — every IAuditEvent implementation must be a record whose name ends in
///    "AuditEvent".  This keeps audit types discoverable and distinguishable from domain events,
///    integration events and other record types.
///
/// 2. Coverage completeness — every module assembly that contains mutation handlers (types whose
///    names end in "Handler" and are NOT read-only by naming convention: not starting with "Get",
///    "List", "Search", or "Query") must also declare at least one IAuditEvent implementation.
///    This does not prove that every individual handler publishes an event, but it does guarantee
///    that the module author has at minimum thought about auditing and created an audit file.
///    Per-handler coverage is enforced by code-review process; a full handler-level test would
///    require reflection into DI registrations which are too brittle for an architecture test.
/// </summary>
public class AuditCoverageMatrixTests
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
    ];

    /// <summary>
    /// AUD-08 — naming convention: every IAuditEvent implementation must have a name that ends
    /// in "AuditEvent" so that audit records are trivially discoverable via tooling and grep.
    /// </summary>
    [Fact]
    public void All_IAuditEvent_Implementations_Have_Names_Ending_In_AuditEvent()
    {
        var violations = ModuleAssemblies
            .Concat([typeof(IAuditEvent).Assembly])
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IAuditEvent).IsAssignableFrom(t))
            .Where(t => !t.Name.EndsWith("AuditEvent", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"The following IAuditEvent implementations do not follow the '*AuditEvent' naming convention:\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// AUD-08 — coverage completeness: every module assembly that has mutation handlers must also
    /// declare at least one IAuditEvent.  A mutation handler is any type whose name ends in
    /// "Handler" and whose name does NOT start with a read-only verb (Get, List, Search, Query).
    /// </summary>
    [Fact]
    public void Every_Module_With_Mutation_Handlers_Declares_At_Least_One_AuditEvent()
    {
        var violations = new List<string>();

        foreach (var assembly in ModuleAssemblies)
        {
            var types = assembly.GetTypes();

            var hasMutationHandlers = types.Any(t =>
                t.Name.EndsWith("Handler", StringComparison.Ordinal) &&
                !t.Name.StartsWith("Get", StringComparison.Ordinal) &&
                !t.Name.StartsWith("List", StringComparison.Ordinal) &&
                !t.Name.StartsWith("Search", StringComparison.Ordinal) &&
                !t.Name.StartsWith("Query", StringComparison.Ordinal));

            if (!hasMutationHandlers)
                continue;

            var hasAuditEvents = types.Any(t =>
                !t.IsAbstract && !t.IsInterface && typeof(IAuditEvent).IsAssignableFrom(t));

            if (!hasAuditEvents)
            {
                violations.Add(assembly.GetName().Name!);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"The following module assemblies have mutation handlers but declare no IAuditEvent implementations:\n" +
            string.Join("\n", violations) +
            "\nAdd an *Audit.cs file and at least one IAuditEvent record.");
    }
}
