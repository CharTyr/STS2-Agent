namespace STS2AIAgent.Llm;

/// <summary>
/// Transport for the TypeSafe Jev "system one" endpoint that answers per-action questions for the
/// dual-layer decision mode.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="ILlmClient"/> even though both are HTTP LLM providers: <see cref="ILlmClient"/>
/// speaks the OpenAI-compatible chat protocol and produces free-form tool calls, while this one asks a
/// fixed set of questions and gets structured answers back with a confidence attached. The dual-layer
/// planner needs that shape, and forcing it through <see cref="ILlmClient"/> would mean encoding the
/// questions as a prompt and parsing the answer out of prose again.
///
/// Both methods accept the caller's <see cref="System.Threading.CancellationToken"/> and let its
/// cancellation propagate as an <see cref="System.OperationCanceledException"/>; a cancelled Jev call
/// must not be recorded as a failed provider probe or spent budget.
/// </remarks>
internal interface IJevClient
{
    /// <summary>Asks <paramref name="request"/>'s questions about the state it carries.</summary>
    Task<JevResponse> SystemOneAsync(JevRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the configured base URL and API key against <c>GET {BaseUrl}/v1/models</c> and returns
    /// a short human sentence for display. Unlike <see cref="SystemOneAsync"/> it reports a failure in
    /// its return value rather than raising, because its caller is a settings screen showing a status
    /// line. Only a cancelled call throws.
    /// </summary>
    Task<string> PingAsync(CancellationToken cancellationToken);
}
