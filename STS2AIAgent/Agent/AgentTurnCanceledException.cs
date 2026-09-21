namespace STS2AIAgent.Agent;

/// <summary>Cancellation plus the actions and known usage that occurred before it.</summary>
/// <remarks>Unknown provider usage remains null. Consuming a receipt must never resume a turn.</remarks>
internal sealed class AgentTurnCanceledException(
    AgentTurnResult receipt, OperationCanceledException cause, CancellationToken token)
    : OperationCanceledException(cause.Message, cause, token)
{
    public AgentTurnResult Receipt { get; } = receipt;
}
