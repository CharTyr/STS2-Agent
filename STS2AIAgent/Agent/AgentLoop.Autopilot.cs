using System.Text.Json;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Agent;

/// <summary>
/// Forced moves without a model request (W2): the executor half of <see cref="AutopilotPolicy"/>.
/// </summary>
/// <remarks>
/// The chosen action runs through <see cref="ExecuteActAsync"/> -- the same legality check, index
/// validation, Sandpit and non-combat checks, and dispatch-time validation inside the execution lease
/// that a model's act gets -- so the policy only decides *that* a step is forced, never bypasses a
/// rail. A forced move the game refuses is remembered by state fingerprint and that frame goes to the
/// model instead, so a misjudged frame cannot loop. The no-progress guard still sees every forced move
/// (same action on the same frame three times stops the session).
/// </remarks>
internal sealed partial class AgentLoop
{
    private string? _autopilotRefusedFingerprint;

    private async Task<AgentTurnResult?> TryAutopilotAsync(
        string stateJson,
        bool playerInstructionQueued,
        CancellationToken token,
        Action<string>? checkState,
        Action<string>? reportPhase)
    {
        var settings = _settings();
        if (!settings.AutopilotEnabled)
        {
            return null;
        }

        var fingerprint = NoProgressPolicy.Fingerprint(stateJson);
        if (fingerprint != null && fingerprint == _autopilotRefusedFingerprint)
        {
            return null;
        }

        var decision = AutopilotPolicy.Decide(stateJson, settings.NonCombatOnlyEnabled, playerInstructionQueued);
        if (!decision.ShouldAct)
        {
            return null;
        }

        token.ThrowIfCancellationRequested();
        reportPhase?.Invoke(PlayPhases.ExecutingAction);
        var reason = ReasonFor(decision);
        var args = JsonSerializer.Serialize(new { action = decision.Action, option_index = decision.OptionIndex, reason });
        string? accepted = null;
        string? response = null;
        AgentTurnResult Receipt() => new()
        {
            Acted = accepted, ActResultJson = response, Reasoning = reason,
            ExecutedUnsettled = accepted != null, RequestsSpent = 0, Source = "autopilot"
        };

        (string? Action, string ResultJson, string? Fingerprint, bool Unsettled, string? Error) outcome;
        try
        {
            outcome = await ExecuteActAsync(args, token, checkState, (action, result) => { accepted = action; response = result; });
        }
        catch (NonCombatOnlyYieldException) { return WaitForCombat(Receipt()); }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            throw new AgentTurnCanceledException(Receipt(), ex, token);
        }
        catch (AutoPlayStoppedException ex)
        {
            ex.Receipt = Receipt();
            throw;
        }

        if (outcome.Error != null && accepted == null)
        {
            // Not dispatched: let the model have this frame rather than retrying the same guess.
            _autopilotRefusedFingerprint = fingerprint;
            return null;
        }

        _autopilotRefusedFingerprint = null;
        return new AgentTurnResult
        {
            Acted = outcome.Action ?? accepted, ActResultJson = outcome.ResultJson, Error = outcome.Error,
            Reasoning = reason, StateFingerprint = outcome.Fingerprint, ExecutedUnsettled = outcome.Unsettled,
            RequestsSpent = 0, Source = "autopilot"
        };
    }

    /// <summary>The one-line reason the decision log and the status show for a forced move.</summary>
    internal static string ReasonFor(AutopilotDecision decision) => decision.RuleId switch
    {
        "modal_ftue" => Loc.T("确认只有「确定」按钮的提示弹窗。"),
        "game_over_continue" => Loc.T("本局已结束，继续到结算。"),
        "combat_no_moves" => Loc.T("没有能打出的牌，也没有能用的药水，结束回合。"),
        "map_single_path" => Loc.T("地图上只有一条路可走。"),
        "reward_collect_all" => Loc.T("奖励里没有需要取舍的项目，全部领取并继续。"),
        "reward_claim_safe" => Loc.T("先领取金币或遗物；选卡留给模型。"),
        "event_single_option" => Loc.T("事件只剩一个可选项。"),
        "capstone_single_option" => Loc.T("只有一个可选项。"),
        "rest_single_option" => Loc.T("休息点只有一个可用选项。"),
        "chest_single_relic" => Loc.T("宝箱里只有一件遗物。"),
        "chest_open" => Loc.T("打开宝箱。"),
        _ => Loc.T("当前只有一个可执行的操作。")
    };
}
