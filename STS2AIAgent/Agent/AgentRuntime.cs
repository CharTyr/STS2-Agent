using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Config;
using STS2AIAgent.Llm;
using STS2AIAgent.Multiplayer;

namespace STS2AIAgent.Agent;

internal sealed class AgentRuntime
{
    private const string LogPrefix = "[STS2AIAgent.Runtime]";

    private static readonly Lazy<AgentRuntime> LazyInstance = new(() => new AgentRuntime());

    private readonly object _gate = new();
    private readonly SettingsStore _store = new();
    private readonly List<ChatTurn> _history = new();
    private CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _playCts;
    private AgentSettings _settings;
    private readonly AgentLoop _loop;
    private bool _playRunning;
    private string _status = "就绪";
    private string _lastAction = "-";
    private string _lastThought = "-";
    private string _dualStatus = "尚未启动双开。";

    public static AgentRuntime Instance => LazyInstance.Value;

    public event Action? Changed;

    private AgentRuntime()
    {
        _settings = _store.Load();
        _loop = new AgentLoop(new GameBridge(), new DefaultLlmClientFactory(), () =>
        {
            lock (_gate)
            {
                return _settings;
            }
        });
    }

    public AgentSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public bool PlayRunning => _playRunning;

    public string Status => _status;

    public string LastAction => _lastAction;

    public string LastThought => _lastThought;

    public string DualStatus => _dualStatus;

    public string SettingsPath => _store.Path;

