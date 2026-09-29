using System.Reflection;

namespace HR.Architecture.Tests;

public class ModuleDependencyBoundariesTests
{
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
            typeof(HR.Modules.Marketing.MarketingModule).Assembly,
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

    private static bool IsContractsAssembly(string? assemblyName) =>
        assemblyName is not null && assemblyName.EndsWith(".Contracts", StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Module_Does_Not_Reference_Other_Module_Implementations(Assembly moduleAssembly)
    {
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
