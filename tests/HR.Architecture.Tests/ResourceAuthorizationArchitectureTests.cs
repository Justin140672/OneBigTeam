using System.Reflection;
using HR.SharedKernel.Authorization;

namespace HR.Architecture.Tests;

/// <summary>
/// IAM-07: enforces that every module's per-resource-family authorizer (a class named
/// "*ResourceAuthorizer", e.g. DocumentResourceAuthorizer, LeaveResourceAuthorizer,
/// SicknessResourceAuthorizer, ProbationResourceAuthorizer, TasksResourceAuthorizer) is built on
/// the single shared HR.SharedKernel.Authorization.EmployeeResourceAuthorizer rather than
/// reimplementing the self/hierarchy/company-wide evaluation inline — and that every known
/// employee-owned-resource endpoint actually invokes one of these authorizers before reaching its
/// handler, rather than omitting the resource-level check entirely.
///
/// When a new employee-owned-resource endpoint is added (documents, tasks, sickness, probation,
/// leave, or a new resource family), add it to <see cref="KnownEmployeeResourceEndpoints"/> (or to
/// <see cref="SelfOnlyEndpointsExemptFromHierarchyCheck"/> if it is deliberately self-service-only
/// and therefore doesn't need a *ResourceAuthorizer — e.g. "submit my own leave request").
/// </summary>
public class ResourceAuthorizationArchitectureTests
{
    private static readonly Assembly[] ModuleAssembliesWithResourceAuthorizers =
        [
            typeof(HR.Modules.Documents.DocumentsModule).Assembly,
            typeof(HR.Modules.Leave.LeaveModule).Assembly,
            typeof(HR.Modules.Sickness.SicknessModule).Assembly,
            typeof(HR.Modules.Probation.ProbationModule).Assembly,
            typeof(HR.Modules.Tasks.TasksModule).Assembly,
        ];

    /// <summary>
    /// Every FastEndpoints Endpoint class that resolves an employee-owned resource by an
    /// employeeId (or a resource id whose owner is only known after a DB lookup) must take a
    /// dependency on an approved "*ResourceAuthorizer" type, either directly on the Endpoint's own
    /// constructor (target known from the route) or on the paired Handler's constructor (target
    /// only known after loading the entity — e.g. GetTask/CompleteTask).
    /// </summary>
    public static TheoryData<Type, Type> KnownEmployeeResourceEndpoints
    {
        get
        {
            var data = new TheoryData<Type, Type>();

            void Add<TEndpoint, TCheckedType>() => data.Add(typeof(TEndpoint), typeof(TCheckedType));

            // Documents — target employeeId known from the route; authorizer used at the endpoint.
            Add<HR.Modules.Documents.Features.ListEmployeeDocuments.Endpoint,
                HR.Modules.Documents.Features.ListEmployeeDocuments.Endpoint>();
            Add<HR.Modules.Documents.Features.GetEmployeeDocument.Endpoint,
                HR.Modules.Documents.Features.GetEmployeeDocument.Endpoint>();
            Add<HR.Modules.Documents.Features.DownloadEmployeeDocument.Endpoint,
                HR.Modules.Documents.Features.DownloadEmployeeDocument.Endpoint>();
            Add<HR.Modules.Documents.Features.DeleteEmployeeDocument.Endpoint,
                HR.Modules.Documents.Features.DeleteEmployeeDocument.Endpoint>();
            Add<HR.Modules.Documents.Features.UploadEmployeeDocumentVersion.Endpoint,
                HR.Modules.Documents.Features.UploadEmployeeDocumentVersion.Endpoint>();
            Add<HR.Modules.Documents.Features.GetArchivedEmployeeDocuments.Endpoint,
                HR.Modules.Documents.Features.GetArchivedEmployeeDocuments.Endpoint>();
            Add<HR.Modules.Documents.Features.SearchEmployeeDocuments.Endpoint,
                HR.Modules.Documents.Features.SearchEmployeeDocuments.Endpoint>();

            // Tasks — GetEmployeeTasks resolves target from the route (endpoint-level check);
            // GetTask/CompleteTask only know the assignee after loading the task, so the check is
            // on the paired handler instead.
            Add<HR.Modules.Tasks.Features.GetEmployeeTasks.Endpoint,
                HR.Modules.Tasks.Features.GetEmployeeTasks.Endpoint>();
            Add<HR.Modules.Tasks.Features.GetTask.Endpoint,
                HR.Modules.Tasks.Features.GetTask.GetTaskHandler>();
            Add<HR.Modules.Tasks.Features.CompleteTask.Endpoint,
                HR.Modules.Tasks.Features.CompleteTask.CompleteTaskHandler>();

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KnownEmployeeResourceEndpoints))]
    public void Employee_Resource_Endpoint_Invokes_An_Approved_Resource_Authorizer(
        Type endpointType, Type typeExpectedToDependOnAuthorizer)
    {
        var hasResourceAuthorizerDependency = GetConstructorParameterTypes(typeExpectedToDependOnAuthorizer)
            .Any(IsApprovedResourceAuthorizerType);

        Assert.True(
            hasResourceAuthorizerDependency,
            $"{endpointType.FullName} must invoke an approved *ResourceAuthorizer (via " +
            $"{typeExpectedToDependOnAuthorizer.FullName}'s constructor) before reaching its " +
            "handler logic — resource-level scope authorization must never be omitted for an " +
            "employee-owned-resource endpoint (IAM-07).");
    }

    /// <summary>
    /// Guards against a future module reintroducing its own duplicated self/hierarchy/company-wide
    /// evaluation logic instead of building on the shared abstraction: every "*ResourceAuthorizer"
    /// type discovered in a module assembly must itself depend on
    /// HR.SharedKernel.Authorization.EmployeeResourceAuthorizer.
    /// </summary>
    [Fact]
    public void Every_Module_ResourceAuthorizer_Builds_On_The_Shared_EmployeeResourceAuthorizer()
    {
        var resourceAuthorizerTypes = ModuleAssembliesWithResourceAuthorizers
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name.EndsWith("ResourceAuthorizer", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(resourceAuthorizerTypes);

        var violations = resourceAuthorizerTypes
            .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .All(f => f.FieldType != typeof(EmployeeResourceAuthorizer)))
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Every *ResourceAuthorizer type must delegate to HR.SharedKernel.Authorization." +
            $"EmployeeResourceAuthorizer rather than duplicating hierarchy logic. Violations: " +
            $"{string.Join(", ", violations)}");
    }

    private static bool IsApprovedResourceAuthorizerType(Type type) =>
        type.Name.EndsWith("ResourceAuthorizer", StringComparison.Ordinal);

    private static IEnumerable<Type> GetConstructorParameterTypes(Type type) =>
        type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);
}
