using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentRuntime
{
    private async Task SendChatCoreAsync(
        string text,
        bool attachState,
        bool attachScreenshot,
        CancellationToken cancellationToken)
    {
        text = text.Trim();
        if (text.Length == 0) return;

        if (PlayRunning)
        {
            await QueuePlayInstructionAsync(text, cancellationToken);
            return;
        }

        string? budgetBlocked;
        lock (_gate)
        {
            budgetBlocked = _budgetGuard.CheckBudget();
        }

        if (budgetBlocked != null)
        {
            AddHistory("user", text);
            AddHistory("assistant", budgetBlocked);
            return;
        }

        SetRequestingModelStatus();
        try
        {
            await _turnGate.WaitAsync(cancellationToken);
            AgentTurnResult result;
            try
            {
                await ObserveCurrentSessionAsync(cancellationToken);
                var prior = History;
                AddHistory("user", text);
                result = await _loop.ChatAsync(
                    text,
                    prior,
                    new ChatOptions
                    {
                        AttachState = attachState,
                        AttachScreenshot = attachScreenshot
                    },
                    cancellationToken,
                    ObserveSessionState);
                RecordTurnReceipt(result, recordBudget: true);
                var reply = result.Error != null
                    ? result.Error
                    : string.IsNullOrWhiteSpace(result.AssistantText) ? Loc.T("(无文本回复)") : result.AssistantText;
                AppendTurnTraces(result);
                AddHistory("assistant", reply);
                _lastThought = result.Reasoning ?? reply;
                if (!string.IsNullOrWhiteSpace(result.Acted)) _lastAction = result.Acted;
                SetStatus(result.Error == null ? Loc.T("对话完成") : Loc.T("对话出错"));
            }
            catch (AgentTurnCanceledException ex)
            {
                RecordTurnReceipt(ex.Receipt, recordBudget: true);
                throw;
            }
            catch (NonCombatOnlyYieldException ex)
            {
                if (ex.Receipt != null)
                {
                    RecordTurnReceipt(ex.Receipt, recordBudget: true);
                    AppendTurnTraces(ex.Receipt);
                }

                var waiting = Loc.T("非战斗模式已启用；Agent 保持只读并等待战斗结束。");
                AddHistory("assistant", waiting);
                _lastThought = waiting;
                SetStatus(Loc.T("非战斗模式已启用；等待战斗结束"));
            }
            finally
            {
                FlushSessionIfDirty();
                _turnGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(Loc.T("对话已取消"));
        }
        catch (Exception ex)
        {
            AddHistory("assistant", Loc.T("请求失败：{0}", ex.Message));
            SetStatus(Loc.T("对话失败"));
        }
        finally { ClearLiveThought(); } // No partial reasoning survives canceled or failed idle chat.
    }
}
