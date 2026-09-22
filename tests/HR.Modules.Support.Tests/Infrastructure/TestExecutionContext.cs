using HR.SharedKernel.ExecutionContext;

namespace HR.Modules.Support.Tests.Infrastructure;

/// <summary>Shared no-op IExecutionContextAccessor for handler tests that don't care about correlation IDs.</summary>
internal static class TestExecutionContext
{
    public static readonly IExecutionContextAccessor Accessor = new ExecutionContextAccessor();
}
