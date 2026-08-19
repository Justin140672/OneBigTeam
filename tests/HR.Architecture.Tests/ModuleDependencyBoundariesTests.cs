using System.Reflection;

namespace HR.Architecture.Tests;

public class ModuleDependencyBoundariesTests
{
    /// <summary>
    /// Single source of truth for the known module assemblies. Both ModuleAssemblies (Rule 1) and
    /// ContractsAssemblies (Rules 2-5, discovered by scanning these modules' references) are
    /// derived from this array.
    /// </summary>
    private static readonly Assembly[] KnownModuleAssemblies =
        [
            typeof(HR.Modules.Companies.CompaniesModule).Assembly,
            typeof(HR.Modules.CompanyOnboarding.CompanyOnboardingModule).Assembly,
            typeof(HR.Modules.DataImport.DataImportModule).Assembly,
            typeof(HR.Modules.Identity.IdentityModule).Assembly,
            typeof(HR.Modules.Employees.EmployeesModule).Assembly,
            typeof(HR.Modules.Leave.LeaveModule).Assembly,
            typeof(HR.Modules.Documents.DocumentsModule).Assembly,
            typeof(HR.Modules.Tasks.TasksModule).Assembly,
            typeof(HR.Modules.Notifications.NotificationsModule).Assembly,
            typeof(HR.Modules.Probation.ProbationModule).Assembly,
            typeof(HR.Modules.Reporting.ReportingModule).Assembly,
            typeof(HR.Modules.Recruitment.RecruitmentModule).Assembly,
            typeof(HR.Modules.Assets.AssetsModule).Assembly,
            typeof(HR.Modules.Sickness.SicknessModule).Assembly,
            typeof(HR.Modules.Onboarding.OnboardingModule).Assembly,
            typeof(HR.Modules.Offboarding.OffboardingModule).Assembly,
            typeof(HR.Modules.Support.SupportModule).Assembly,
        ];

    public static TheoryData<Assembly> ModuleAssemblies
    {
        get
        {
            var data = new TheoryData<Assembly>();
            foreach (var assembly in KnownModuleAssemblies)
            {
                data.Add(assembly);
            }

            return data;
        }
    }

    /// <summary>
    /// Contracts assemblies (e.g. HR.Modules.Tasks.Contracts) are the sanctioned exception to the
    /// "no module-to-module references" rule: they hold only the cross-module interfaces/DTOs a
    /// module explicitly chooses to expose, never implementation. Any assembly whose name ends in
    /// ".Contracts" is exempt from being treated as a forbidden implementation reference.
    /// </summary>
    private static bool IsContractsAssembly(string? assemblyName) =>
        assemblyName is not null && assemblyName.EndsWith(".Contracts", StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Module_Does_Not_Reference_Other_Module_Implementations(Assembly moduleAssembly)
    {
        // Rule 1 + 2: module-to-module references are forbidden, except references to another
        // module's *.Contracts assembly, which is the explicitly sanctioned cross-module surface.
        var forbiddenReferences = moduleAssembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null &&
                reference.Name.StartsWith("HR.Modules.", StringComparison.Ordinal) &&
                !string.Equals(reference.Name, moduleAssembly.GetName().Name, StringComparison.Ordinal) &&
                !IsContractsAssembly(reference.Name))
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"Module '{moduleAssembly.GetName().Name}' references other module implementations: {string.Join(", ", forbiddenReferences)}");
    }

    /// <summary>
    /// Discovers every "*.Contracts" assembly transitively referenced by any known module
    /// assembly, instead of hard-coding a fixed list. This means Rules 2-5 below automatically
    /// cover any future module's Contracts project (e.g. HR.Modules.Foo.Contracts) as soon as
    /// some module references it — no test-file update required when a new module is added.
    /// </summary>
    public static TheoryData<Assembly> ContractsAssemblies
    {
        get
        {
            var discovered = new Dictionary<string, Assembly>(StringComparer.Ordinal);

            foreach (var moduleAssembly in KnownModuleAssemblies)
            {
                foreach (var reference in moduleAssembly.GetReferencedAssemblies())
                {
                    if (reference.Name is null || !IsContractsAssembly(reference.Name))
                    {
                        continue;
                    }

                    if (discovered.ContainsKey(reference.Name))
                    {
                        continue;
                    }

                    discovered[reference.Name] = Assembly.Load(reference);
                }
            }

            var data = new TheoryData<Assembly>();
            foreach (var assembly in discovered.Values)
            {
                data.Add(assembly);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ContractsAssemblies))]
    public void Contracts_Assembly_Does_Not_Reference_Any_Module_Implementation(Assembly contractsAssembly)
    {
        // Rule 3: a Contracts project must never depend back on any module's implementation
        // (including its own owning module), or it stops being a safe, dependency-free surface
        // that any other module can reference.
        var forbiddenReferences = contractsAssembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null &&
                reference.Name.StartsWith("HR.Modules.", StringComparison.Ordinal) &&
                !IsContractsAssembly(reference.Name))
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"Contracts assembly '{contractsAssembly.GetName().Name}' references module implementations: {string.Join(", ", forbiddenReferences)}");
    }

    [Theory]
    [MemberData(nameof(ContractsAssemblies))]
    public void Contracts_Assembly_Does_Not_Reference_Other_Contracts_Assemblies(Assembly contractsAssembly)
    {
        // Rule 4: prevent circular/coupled contracts (Tasks.Contracts -> Leave.Contracts, etc).
        // Each Contracts assembly must stand alone so consuming modules never pull in a
        // transitive graph of unrelated contracts.
        var otherContractsReferences = contractsAssembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null &&
                IsContractsAssembly(reference.Name) &&
                !string.Equals(reference.Name, contractsAssembly.GetName().Name, StringComparison.Ordinal))
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.True(
            otherContractsReferences.Length == 0,
            $"Contracts assembly '{contractsAssembly.GetName().Name}' references other contracts assemblies: {string.Join(", ", otherContractsReferences)}");
    }

    [Theory]
    [MemberData(nameof(ContractsAssemblies))]
    public void Contracts_Assembly_Avoids_Implementation_Framework_Dependencies(Assembly contractsAssembly)
    {
        // Rule 5: Contracts assemblies must stay pure interfaces/DTOs/enums — no EF Core,
        // FastEndpoints, Blazor or other infrastructure packages, so any module (including
        // eventually HR.Web) can reference them without dragging in persistence or web frameworks.
        var forbiddenPrefixes = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "FastEndpoints",
            "Microsoft.AspNetCore",
            "Microsoft.FluentUI",
            "Syncfusion",
            "Hangfire",
        };

        var forbiddenReferences = contractsAssembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null &&
                forbiddenPrefixes.Any(prefix => reference.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"Contracts assembly '{contractsAssembly.GetName().Name}' references infrastructure/framework packages: {string.Join(", ", forbiddenReferences)}");
    }
}
