using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace STS2AIAgent.Agent;

/// <summary>
/// Result of splitting a dieted compact game state into a cache-friendly static run prefix and a
/// volatile per-step state payload.
/// </summary>
/// <param name="StaticRunJson">
/// Deterministic JSON carrying unchanging run inventory and baseline glossary for the current
/// <c>(run_id, screen, inventory)</c> scope, or <c>null</c> when the snapshot carries no static run inventory.
/// </param>
/// <param name="DynamicStateJson">
/// Remaining volatile frame data (combat, hand, enemies, HP/gold/potions/relic_stacks, and any
/// newly-introduced transient glossary entries not in the cached baseline).
/// </param>
internal sealed record PlayStateSplitResult(string? StaticRunJson, string DynamicStateJson);

/// <summary>
/// Splits static run context (<c>deck</c>, <c>relics</c>, <c>relic_descriptions</c>,
/// <c>ascension_effects</c>, and baseline <c>glossary</c>) out of the volatile user-message state so
/// LLM prefix caches stay intact across every turn and card play of a combat room (#234).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only transforms the prompt's already-dieted state string; HTTP <c>/state</c> and MCP payloads are untouched.</item>
/// <item>Caches the serialized <c>StaticRunJson</c> and baseline glossary keys per <c>(run_id, screen, inventory)</c>
/// so mid-fight combat keywords added on later turns stay in <c>DynamicStateJson</c> as a tiny delta instead of
/// mutating the system prefix.</item>
/// <item>Whenever <c>deck</c>, <c>relics</c>, <c>relic_ids</c>, <c>relic_descriptions</c>, or
/// <c>ascension_effects</c> changes (card reward, shop purchase, relic pickup), the inventory key changes and a
/// fresh static prefix is computed immediately.</item>
/// <item>Never throws: malformed or minimalist test states without static run inventory pass through with
/// <c>StaticRunJson = null</c>.</item>
/// </list>
/// </remarks>
internal sealed class PlayStateSplitCache
{
    private const int MaxEntries = 16;

    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] TriggerRunKeys =
    {
        "deck",
        "relics",
        "relic_ids",
        "relic_descriptions",
        "ascension_effects",
    };

    private static readonly string[] HoistedRunKeys =
    {
        "character",
        "ascension",
        "ascension_effects",
        "max_energy",
        "base_orb_slots",
        "deck",
        "relics",
        "relic_ids",
        "relic_descriptions",
    };

    private sealed record CacheEntry(
        string CacheKey,
        string StaticRunJson,
        HashSet<string> BaselineGlossaryKeys);

    private readonly object _sync = new();
    private readonly List<CacheEntry> _entries = new();

    public PlayStateSplitResult Split(string? dietStateJson, string? screen = null)
    {
        if (dietStateJson is null) return new PlayStateSplitResult(null, string.Empty);
        if (string.IsNullOrWhiteSpace(dietStateJson)) return new PlayStateSplitResult(null, dietStateJson);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(dietStateJson);
        }
        catch (JsonException)
        {
            return new PlayStateSplitResult(null, dietStateJson);
        }

        if (root is not JsonObject state || state["run"] is not JsonObject runObj || !HasStaticRunInventory(runObj))
        {
            return new PlayStateSplitResult(null, dietStateJson);
        }

        var staticRunObj = new JsonObject();
        foreach (var key in HoistedRunKeys)
        {
            if (!runObj.TryGetPropertyValue(key, out var node)) continue;
            runObj.Remove(key);
            if (node is not null)
            {
                staticRunObj[key] = node.DeepClone();
            }
        }

        if (runObj.Count == 0)
        {
            state.Remove("run");
        }

        var runScope = Text(state["run_id"]) ?? "_default_";
        var screenScope = !string.IsNullOrWhiteSpace(screen)
            ? screen.Trim()
            : Text(state["screen"]) ?? "UNKNOWN";
        var inventoryJson = staticRunObj.ToJsonString(Compact);
        var cacheKey = runScope + "\n" + screenScope + "\n" + inventoryJson;

        CacheEntry entry;
        lock (_sync)
        {
            var existingIndex = _entries.FindIndex(item => string.Equals(item.CacheKey, cacheKey, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                entry = _entries[existingIndex];
                if (existingIndex != _entries.Count - 1)
                {
                    _entries.RemoveAt(existingIndex);
                    _entries.Add(entry);
                }
            }
            else
            {
                var staticRoot = new JsonObject();
                if (Text(state["run_id"]) is { Length: > 0 } runId)
                {
                    staticRoot["run_id"] = runId;
                }

                staticRoot["run"] = staticRunObj;
                var baselineKeys = new HashSet<string>(StringComparer.Ordinal);
                if (state["glossary"] is JsonObject glossaryObj && glossaryObj.Count > 0)
                {
                    var baselineGlossary = new JsonObject();
                    foreach (var pair in glossaryObj)
                    {
                        baselineKeys.Add(pair.Key);
                        baselineGlossary[pair.Key] = pair.Value?.DeepClone();
                    }

                    staticRoot["glossary"] = baselineGlossary;
                }

                entry = new CacheEntry(cacheKey, staticRoot.ToJsonString(Compact), baselineKeys);
                if (_entries.Count >= MaxEntries)
                {
                    _entries.RemoveAt(0);
                }

                _entries.Add(entry);
            }
        }

        if (state["glossary"] is JsonObject dynamicGlossary)
        {
            foreach (var key in dynamicGlossary.Select(pair => pair.Key).ToList())
            {
                if (entry.BaselineGlossaryKeys.Contains(key))
                {
                    dynamicGlossary.Remove(key);
                }
            }

            if (dynamicGlossary.Count == 0)
            {
                state.Remove("glossary");
            }
        }

        state.Remove("run_id");
        return new PlayStateSplitResult(entry.StaticRunJson, state.ToJsonString(Compact));
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    private static bool HasStaticRunInventory(JsonObject runObj)
    {
        foreach (var key in TriggerRunKeys)
        {
            if (!runObj.TryGetPropertyValue(key, out var node) || node is null) continue;
            if (node is JsonArray array && array.Count > 0) return true;
            if (node is JsonObject obj && obj.Count > 0) return true;
        }

        return false;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : null;
}
