using System;
using System.Collections.Generic;

namespace STS2AIAgent.Localization;

/// <summary>
/// UI language selection and text lookup for everything the mod shows a player.
/// </summary>
/// <remarks>
/// The Chinese text is the key. Chinese mode returns it unchanged, so the language the strings were
/// written in needs no table; English mode looks the key up. A key with no English entry falls back
/// to the Chinese text, so a half-translated build stays readable instead of showing blanks.
///
/// This half stays free of Godot and game types so the offline test project can compile and drive
/// it; <c>Loc.Live.cs</c> owns the parts that read the running game's language.
/// </remarks>
internal static partial class Loc
{
    public const string Chinese = "zh";
    public const string English = "en";

    /// <summary>Language used before anything has reported the game's setting.</summary>
    public const string Default = Chinese;

    private static readonly Dictionary<string, string> Table = BuildTable();
    private static string _current = Default;
    private static bool _hasReported;

    /// <summary>Raised after the language actually changes, so views built from text can rebuild.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Current UI language: <see cref="Chinese"/> or <see cref="English"/>.</summary>
    public static string Current => _current;

    public static bool IsChinese => _current == Chinese;

    /// <summary>True once a language source has reported in; until then <see cref="Default"/> applies.</summary>
    public static bool HasReported => _hasReported;

    /// <summary>
    /// Applies a raw language code from any source. Accepts the game's three-letter codes
    /// (<c>zhs</c>, <c>eng</c>) and the platform shapes they come from (<c>zh_CN</c>,
    /// <c>zh-Hans</c>, Steam's <c>schinese</c>), because either can arrive through the fallbacks.
    /// </summary>
    public static void SetLanguage(string? code)
    {
        var next = IsChineseCode(code) ? Chinese : English;
        _hasReported = true;
        if (next == _current)
        {
            return;
        }

        _current = next;
        LanguageChanged?.Invoke();
    }

    /// <summary>Translates a Chinese source string; unknown keys return the source unchanged.</summary>
    public static string T(string chinese)
    {
        if (chinese.Length == 0 || _current == Chinese)
        {
            return chinese;
        }

        return Table.TryGetValue(chinese, out var english) ? english : chinese;
    }

    /// <summary>Formatted variant: the source carries {0}, {1}, ... placeholders.</summary>
    public static string T(string chinese, params object?[] args)
    {
        var text = T(chinese);
        try
        {
            return string.Format(text, args);
        }
        catch (FormatException)
        {
            // A translation with a broken placeholder must not take the overlay down.
            return text;
        }
    }

    /// <summary>Number of English entries, for tests and diagnostics.</summary>
    public static int EntryCount => Table.Count;

    /// <summary>True when the source string has an English entry. Used by the coverage test.</summary>
    public static bool HasEntry(string chinese) => Table.ContainsKey(chinese);

    /// <summary>Every Chinese key that has an English entry, for the coverage test.</summary>
    public static IReadOnlyCollection<string> Keys => Table.Keys;

    /// <summary>The merged table, for tests that have to judge the English text itself.</summary>
    internal static IReadOnlyDictionary<string, string> Entries => Table;

    public static bool IsChineseCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalized = code.Trim().ToLowerInvariant().Replace('-', '_');
        return normalized.StartsWith("zh", StringComparison.Ordinal)
            || normalized is "schinese" or "tchinese" or "chinese";
    }

    private static Dictionary<string, string> BuildTable()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var shard in Shards())
        {
            foreach (var entry in shard)
            {
                map[entry.Key] = entry.Value;
            }
        }

        return map;
    }

    /// <summary>
    /// The same table split by the area each shard covers, in merge order. Exposed so tests can see
    /// a key defined twice with different wording, which the merged table silently resolves.
    /// </summary>
    internal static IReadOnlyList<Dictionary<string, string>> Shards()
    {
        var builders = new Action<Dictionary<string, string>>[]
        {
            AddUiEntries,
            AddRuntimeEntries,
            AddFacingEntries,
            AddSupportEntries,
            AddGameEntries
        };

        var shards = new List<Dictionary<string, string>>(builders.Length);
        foreach (var build in builders)
        {
            var shard = new Dictionary<string, string>(StringComparer.Ordinal);
            build(shard);
            shards.Add(shard);
        }

        return shards;
    }
}
