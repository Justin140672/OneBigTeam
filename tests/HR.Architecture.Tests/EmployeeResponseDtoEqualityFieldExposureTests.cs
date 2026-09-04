using System.Collections;
using System.Reflection;
using HR.Modules.Employees;

namespace HR.Architecture.Tests;

/// <summary>
/// Ticket 5: voluntary equality-monitoring answers are special-category data and must never be
/// surfaced by a general employee DTO. This guard reflects over every <c>*Response</c> record in the
/// Employees module assembly (and the nested item/row DTOs they expose) and fails if any property
/// name matches an equality field — UNLESS it belongs to one of the three intentional equality
/// features, which are the only place these values are allowed to leave the module.
/// </summary>
public class EmployeeResponseDtoEqualityFieldExposureTests
{
    private static readonly Assembly ModuleAssembly = typeof(EmployeesModule).Assembly;

    // Equality field names (from EqualityEnums.cs / EmployeeEqualityData.cs). Any response property
    // whose name equals one of these — or ends in "SelfDescribed" — is a leak.
    private static readonly string[] ForbiddenPropertyNames =
    [
        "GenderIdentity",
        "EthnicGroup",
        "DisabilityStatus",
        "DisabilityImpact",
        "SexualOrientation",
        "ReligionOrBelief",
        "MarriedOrCivilPartnershipStatus",
        "CaringResponsibilities",
    ];

    // The ONLY namespaces permitted to expose equality answers, by design:
    //  - GetMyEqualityData    : the employee reading back their own answers (self-service)
    //  - SaveMyEqualityData   : the employee submitting their own answers (self-service)
    //  - GetEqualityDiversityReport : anonymous aggregate counts/percentages only (no raw values)
    private static readonly string[] AllowedNamespaces =
    [
        "HR.Modules.Employees.Features.GetMyEqualityData",
        "HR.Modules.Employees.Features.SaveMyEqualityData",
        "HR.Modules.Employees.Features.GetEqualityDiversityReport",
    ];

    [Fact]
    public void No_General_Response_Dto_Exposes_Equality_Fields()
    {
        var responseTypes = ModuleAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true } && t.Name.EndsWith("Response", StringComparison.Ordinal));

        var violations = new List<string>();

        foreach (var responseType in responseTypes)
        {
            var visited = new HashSet<Type>();
            InspectType(responseType, responseType, visited, violations);
        }

        Assert.True(violations.Count == 0, "Equality fields leaked into response DTOs:\n" + string.Join("\n", violations));
    }

    private static void InspectType(Type root, Type type, HashSet<Type> visited, List<string> violations)
    {
        if (!visited.Add(type) || type.Namespace is null || !type.Namespace.StartsWith("HR.Modules.Employees", StringComparison.Ordinal))
            return;

        if (AllowedNamespaces.Any(ns => type.Namespace.StartsWith(ns, StringComparison.Ordinal)))
            return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (IsForbidden(prop.Name))
                violations.Add($"{root.FullName} -> {type.Name}.{prop.Name}");

            foreach (var nested in UnwrapTypes(prop.PropertyType))
                InspectType(root, nested, visited, violations);
        }
    }

    private static bool IsForbidden(string name)
        => name.EndsWith("SelfDescribed", StringComparison.Ordinal)
           || ForbiddenPropertyNames.Contains(name, StringComparer.Ordinal);

    private static IEnumerable<Type> UnwrapTypes(Type type)
    {
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
                foreach (var t in UnwrapTypes(arg))
                    yield return t;
            yield break;
        }

        if (type.IsArray && type.GetElementType() is { } element)
        {
            foreach (var t in UnwrapTypes(element))
                yield return t;
            yield break;
        }

        if (type is { IsClass: true } && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type))
            yield return type;
    }
}
