using System.Text;
using System.Text.RegularExpressions;
using STS2AIAgent.Localization;

namespace STS2AIAgent.Tests;

/// <summary>
/// The mod follows the game's language: Chinese returns the source text untouched, English reads the
/// English table, and a key with no entry falls back to the Chinese text instead of a blank.
/// </summary>
internal static class LocalizationTests
{
    private static readonly Regex CallSite = new("Loc\\.T\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex ConstantArgument = new("Loc\\.T\\(\\s*(\\w+)\\s*[,)]", RegexOptions.Compiled);
    private static readonly Regex ConstantDeclaration = new("const\\s+string\\s+(\\w+)\\s*=\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex FrozenField = new("^[ \\t]*(?:private|public|internal|protected)[ \\t]+[^\\n=<>]*=[ \\t]*Loc\\.T\\(", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex FrozenProperty = new("\\{\\s*get;[^}]*\\}\\s*=\\s*new\\b[^;]*;", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex DefinitionEntry = new("\\(\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\)", RegexOptions.Compiled);
    private static readonly Regex AliasEntry = new("\\[\"([^\"]+)\"\\]\\s*=\\s*new\\[\\]\\s*\\{([^}]*)\\}", RegexOptions.Compiled);

    public static void ChineseReadsTheTextAsWritten()
    {
        try
        {
            Loc.SetLanguage("zhs");
            Assert.True(Loc.IsChinese, "zhs must select Chinese.");
            Assert.Equal("力量", Loc.T("力量"));
            Assert.Equal("2费", Loc.T("{0}费", 2));
        }
        finally
        {
            Loc.SetLanguage("zhs");
        }
    }

    public static void EnglishLooksUpTheTable()
    {
        try
        {
            Loc.SetLanguage("eng");
            Assert.False(Loc.IsChinese, "eng must not select Chinese.");
            Assert.Equal("Settings", Loc.T("设置"));
            Assert.Equal("Strength", Loc.T("力量"));
            Assert.Equal("2 Energy", Loc.T("{0}费", 2));
            Assert.Equal("Status: ready", Loc.T("状态：{0}", "ready"));
        }
        finally
        {
            Loc.SetLanguage("zhs");
        }
    }

    public static void UnknownKeyFallsBackToChinese()
    {
        try
        {
            Loc.SetLanguage("eng");
            Assert.Equal("这句话没有英文条目。", Loc.T("这句话没有英文条目。"));
            Assert.Equal("没有条目：7", Loc.T("没有条目：{0}", 7));
        }
        finally
        {
            Loc.SetLanguage("zhs");
        }
    }

    public static void BrokenPlaceholderDoesNotThrow()
    {
        var format = "带 {0} 和 {1} 的句子。";

        Assert.True(Loc.T(format) == format, "Chinese must return the source text.");
        try
        {
            Loc.SetLanguage("eng");
            Loc.SetLanguage("eng");
            var text = Loc.T("{0}费");
            Assert.True(text.Length > 0, "A missing argument must still produce text.");
        }
        finally
        {
            Loc.SetLanguage("zhs");
        }
    }

    public static void ReadsEveryLanguageCodeShape()
    {
        foreach (var code in new[] { "zhs", "zh", "zh_CN", "zh-Hans", "schinese", "tchinese", "chinese", "ZHS" })
        {
            Assert.True(Loc.IsChineseCode(code), code + " must count as Chinese.");
        }

        foreach (var code in new[] { "eng", "en", "enu", "jpn", "deu", "esp", "fra", "kor", "rus", "", "  ", null })
        {
            Assert.False(Loc.IsChineseCode(code), (code ?? "<null>") + " must not count as Chinese.");
        }
    }

    public static void ReportsLanguageChangesOnlyOnce()
    {
        var changes = 0;
        Action handler = () => changes++;

        try
        {
            Loc.SetLanguage("zhs");
            Loc.LanguageChanged += handler;
            Loc.SetLanguage("zhs");
            Assert.Equal(0, changes);
            Loc.SetLanguage("eng");
            Loc.SetLanguage("eng");
            Assert.Equal(1, changes);
        }
        finally
        {
            Loc.LanguageChanged -= handler;
            Loc.SetLanguage("zhs");
        }
    }

    /// <summary>
    /// The guard that keeps a later string from shipping untranslated: every Chinese literal handed
    /// to <see cref="Loc.T(string)"/> in the mod's source has to have an English entry. Constants
    /// passed in place of a literal are resolved from their declaration in the same file.
    /// </summary>
    public static void EveryCallSiteHasAnEnglishEntry()
    {
        var missing = new List<string>();
        var sites = 0;

        foreach (var path in AgentSourceFixture.SourceFiles())
        {
            var relative = Path.GetRelativePath(AgentSourceFixture.Root, path).Replace('\\', '/');
            if (relative.StartsWith("STS2AIAgent/Localization/", StringComparison.Ordinal))
            {
                continue;
            }

            var source = File.ReadAllText(path, Encoding.UTF8);
            foreach (Match match in CallSite.Matches(source))
            {
                var key = Unescape(match.Groups[1].Value);
                if (!HasChinese(key))
                {
                    continue;
                }

                sites++;
                if (!Loc.HasEntry(key))
                {
                    missing.Add(relative + ": " + key);
                }
            }

            foreach (Match match in ConstantArgument.Matches(source))
            {
                var name = match.Groups[1].Value;
                foreach (Match declaration in ConstantDeclaration.Matches(source))
                {
                    if (declaration.Groups[1].Value != name)
                    {
                        continue;
                    }

                    var value = Unescape(declaration.Groups[2].Value);
                    if (!HasChinese(value))
                    {
                        continue;
                    }

                    sites++;
                    if (!Loc.HasEntry(value))
                    {
                        missing.Add(relative + ": " + name + " = " + value);
                    }
                }
            }
        }

        Assert.True(sites > 300, "The scan found only " + sites + " call sites; it is not reading the mod source.");
        Assert.True(missing.Count == 0, "Missing English entries:\n" + string.Join("\n", missing));
    }

    public static void NoKeyIsDefinedTwiceWithDifferentText()
    {
        var shards = Loc.Shards();
        var conflicts = new List<string>();

        for (var left = 0; left < shards.Count; left++)
        {
            for (var right = left + 1; right < shards.Count; right++)
            {
                foreach (var entry in shards[left])
                {
                    if (shards[right].TryGetValue(entry.Key, out var other) && other != entry.Value)
                    {
                        conflicts.Add(entry.Key + ": \"" + entry.Value + "\" vs \"" + other + "\"");
                    }
                }
            }
        }

        Assert.True(conflicts.Count == 0, "Shards disagree on:\n" + string.Join("\n", conflicts));
    }

    public static void EveryChineseEntryCarriesRealEnglish()
    {
        var offenders = new List<string>();
        foreach (var entry in Loc.Entries)
        {
            if (!HasChinese(entry.Key))
            {
                continue;
            }

            if (entry.Value == entry.Key)
            {
                offenders.Add(entry.Key + " is left untranslated");
            }
            else if (HasChinese(entry.Value))
            {
                offenders.Add(entry.Key + " still contains Chinese: " + entry.Value);
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    /// <summary>
    /// Text resolved while a field or a computed-once property is initialized freezes in whatever
    /// language was active then, so the overlay keeps reading Chinese after the player switches the
    /// game to English. Idle wording has to be resolved on read instead.
    /// </summary>
    public static void NoTranslatedTextIsFrozenAtConstruction()
    {
        var offenders = new List<string>();

        foreach (var path in AgentSourceFixture.SourceFiles())
        {
            var relative = Path.GetRelativePath(AgentSourceFixture.Root, path).Replace('\\', '/');
            if (relative.StartsWith("STS2AIAgent/Localization/", StringComparison.Ordinal))
            {
                continue;
            }

            var source = File.ReadAllText(path, Encoding.UTF8);
            foreach (Match match in FrozenField.Matches(source))
            {
                offenders.Add(relative + ": field holds Loc.T -> " + match.Value.Trim());
            }

            foreach (Match match in FrozenProperty.Matches(source))
            {
                if (match.Value.Contains("Loc.T(", StringComparison.Ordinal))
                {
                    offenders.Add(relative + ": computed-once property holds Loc.T -> " + FirstLine(match.Value));
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    private static string FirstLine(string text)
    {
        var index = text.IndexOf('\n');
        return index < 0 ? text.Trim() : text[..index].Trim();
    }

    public static void StartupReadsTheLanguageBeforeTheUiIsBuilt()
    {
        var entry = AgentSourceFixture.Read("STS2AIAgent/ModEntry.cs");
        var overlay = AgentSourceFixture.Read("STS2AIAgent/Ui/AgentOverlayHost.cs");

        var language = entry.IndexOf("LocSource.Initialize()", StringComparison.Ordinal);
        var overlayInstall = entry.IndexOf("AgentOverlayHost.Install()", StringComparison.Ordinal);
        Assert.True(language >= 0, "ModEntry must load the game language.");
        Assert.True(overlayInstall < 0 || language < overlayInstall, "The language must load before the overlay is installed.");

        Assert.Contains("LocSource.Initialize()", overlay);
        Assert.Contains("Loc.LanguageChanged", overlay);
        Assert.Contains("Loc.LanguageChanged -=", overlay);
    }

    /// <summary>
    /// The combat glossary keys off Chinese words, but card text arrives in the game's language, so
    /// every keyword needs the English spelling too. Both tables are hand-written in
    /// GameStateService; this keeps them, and the English label the model reads, in step.
    /// </summary>
    public static void GlossaryKeywordsStayAlignedWithTheirEnglishSpellings()
    {
        var source = AgentSourceFixture.Read("STS2AIAgent/Game/GameStateService.cs");
        var keywords = new List<string>();
        var definitions = new List<string>();

        var block = Slice(source, "AgentKeywordDefinitions =", "};");
        foreach (Match match in DefinitionEntry.Matches(block))
        {
            keywords.Add(match.Groups[1].Value);
            definitions.Add(match.Groups[2].Value);
        }

        var aliasBlock = Slice(source, "AgentKeywordAliases = new(StringComparer.Ordinal)", "};");
        var aliases = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match match in AliasEntry.Matches(aliasBlock))
        {
            var spellings = match.Groups[2].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(spelling => spelling.Trim('"', ' '))
                .ToList();
            aliases[match.Groups[1].Value] = spellings;
        }

        Assert.True(keywords.Count >= 15, "Only found " + keywords.Count + " glossary keywords.");
        Assert.Equal(keywords.Count, definitions.Count);

        // The English column is only visible to the model on an English client, so compare there.
        Loc.SetLanguage("eng");
        var offenders = new List<string>();
        try
        {
            foreach (var keyword in keywords)
            {
                if (!Loc.HasEntry(keyword))
                {
                    offenders.Add("no English entry for keyword " + keyword);
                    continue;
                }

                if (!aliases.TryGetValue(keyword, out var spellings))
                {
                    offenders.Add("no English spelling to match for keyword " + keyword);
                }
                else if (!spellings.Contains(Loc.T(keyword), StringComparer.Ordinal))
                {
                    offenders.Add(
                        keyword + " matches [" + string.Join(", ", spellings) + "] but reads as \"" + Loc.T(keyword) + "\"");
                }
            }

            foreach (var definition in definitions)
            {
                if (!Loc.HasEntry(definition))
                {
                    offenders.Add("no English entry for definition " + definition);
                }
            }
        }
        finally
        {
            Loc.SetLanguage("zhs");
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Marker not found: " + startMarker);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, "End marker not found after: " + startMarker);
        return source[start..end];
    }

    private static bool HasChinese(string text)
    {
        foreach (var character in text)
        {
            if (character is >= '\u3000' and <= '\u303f')
            {
                return true;
            }

            if (character is >= '\u3400' and <= '\u9fff')
            {
                return true;
            }

            if (character is >= '\uff00' and <= '\uffef')
            {
                return true;
            }
        }

        return false;
    }

    private static string Unescape(string literal)
    {
        return literal
            .Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }
}
