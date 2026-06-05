using System.Reflection;

namespace HR.Architecture.Tests;

public class ModuleDependencyBoundariesTests
{
    public static TheoryData<Assembly> ModuleAssemblies =>
        [
            typeof(HR.Modules.Companies.CompaniesModule).Assembly,
            typeof(HR.Modules.Identity.IdentityModule).Assembly,
            typeof(HR.Modules.Employees.Class1).Assembly
        ];

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Module_Does_Not_Reference_Other_Modules(Assembly moduleAssembly)
    {
        var forbiddenReferences = moduleAssembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null &&
                reference.Name.StartsWith("HR.Modules.", StringComparison.Ordinal) &&
                !string.Equals(reference.Name, moduleAssembly.GetName().Name, StringComparison.Ordinal))
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"Module '{moduleAssembly.GetName().Name}' references other modules: {string.Join(", ", forbiddenReferences)}");
    }
}
