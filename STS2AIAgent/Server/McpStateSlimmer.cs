using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using STS2AIAgent.Agent;

namespace STS2AIAgent.Server;

/// <summary>
/// Slims compact MCP game-state payloads inside a single frame and deduplicates unchanged static
/// run/glossary/guidance blocks across consecutive tool calls in the same MCP session.
/// </summary>
internal static class McpStateSlimmer
{
    internal const DietRules McpDietRules = DietRules.All & ~DietRules.DropCardValueNote;

    internal static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Stateless intra-frame projection for MCP compact state. Preserves <c>card_value_note</c> for
    /// session-level first-frame delivery and leaves raw fallback payloads untouched.
    /// </summary>
    public static string SlimCompactState(string? compactStateJson)
    {
        if (compactStateJson is null) return string.Empty;
        if (string.IsNullOrWhiteSpace(compactStateJson)) return compactStateJson;

        try
        {
            var parsed = JsonNode.Parse(compactStateJson);
            if (parsed is not JsonObject obj) return compactStateJson;
            if (IsRawFallback(obj)) return compactStateJson;
        }
        catch (JsonException)
        {
            return compactStateJson;
        }

        return PromptStateDiet.Apply(compactStateJson, McpDietRules);
    }

    internal static JsonObject? SlimStateObject(JsonObject? state)
    {
        if (state is null) return null;
        if (IsRawFallback(state)) return state;
        var slimmed = PromptStateDiet.Apply(state.ToJsonString(CompactJson), McpDietRules);
        return JsonNode.Parse(slimmed) as JsonObject;
    }

    internal static bool IsRawFallback(JsonObject state) =>
        (state["compact_agent_view"] is JsonValue marker && marker.GetValueKind() == JsonValueKind.False)
        || state.ContainsKey("mod_version");

    internal static string HashNode(JsonNode? node)
    {
        var text = node?.ToJsonString(CompactJson) ?? "null";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// Bounded per-session tracker that replaces unchanged <c>run.deck</c>, <c>run.relic_descriptions</c>,
/// <c>run.ascension_effects</c>, already-sent <c>glossary</c> definitions, <c>combat.card_value_note</c>,
/// and repeated <c>decide()</c> <c>scene_guidance</c> blocks with lightweight markers.
/// </summary>
internal sealed class McpSessionDedup
{
    private const string DefaultSessionKey = "__default__";
    private const string FallbackActiveRunScope = "__active_run__";
    private const int MaxSessions = 16;

    private readonly object _gate = new();
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();

    public void ResetSession(string? sessionId)
    {
        lock (_gate)
        {
            var key = NormalizeKey(sessionId);
            if (_sessions.Remove(key))
            {
                _lru.Remove(key);
            }
        }
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            _sessions.Clear();
            _lru.Clear();
        }
    }

    public string ApplyToStateJson(string? sessionId, string? compactStateJson, bool fullState = false)
    {
        var slimmed = McpStateSlimmer.SlimCompactState(compactStateJson);
        if (string.IsNullOrWhiteSpace(slimmed)) return slimmed;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(slimmed);
        }
        catch (JsonException)
        {
            return slimmed;
        }

        if (root is not JsonObject stateObj || McpStateSlimmer.IsRawFallback(stateObj))
        {
            return slimmed;
        }

        lock (_gate)
        {
            var session = GetOrCreateSession(sessionId);
            DedupStateInPlace(session, stateObj, fullState);
        }

        return stateObj.ToJsonString(McpStateSlimmer.CompactJson);
    }

    public string ApplyToWaitResultJson(string? sessionId, string waitResultJson, bool rawState)
    {
        if (rawState || string.IsNullOrWhiteSpace(waitResultJson)) return waitResultJson;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(waitResultJson);
        }
        catch (JsonException)
        {
            return waitResultJson;
        }

        if (root is not JsonObject obj || obj["state"] is not JsonObject stateObj)
        {
            return waitResultJson;
        }

        var slimmedState = McpStateSlimmer.SlimStateObject(stateObj);
        if (slimmedState is null || McpStateSlimmer.IsRawFallback(slimmedState))
        {
            return waitResultJson;
        }

        lock (_gate)
        {
            var session = GetOrCreateSession(sessionId);
            DedupStateInPlace(session, slimmedState, fullState: false);
        }

