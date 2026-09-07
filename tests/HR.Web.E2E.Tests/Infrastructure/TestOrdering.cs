using Xunit.Abstractions;
using Xunit.Sdk;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Marks a test method's relative execution order within a class decorated with
/// <c>[TestCaseOrderer(...PriorityOrderer...)]</c>. Lower values run first; methods without this
/// attribute default to priority 0 and run before any explicitly-prioritised (i.e. positive)
/// method, in declaration order among themselves.
///
/// xUnit does NOT guarantee declaration-order execution by default — a class-level "run last"
/// code comment is not enforced and silently breaks the moment xUnit (or a parallel run) picks a
/// different order. Use this attribute (plus <see cref="PriorityOrderer"/>) instead of relying on
/// method declaration order whenever a test class shares mutable seeded state across its tests.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}

/// <summary>
/// Orders test cases within a class by ascending <see cref="TestPriorityAttribute.Priority"/>,
/// falling back to declaration order for methods that share a priority (or have none). Apply via
/// <c>[TestCaseOrderer("HR.Web.E2E.Tests.Infrastructure.PriorityOrderer", "HR.Web.E2E.Tests")]</c>
/// on the test class.
/// </summary>
public sealed class PriorityOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var list = testCases.ToList();
        return list
            .Select((testCase, index) => (testCase, index, priority: GetPriority(testCase)))
            .OrderBy(t => t.priority)
            .ThenBy(t => t.index)
            .Select(t => t.testCase);
    }

    private static int GetPriority(ITestCase testCase)
    {
        var attr = testCase.TestMethod.Method
            .GetCustomAttributes(typeof(TestPriorityAttribute).AssemblyQualifiedName)
            .FirstOrDefault();

        return attr?.GetNamedArgument<int>(nameof(TestPriorityAttribute.Priority)) ?? 0;
    }
}
