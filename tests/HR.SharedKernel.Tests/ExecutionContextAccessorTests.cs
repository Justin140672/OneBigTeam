using HR.SharedKernel.ExecutionContext;

namespace HR.SharedKernel.Tests;

/// <summary>
/// Ticket 23 (P2): the AsyncLocal-backed accessor must (a) start empty, (b) restore the previous
/// ambient value on Dispose, (c) support nesting, (d) flow across await points within the pushed
/// scope, and (e) NOT leak into a sibling async flow started outside the using block — the entire
/// point of choosing AsyncLocal over a plain static field.
/// </summary>
public class ExecutionContextAccessorTests
{
    [Fact]
    public void Current_Is_Null_Before_Any_Push()
    {
        var accessor = new ExecutionContextAccessor();

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Push_Establishes_Current()
    {
        var accessor = new ExecutionContextAccessor();
        var ctx = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        using (accessor.Push(ctx))
        {
            Assert.Same(ctx, accessor.Current);
        }
    }

    [Fact]
    public void Dispose_Restores_Previous_Value()
    {
        var accessor = new ExecutionContextAccessor();
        var ctx = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        using (accessor.Push(ctx))
        {
            Assert.Same(ctx, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Nested_Push_Restores_Outer_Context_After_Inner_Disposed()
    {
        var accessor = new ExecutionContextAccessor();
        var outer = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var inner = ExecutionContextInfo.CausedBy(outer, ExecutionOrigin.IntegrationEvent);

        using (accessor.Push(outer))
        {
            Assert.Same(outer, accessor.Current);

            using (accessor.Push(inner))
            {
                Assert.Same(inner, accessor.Current);
            }

            Assert.Same(outer, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Disposing_Twice_Is_A_Noop_And_Does_Not_Corrupt_Ambient_State()
    {
        var accessor = new ExecutionContextAccessor();
        var outer = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var inner = ExecutionContextInfo.CausedBy(outer, ExecutionOrigin.IntegrationEvent);

        using (accessor.Push(outer))
        {
            var innerScope = accessor.Push(inner);
            innerScope.Dispose();
            Assert.Same(outer, accessor.Current);

            // A second Dispose() must not re-apply "previous" a second time and clobber outer's
            // restoration with whatever happened to be ambient in between.
            innerScope.Dispose();
            Assert.Same(outer, accessor.Current);
        }
    }

    [Fact]
    public async Task Pushed_Context_Flows_Across_An_Await_Within_The_Scope()
    {
        var accessor = new ExecutionContextAccessor();
        var ctx = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        using (accessor.Push(ctx))
        {
            await Task.Delay(1);
            await Task.Yield();

            Assert.Same(ctx, accessor.Current);
        }
    }

    [Fact]
    public async Task Pushed_Context_Does_Not_Leak_Into_A_Sibling_Task_Run_Started_Outside_The_Scope()
    {
        var accessor = new ExecutionContextAccessor();
        var ctx = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        using (accessor.Push(ctx))
        {
            // Intentionally does nothing with ctx — just establishes it as ambient on this flow.
        }

        IExecutionContext? observedInSibling = null;
        await Task.Run(() =>
        {
            observedInSibling = accessor.Current;
        });

        Assert.Null(observedInSibling);
    }

    [Fact]
    public async Task Concurrent_Async_Flows_Each_See_Their_Own_Pushed_Context_Not_Each_Others()
    {
        var accessor = new ExecutionContextAccessor();
        var ctxA = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var ctxB = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        async Task<IExecutionContext?> RunWithAsync(IExecutionContext ctx)
        {
            using (accessor.Push(ctx))
            {
                await Task.Delay(10);
                return accessor.Current;
            }
        }

        var taskA = RunWithAsync(ctxA);
        var taskB = RunWithAsync(ctxB);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Same(ctxA, results[0]);
        Assert.Same(ctxB, results[1]);
    }
}
