using System.Text.Json;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>Short-lived, run-scoped player guidance for the next model decision.</summary>
/// <remarks>
/// This is not an action queue: a message cannot run PlayIntent or trigger an additional move.
/// The model loop peeks without consuming it and acknowledges by immutable id immediately before
/// its first primary provider request. Pauses and cancellation before dispatch retain the message.
/// </remarks>
internal sealed partial class AgentRuntime
{
    private const int MaxPendingPlayInstructions = 4;
    private const int MaxPlayInstructionChars = 1000;
    private readonly Queue<PendingPlayInstruction> _pendingPlayInstructions = new();

    private readonly record struct PendingPlayInstruction(string Id, string RunId, string Text);

    private async Task QueuePlayInstructionAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            // A running loop can be preparing a new run; do not bind guidance to the previous
            // auto-play boundary or to an unknown/menu identity.
            await ObserveCurrentSessionAsync(cancellationToken);
            string response;
            lock (_gate)
            {
                if (text.Length > MaxPlayInstructionChars)
                {
                    response = Loc.T("游玩指令太长，请缩短后重试。");
                }
                else if (!PlaySessionStore.IsPersistable(_sessionRunId))
                {
                    response = Loc.T("尚未确认当前对局，请稍后重试游玩指令。");
                }
                else if (_pendingPlayInstructions.Count >= MaxPendingPlayInstructions)
                {
                    response = Loc.T("待处理游玩指令已满，请等待下一次决策。");
                }
                else
                {
                    _pendingPlayInstructions.Enqueue(new PendingPlayInstruction(
                        Guid.NewGuid().ToString("N"), _sessionRunId!, text));
                    response = Loc.T("已收到游玩指令，将在同一局的下一次决策中生效。");
                }

                // AddHistoryCore reenters the same monitor; keep receipt and run binding atomic
                // so a concurrent confirmed session switch cannot move this text to another run.
                AddHistoryCore("user", text, notify: false);
                AddHistoryCore("assistant", response, notify: false);
            }

            FlushSessionIfDirty();
            RaiseChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(Loc.T("对话已取消"));
        }
        catch (Exception ex)
        {
            AddHistory("assistant", Loc.T("请求失败：{0}", DiagnosticExport.Redact(ex.Message)));
            SetStatus(Loc.T("对话失败"));
        }
    }

    private (string Id, string Text)? PeekPlayInstruction(string stateJson)
    {
        string? runId;
        try
        {
            using var document = JsonDocument.Parse(stateJson);
            var root = document.RootElement;
            runId = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("run_id", out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (JsonException) { return null; }

        lock (_gate)
        {
            if (!PlaySessionStore.IsPersistable(runId) || !string.Equals(runId, _sessionRunId, StringComparison.Ordinal)
                || _pendingPlayInstructions.Count == 0) return null;
            var next = _pendingPlayInstructions.Peek();
            return next.RunId == runId ? (next.Id, next.Text) : null;
        }
    }

    private void AcknowledgePlayInstruction(string id)
    {
        lock (_gate)
        {
            if (_pendingPlayInstructions.Count > 0 && _pendingPlayInstructions.Peek().Id == id)
                _pendingPlayInstructions.Dequeue();
        }
    }

    /// <summary>Called while the runtime session lock is held, after a real run change is confirmed.</summary>
    private void DropPlayInstructionsForRun(string? runId)
    {
        lock (_gate)
        {
            if (_pendingPlayInstructions.Count > 0
                && !string.Equals(_pendingPlayInstructions.Peek().RunId, runId, StringComparison.Ordinal))
                _pendingPlayInstructions.Clear();
        }
    }
}
