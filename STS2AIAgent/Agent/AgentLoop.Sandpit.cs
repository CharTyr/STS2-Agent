using System.Text.Json;
using STS2AIAgent.Localization;
using STS2AIAgent.Server;

namespace STS2AIAgent.Agent;

internal sealed partial class AgentLoop
{
    private async Task<AgentTurnResult?> TryEscapeSandpitAsync(
        string stateJson, CancellationToken token, Action<string>? checkState, Action<string>? reportPhase)
    {
        var safety = SandpitSafetyPolicy.Read(stateJson);
        if (!safety.Critical || safety.EscapeIndex is not int index) return null;
        token.ThrowIfCancellationRequested();
        reportPhase?.Invoke(PlayPhases.ExecutingAction);
        var reason = Loc.T("沙坑计数即将归零，优先打出狂乱逃离，避免下次敌方回合开始时被吞噬。");
        var args = JsonSerializer.Serialize(new { action = "play_card", card_index = index, reason });
        string? accepted = null;
        string? response = null;
        (string? Action, string ResultJson, string? Fingerprint, bool Unsettled, string? Error) outcome;
        AgentTurnResult Receipt() => new()
        {
            Acted = accepted, ActResultJson = response, Reasoning = reason,
            ExecutedUnsettled = accepted != null, RequestsSpent = 0
        };
        try
        {
            outcome = await ExecuteActAsync(args, token, checkState,
                (action, result) => { accepted = action; response = result; }, requireSandpitEscape: true);
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            throw new AgentTurnCanceledException(Receipt(), ex, token);
        }
        catch (AutoPlayStoppedException ex)
        {
            ex.Receipt = Receipt();
            throw;
        }
        return new AgentTurnResult
        {
            Acted = outcome.Action, ActResultJson = outcome.ResultJson,
            Error = outcome.Error, Reasoning = reason,
            StateFingerprint = outcome.Fingerprint, ExecutedUnsettled = outcome.Unsettled,
            RequestsSpent = 0
        };
    }

    private static void ValidateSandpitDispatch(string stateJson, string action, int? cardIndex, bool requireEscape)
    {
        var safety = SandpitSafetyPolicy.Read(stateJson);
        // Read() establishes the card's identity and native playability, not just its index.
        // Cancel the automatic escape even when the countdown itself changed to a safe value.
        if (requireEscape && (!safety.Critical || !cardIndex.HasValue || safety.EscapeIndex != cardIndex))
            throw new ApiException(409, "invalid_target",
                Loc.T("沙坑状态或手牌已变化，已取消自动逃离，请重新读取状态。"),
                new { card_index = cardIndex, expected_card_id = "FRANTIC_ESCAPE" });
        var error = SandpitSafetyPolicy.Reject(safety, action, cardIndex);
        if (error == null) return;
        if (action == "end_turn") throw new AutoPlayStoppedException(error, StopKindPolicy.Failed);
        throw new ApiException(409, "invalid_action", error, new { reason = "sandpit_emergency" });
    }
}
