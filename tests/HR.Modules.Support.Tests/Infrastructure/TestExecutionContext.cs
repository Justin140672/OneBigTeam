using HR.SharedKernel.ExecutionContext;

namespace HR.Modules.Support.Tests.Infrastructure;

internal static class TestExecutionContext
{
    public static readonly IExecutionContextAccessor Accessor = new ExecutionContextAccessor();
}
