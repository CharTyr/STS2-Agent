using STS2AIAgent.Config;
using STS2AIAgent.Llm;

namespace STS2AIAgent.Agent;

/// <summary>
/// Offline host for the real ModelTest and CompanionSettings runtime partials. Only their external
/// dependencies (lifecycle entry, settings publication, notifications and unavailable game bridge)
/// are supplied here. The probe, synchronization and pause/apply/resume methods under test are
/// compiled directly from production sources, alongside the real AgentLoop and AutoPlaySession.
/// This does not compile or simulate Godot, the game's bootstrap, or the rest of AgentRuntime.
/// </summary>
internal sealed partial class AgentRuntime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly object _modeControlGate = new();
    private bool _companionReady = true;
    private bool _companionAutoStartSuppressed;
    public bool AutoStartSuppressed => _companionAutoStartSuppressed;
    public string Status { get; private set; } = "";
    private void SetStatus(string status) => Status = status;
    private AgentSettings _settings;
    private readonly AgentLoop _loop;
    private readonly AutoPlaySession _playSession = new();
    private readonly SemaphoreSlim _companionControlGate = new(1, 1);
    private readonly SettingsStore _store;
    private readonly string? _previousRole;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sts2-runtime-" + Guid.NewGuid().ToString("N"));

    internal AgentRuntime(AgentSettings settings, ILlmClientFactory factory)
    {
        _previousRole = Environment.GetEnvironmentVariable("STS2_AGENT_ROLE");
        Environment.SetEnvironmentVariable("STS2_AGENT_ROLE", InstanceRole.Companion);
        _settings = SettingsClone.Clone(settings);
        _store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        _loop = new AgentLoop(new NoGameBridge(), factory, () => Settings);
    }

    public AgentSettings Settings { get { lock (_gate) return _settings; } }
    public bool PlayRunning => _playSession.IsActive;
    public string PlayPhase => _playSession.Phase;
    public int Starts { get; private set; }
    public int Saves { get; private set; }
    public Action? BeforeSave { get; set; }
    public string LastJevChoice => "-";
    public string LastJevProbabilities => "-";
    public string LastJevDanger => "-";
    public string LastJevLatency => "-";

    public void SaveSettings(AgentSettings settings)
    {
        BeforeSave?.Invoke();
        _store.Save(settings);
        lock (_gate) _settings = settings;
        Saves++;
    }

    public AgentSettings ReadPersistedSettings() => _store.Load();
    public Task BeginLoop(Func<CancellationToken, Task> run)
    {
        var task = _playSession.TryStart(run, CancellationToken.None) ?? throw new InvalidOperationException("Already running");
        Starts++;
        return task;
    }
    public Task RequestPause() => _playSession.RequestPause();
    public void StartAutoPlay() => BeginLoop(token => Task.Delay(Timeout.InfiniteTimeSpan, token));
    private void ClearModelTestFailure() { }
    private void MarkFirstRunGuideSeen() => _settings.HasSeenFirstRunGuide = true;
    private void NoteEvent(string message) { }
    private void RaiseChanged() { }

    public async ValueTask DisposeAsync()
    {
        try { await _playSession.RequestPause().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception) { /* Faulted sessions are deliberately exercised by the regression tests. */ }
        finally
        {
            Environment.SetEnvironmentVariable("STS2_AGENT_ROLE", _previousRole);
            _companionControlGate.Dispose();
            _companionSettingsGate.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    private sealed class NoGameBridge : IGameBridge
    {
        private static Exception Unavailable() => new InvalidOperationException("The runtime harness must not access the game.");
        public Task<string> GetCompactStateJsonAsync(CancellationToken cancellationToken) => throw Unavailable();
        public Task<string> GetRawStateJsonAsync(CancellationToken cancellationToken) => throw Unavailable();
        public Task<string> GetAvailableActionsJsonAsync(CancellationToken cancellationToken) => throw Unavailable();
        public Task<string> GetActionSnapshotJsonAsync(CancellationToken cancellationToken) => throw Unavailable();
        public Task<string> GetScreenAsync(CancellationToken cancellationToken) => throw Unavailable();
        public Task<string> ActAsync(string action, int? cardIndex, int? targetIndex, int? optionIndex, int? x, int? y, string? tool, CancellationToken cancellationToken, bool rawState = false, Action<string>? validateState = null) => throw Unavailable();
        public Task<string> GetGameDataItemJsonAsync(string collection, string itemId, CancellationToken cancellationToken) => throw Unavailable();
        public Task<string> GetGameDataItemsJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => throw Unavailable();
        public Task<string> GetRelevantGameDataJsonAsync(string collection, IReadOnlyList<string> itemIds, CancellationToken cancellationToken) => throw Unavailable();
        public Task<bool> WaitUntilActionableAsync(TimeSpan timeout, CancellationToken cancellationToken) => throw Unavailable();
        public Task<byte[]?> CaptureScreenshotJpegAsync(CancellationToken cancellationToken) => throw Unavailable();
    }
}
