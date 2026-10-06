namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class PausePoint
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int armed = 1;

    public Task Entered => entered.Task;

    public void Release() => release.TrySetResult();

    public async Task PauseOnceAsync()
    {
        if (Interlocked.Exchange(ref armed, 0) == 0)
        {
            return;
        }

        entered.TrySetResult();
        await release.Task;
    }

    public Func<Task> AsHook() => PauseOnceAsync;
}