        obj["state"] = slimmedState;
        return obj.ToJsonString(McpStateSlimmer.CompactJson);
    }

    public string ApplyToActResultJson(string? sessionId, string actResultJson, bool rawState)
    {
        if (rawState || string.IsNullOrWhiteSpace(actResultJson)) return actResultJson;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(actResultJson);
        }
        catch (JsonException)
        {
            return actResultJson;
        }

        if (root is not JsonObject obj || obj.ContainsKey("error"))
        {
            return actResultJson;
        }

        lock (_gate)
        {
            var session = GetOrCreateSession(sessionId);
            if (obj["plan"] is JsonObject)
            {
                if (obj["final_state"] is JsonObject finalState)
                {
                    var slimFinal = McpStateSlimmer.SlimStateObject(finalState);
                    if (slimFinal is not null && !McpStateSlimmer.IsRawFallback(slimFinal))
                    {
                        DedupStateInPlace(session, slimFinal, fullState: false);
                        obj["final_state"] = slimFinal;
                    }
                    obj.Remove("state");
                }
                else if (obj["state"] is JsonObject singlePlanState)
                {
                    var slimSingle = McpStateSlimmer.SlimStateObject(singlePlanState);
                    if (slimSingle is not null && !McpStateSlimmer.IsRawFallback(slimSingle))
                    {
                        DedupStateInPlace(session, slimSingle, fullState: false);
                        obj["state"] = slimSingle;
                    }
                }
            }
            else
            {
                if (obj["state"] is JsonObject stateObj)
                {
                    var slimState = McpStateSlimmer.SlimStateObject(stateObj);
                    if (slimState is not null && !McpStateSlimmer.IsRawFallback(slimState))
                    {
                        DedupStateInPlace(session, slimState, fullState: false);
                        obj["state"] = slimState;
                    }

                    if (obj["previous"] is JsonObject previous)
                    {
                        previous.Remove("state");
                    }
                }

                if (obj["reconciliation"] is JsonObject recon && recon["state"] is JsonObject reconState)
                {
                    var slimRecon = McpStateSlimmer.SlimStateObject(reconState);
                    if (slimRecon is not null && !McpStateSlimmer.IsRawFallback(slimRecon))
                    {
                        DedupStateInPlace(session, slimRecon, fullState: false);
                        recon["state"] = slimRecon;
                    }
                }
            }
        }

        return obj.ToJsonString(McpStateSlimmer.CompactJson);
    }

    public string ApplyToDecideResultJson(string? sessionId, string decideResultJson)
    {
        if (string.IsNullOrWhiteSpace(decideResultJson)) return decideResultJson;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(decideResultJson);
        }
        catch (JsonException)
        {
            return decideResultJson;
        }

        if (root is not JsonObject obj) return decideResultJson;

        lock (_gate)
        {
            var session = GetOrCreateSession(sessionId);
            if (obj["state"] is JsonObject stateObj)
            {
                var slimState = McpStateSlimmer.SlimStateObject(stateObj);
                if (slimState is not null && !McpStateSlimmer.IsRawFallback(slimState))
                {
                    DedupStateInPlace(session, slimState, fullState: false);
                    obj["state"] = slimState;
                }
            }

            if (obj["scene_guidance"] is JsonObject guidance)
            {
                var guidanceHash = McpStateSlimmer.HashNode(guidance);
                if (string.Equals(session.LastGuidanceHash, guidanceHash, StringComparison.Ordinal))
                {
                    var slimGuidance = new JsonObject
                    {
                        ["screen"] = guidance["screen"]?.DeepClone(),
                        ["scene"] = guidance["scene"]?.DeepClone(),
                        ["guidance_unchanged"] = true,
                    };
                    obj["scene_guidance"] = slimGuidance;
                }
                else
                {
                    session.LastGuidanceHash = guidanceHash;
                }
            }
        }

        return obj.ToJsonString(McpStateSlimmer.CompactJson);
    }

    private static void DedupStateInPlace(SessionState session, JsonObject state, bool fullState)
    {
        var runScope = ReadRunScope(state);
        if (runScope is null)
        {
            session.ResetRun();
            if (fullState) session.LastGuidanceHash = null;
            return;
        }

        if (fullState || !string.Equals(session.LastRunScope, runScope, StringComparison.Ordinal))
        {
            session.ResetRun();
            session.LastRunScope = runScope;
            if (fullState) session.LastGuidanceHash = null;
        }

        if (state["run"] is JsonObject run)
        {
            if (run["deck"] is JsonArray deck && deck.Count > 0)
            {
                var deckHash = McpStateSlimmer.HashNode(deck);
                var deckGroups = deck.Count;
                if (!fullState && string.Equals(session.DeckHash, deckHash, StringComparison.Ordinal))
                {
                    run.Remove("deck");
                    run["deck_unchanged"] = true;
                    run["deck_groups"] = deckGroups;
                }
                else
                {
                    session.DeckHash = deckHash;
                }
            }
            else if (run.ContainsKey("deck"))
            {
                session.DeckHash = null;
            }

            if (run["relic_descriptions"] is JsonArray relicDescriptions && relicDescriptions.Count > 0)
            {
                var composite = new JsonArray(
                    run["relics"]?.DeepClone(),
                    run["relic_ids"]?.DeepClone(),
                    relicDescriptions.DeepClone());
                var relicHash = McpStateSlimmer.HashNode(composite);
                if (!fullState && string.Equals(session.RelicDescHash, relicHash, StringComparison.Ordinal))
                {
                    run.Remove("relic_descriptions");
                    run["relic_descriptions_unchanged"] = true;
                }
                else
                {
                    session.RelicDescHash = relicHash;
                }
            }
            else if (run.ContainsKey("relic_descriptions"))
            {
                session.RelicDescHash = null;
            }

            if (run["ascension_effects"] is JsonArray ascensionEffects && ascensionEffects.Count > 0)
            {
                var ascHash = McpStateSlimmer.HashNode(ascensionEffects);
                if (!fullState && string.Equals(session.AscensionHash, ascHash, StringComparison.Ordinal))
                {
                    run.Remove("ascension_effects");
                    run["ascension_effects_unchanged"] = true;
                }
                else
                {
                    session.AscensionHash = ascHash;
                }
            }
            else if (run.ContainsKey("ascension_effects"))
            {
                session.AscensionHash = null;
            }
        }

        if (state["combat"] is JsonObject combat
            && combat["card_value_note"] is JsonValue noteValue
            && noteValue.GetValueKind() == JsonValueKind.String
            && !string.IsNullOrEmpty(noteValue.GetValue<string>()))
        {
            if (!fullState && session.CardValueNoteSent)
            {
                combat.Remove("card_value_note");
            }
            else
            {
                session.CardValueNoteSent = true;
            }
        }

        if (state["glossary"] is JsonObject glossary)
        {
            if (glossary.Count == 0)
            {
                session.Glossary.Clear();
            }
            else
            {
                var toRemove = new List<string>();
                foreach (var (term, defNode) in glossary)
                {
                    var defText = defNode?.ToJsonString(McpStateSlimmer.CompactJson) ?? "null";
                    if (!fullState
                        && session.Glossary.TryGetValue(term, out var previousDef)
                        && string.Equals(previousDef, defText, StringComparison.Ordinal))
                    {
                        toRemove.Add(term);
                    }
                    else
                    {
                        session.Glossary[term] = defText;
                    }
                }

                if (toRemove.Count > 0)
                {
                    foreach (var term in toRemove)
                    {
                        glossary.Remove(term);
                    }

                    if (glossary.Count == 0)
                    {
                        state.Remove("glossary");
                        state["glossary_unchanged"] = true;
                    }
                    else
                    {
                        state["glossary_delta"] = true;
                    }
                }
            }
        }
    }

    private static string? ReadRunScope(JsonObject state)
    {
        if (state["run_id"] is JsonValue idVal && idVal.GetValueKind() == JsonValueKind.String)
        {
            var id = idVal.GetValue<string>().Trim();
            if (id.Length > 0 && !string.Equals(id, "run_unknown", StringComparison.Ordinal))
            {
                return id;
            }
        }

        if (state["run"] is JsonObject)
        {
            return FallbackActiveRunScope;
        }

        if (state["combat"] is JsonObject || state["glossary"] is JsonObject { Count: > 0 })
        {
            return FallbackActiveRunScope;
        }

        return null;
    }

    private static string NormalizeKey(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? DefaultSessionKey : sessionId.Trim();

    private SessionState GetOrCreateSession(string? sessionId)
    {
        var key = NormalizeKey(sessionId);
        if (_sessions.TryGetValue(key, out var existing))
        {
            _lru.Remove(key);
            _lru.AddLast(key);
            return existing;
        }

        while (_sessions.Count >= MaxSessions && _lru.First is { } oldest)
        {
            _sessions.Remove(oldest.Value);
            _lru.RemoveFirst();
        }

        var created = new SessionState();
        _sessions[key] = created;
        _lru.AddLast(key);
        return created;
    }

    private sealed class SessionState
    {
        public string? LastRunScope { get; set; }
        public string? DeckHash { get; set; }
        public string? RelicDescHash { get; set; }
        public string? AscensionHash { get; set; }
        public bool CardValueNoteSent { get; set; }
        public Dictionary<string, string> Glossary { get; } = new(StringComparer.Ordinal);
        public string? LastGuidanceHash { get; set; }

        public void ResetRun()
        {
            LastRunScope = null;
            DeckHash = null;
            RelicDescHash = null;
            AscensionHash = null;
            CardValueNoteSent = false;
            Glossary.Clear();
        }
    }
}
