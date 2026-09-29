using Xunit.Abstractions;
using Xunit.Sdk;

namespace HR.Web.E2E.Tests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}

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