    public IReadOnlyList<ChatTurn> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }

    public void Initialize()
    {
        AppendLog($"API {Server.HttpServer.Instance.Prefix}  role={InstanceRole.Current}");
        if (InstanceRole.IsCompanion)
        {
            SetStatus("同伴实例：正在加入大厅");
            _ = Task.Run(() => CompanionEntryAsync(_lifetime.Token));
        }
    }

    public void Shutdown()
    {
        StopAutoPlay();
        try
        {
            _lifetime.Cancel();
        }
        catch
        {
        }
    }

    public void SaveSettings(AgentSettings settings)
    {
        settings.EnsureValidShape();
        lock (_gate)
        {
            _settings = settings;
        }

        _store.Save(settings);
        SetStatus("设置已保存");
        RaiseChanged();
    }

    public AgentSettings ReloadSettings()
    {
        var loaded = _store.Load();
        lock (_gate)
        {
            _settings = loaded;
        }

        RaiseChanged();
        return loaded;
    }

    public Task SendChatAsync(
        string text,
        bool attachState,
        bool attachScreenshot,
        bool allowAct,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => SendChatCoreAsync(text, attachState, attachScreenshot, allowAct, cancellationToken), cancellationToken);
    }

    public Task<string> TestConnectionAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => TestConnectionCoreAsync(cancellationToken), cancellationToken);
    }

    public void StartAutoPlay()
    {
        if (_playRunning)
        {
            return;
        }

        _playCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _playRunning = true;
        SetStatus("自动游玩中");
        _ = Task.Run(() => AutoPlayLoopAsync(_playCts.Token));
    }

    public void StopAutoPlay()
    {
        _playRunning = false;
        try
        {
            _playCts?.Cancel();
        }
        catch
        {
        }

        SetStatus("已暂停自动游玩");
    }

    public Task StepOnceAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => StepOnceCoreAsync(cancellationToken), cancellationToken);
    }

    public Task LaunchDualInstanceAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => LaunchDualInstanceCoreAsync(cancellationToken), cancellationToken);
    }

    public void ClearChat()
    {
        lock (_gate)
        {
            _history.Clear();
        }

        RaiseChanged();
    }

    private async Task SendChatCoreAsync(
        string text,
        bool attachState,
        bool attachScreenshot,
        bool allowAct,
        CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var prior = History;
        AddHistory("user", text);
        SetStatus("正在请求模型…");
        try
        {
            var result = await _loop.ChatAsync(
                text,
                prior,
                new ChatOptions
                {
                    AttachState = attachState,
                    AttachScreenshot = attachScreenshot,
                    AllowAct = allowAct
                },
                cancellationToken);

            var reply = result.Error != null
                ? result.Error
                : string.IsNullOrWhiteSpace(result.AssistantText) ? "(无文本回复)" : result.AssistantText;
            AddHistory("assistant", reply);
            _lastThought = result.Reasoning ?? reply;
            if (!string.IsNullOrWhiteSpace(result.Acted))
            {
                _lastAction = result.Acted;
            }

            SetStatus(result.Error == null ? "对话完成" : "对话出错");
        }
        catch (Exception ex)
        {
            AddHistory("assistant", "请求失败：" + ex.Message);
            SetStatus("对话失败");
        }
    }

    private async Task<string> TestConnectionCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus("正在测试连通…");
        try
        {
            var reply = await _loop.TestConnectionAsync(cancellationToken);
            SetStatus("连通正常");
            return reply;
        }
        catch (Exception ex)
        {
            SetStatus("连通失败");
            return ex.Message;
        }
    }

    private async Task StepOnceCoreAsync(CancellationToken cancellationToken)
    {
        SetStatus("单步决策中");
        try
        {
            var result = await _loop.PlayOnceAsync(cancellationToken);
            ApplyPlayResult(result);
        }
        catch (Exception ex)
        {
            SetStatus("单步失败：" + ex.Message);
        }
    }

    private async Task LaunchDualInstanceCoreAsync(CancellationToken cancellationToken)
    {
        if (InstanceRole.IsCompanion)
        {
            _dualStatus = "当前已是同伴实例。";
            RaiseChanged();
            return;
        }

        _dualStatus = "正在启动第二实例…";
        RaiseChanged();
        try
        {
            _dualStatus = await DualInstanceCoordinator.HostLocalCoopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _dualStatus = "双开失败：" + ex.Message;
        }

        RaiseChanged();
    }

    private async Task CompanionEntryAsync(CancellationToken cancellationToken)
    {
        var joined = await DualInstanceCoordinator.RunCompanionBootstrapAsync(cancellationToken);
        if (!joined)
        {
            SetStatus("同伴实例加入大厅失败");
            return;
        }
        var autoPlay = Environment.GetEnvironmentVariable("STS2_AGENT_AUTOPLAY");
        if (string.Equals(autoPlay, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(autoPlay, "true", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(autoPlay))
        {
            StartAutoPlay();
        }
    }

    private async Task AutoPlayLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _playRunning)
        {
            try
            {
                var result = await _loop.PlayOnceAsync(cancellationToken);
                ApplyPlayResult(result);
                if (result.Error != null)
                {
                    await Task.Delay(1200, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LogPrefix} Auto-play step failed: {ex.Message}");
                SetStatus("自动游玩出错：" + ex.Message);
                await Task.Delay(1500, cancellationToken);
            }
        }

        _playRunning = false;
        RaiseChanged();
    }

    private void ApplyPlayResult(AgentTurnResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Acted))
        {
            _lastAction = result.Acted;
        }

        _lastThought = result.Reasoning ?? result.AssistantText ?? _lastThought;
        if (!string.IsNullOrWhiteSpace(result.AssistantText))
        {
            AddHistory("assistant", result.AssistantText);
        }

        SetStatus(result.Error == null
            ? (result.Acted != null ? "已执行 " + result.Acted : "等待可操作状态")
            : result.Error);
    }

    private void AddHistory(string role, string text)
    {
        lock (_gate)
        {
            _history.Add(new ChatTurn { Role = role, Text = text });
            if (_history.Count > 80)
            {
                _history.RemoveRange(0, _history.Count - 80);
            }
        }

        RaiseChanged();
    }

    private void AppendLog(string line)
    {
        Log.Info($"{LogPrefix} {line}");
    }

    private void SetStatus(string status)
    {
        _status = status;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Changed?.Invoke();
    }
}
