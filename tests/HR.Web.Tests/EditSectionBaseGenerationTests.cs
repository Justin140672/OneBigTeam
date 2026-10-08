using HR.Web.Components.Pages;

namespace HR.Web.Tests;

public class EditSectionBaseGenerationTests
{
    private sealed class TestModel
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class Section : EditSectionBase<TestModel>
    {
        public string Key { get; set; } = "";
        public Dictionary<string, Queue<TaskCompletionSource<string>>> Pending { get; } = new();
        public List<string> LoadCalls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public List<object?> SavedKeys { get; } = [];
        public Func<string, string>? OnLoad { get; set; }

        protected override object? LoadKey => Key;

        public string Value => Model.Value;
        public bool Loading => IsLoading;
        public bool Saveable => CanSave;
        public int? Version => LoadedVersion;

        public void Init() => OnInitialized();

        protected override void RequestRender() { }

        public Task Set(string key)
        {
            Key = key;
            return OnParametersSetAsync();
        }

        public TaskCompletionSource<string> Expect(string key)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!Pending.TryGetValue(key, out var q)) Pending[key] = q = new();
            q.Enqueue(tcs);
            return tcs;
        }

        protected override async Task<Action> LoadAsync(CancellationToken cancellationToken)
        {
            var key = Key;
            LoadCalls.Add(key);
            Tokens.Add(cancellationToken);
            var data = await Pending[key].Dequeue().Task;
            cancellationToken.ThrowIfCancellationRequested();
            return () =>
            {
                Model.Value = data;
                LoadedVersion = data.Length;
            };
        }

        protected override Task<string?> SaveCoreAsync()
        {
            SavedKeys.Add(LoadedKey);
            return Task.FromResult<string?>(null);
        }

        public Task<string?> Save() => SaveAsync();
    }

    private static Section Create()
    {
        var s = new Section();
        s.Init();
        return s;
    }

    [Fact]
    public async Task OlderLoadCompletingAfterNewerKeyLoad_DoesNotOverwriteState()
    {
        var s = Create();
        var a = s.Expect("A");
        var b = s.Expect("B");

        var loadA = s.Set("A");
        var loadB = s.Set("B");

        b.SetResult("data-B");
        await loadB;
        a.SetResult("data-A-stale");
        await loadA;

        Assert.Equal("data-B", s.Value);
        Assert.Equal("data-B".Length, s.Version);
        Assert.False(s.Loading);
        Assert.True(s.Saveable);
        Assert.False(s.HasUnsavedChanges);
        await s.Save();
        Assert.Equal(["B"], s.SavedKeys.Cast<string>());
    }

    [Fact]
    public async Task NewerLoadCompletingAfterOlder_OnlyNewerApplied_AndNothingSaveableMeanwhile()
    {
        var s = Create();
        var a = s.Expect("A");
        var b = s.Expect("B");

        var loadA = s.Set("A");
        var loadB = s.Set("B");

        a.SetResult("data-A");
        await loadA;

        Assert.Equal(string.Empty, s.Value);
        Assert.True(s.Loading);
        Assert.False(s.Saveable);
        Assert.NotNull(await s.Save());
        Assert.Empty(s.SavedKeys);

        b.SetResult("data-B");
        await loadB;

        Assert.Equal("data-B", s.Value);
        Assert.True(s.Saveable);
    }

    [Fact]
    public async Task SameKeyWhileInFlight_DoesNotStartSecondLoad()
    {
        var s = Create();
        var a = s.Expect("A");

        var first = s.Set("A");
        var second = s.Set("A");
        await second;

        a.SetResult("data-A");
        await first;

        Assert.Equal(["A"], s.LoadCalls);
        Assert.Equal("data-A", s.Value);
    }

    [Fact]
    public async Task SameKeyAfterLoaded_DoesNotReload()
    {
        var s = Create();
        s.Expect("A").SetResult("data-A");
        await s.Set("A");
        await s.Set("A");

        Assert.Single(s.LoadCalls);
    }

    [Fact]
    public async Task NewKeyCancelsPreviousLoadToken()
    {
        var s = Create();
        s.Expect("A");
        s.Expect("B");

        _ = s.Set("A");
        _ = s.Set("B");

        Assert.True(s.Tokens[0].IsCancellationRequested);
        Assert.False(s.Tokens[1].IsCancellationRequested);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Dispose_CancelsOutstandingLoad_AndLateCompletionIsIgnored()
    {
        var s = Create();
        var a = s.Expect("A");
        var load = s.Set("A");

        s.Dispose();
        Assert.True(s.Tokens[0].IsCancellationRequested);

        a.SetResult("data-A");
        await load;

        Assert.Equal(string.Empty, s.Value);
        Assert.False(s.Saveable);
    }

    [Fact]
    public async Task FailedCurrentLoad_CanBeRetriedSuccessfully()
    {
        var s = Create();
        var first = s.Expect("A");
        var load = s.Set("A");
        first.SetException(new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => load);

        Assert.False(s.Saveable);

        s.Expect("A").SetResult("data-A");
        await s.Set("A");

        Assert.Equal("data-A", s.Value);
        Assert.True(s.Saveable);
        Assert.Equal(2, s.LoadCalls.Count);
    }

    [Fact]
    public async Task CancelledStaleLoad_DoesNotBreakNewerLoad()
    {
        var s = Create();
        var a = s.Expect("A");
        var b = s.Expect("B");
        var loadA = s.Set("A");
        var loadB = s.Set("B");

        a.SetCanceled();
        await loadA;
        b.SetResult("data-B");
        await loadB;

        Assert.Equal("data-B", s.Value);
        Assert.True(s.Saveable);
    }

    [Fact]
    public async Task Save_TargetsKeyTheModelWasLoadedFrom()
    {
        var s = Create();
        s.Expect("A").SetResult("data-A");
        await s.Set("A");
        s.Expect("B");

        await s.Save();
        Assert.Equal(["A"], s.SavedKeys.Cast<string>());

        s.Key = "B";
        var error = await s.Save();
        Assert.NotNull(error);
        Assert.Single(s.SavedKeys);
    }
}
