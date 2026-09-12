using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.addons.mega_text;

namespace STS2AIAgent.Game;

internal static class GameDataExportService
{
    private static readonly Regex CardMarkupRegex = new(@"\[(?:/?[^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex CardWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static object ExportCollection(string collection)
    {
        return collection.Trim().ToLowerInvariant() switch
        {
            "cards" => ExportCards(),
            "relics" => ExportRelics(),
            "monsters" => ExportMonsters(),
            "potions" => ExportPotions(),
            "events" => ExportEvents(),
            "powers" => ExportPowers(),
            "characters" => ExportCharacters(),
            _ => throw new KeyNotFoundException($"Unknown data collection: {collection}")
        };
    }

    private static object ExportCards()
    {
        return ModelDb.AllCards
            .OrderBy(card => card.Id.Entry, StringComparer.Ordinal)
            .Select(card =>
            {
                var dynamicValues = BuildCardDynamicValuePayloads(card);
                return new
                {
                    id = card.Id.Entry,
                    name = card.Title,
                    description = GetResolvedCardRulesText(card),
                    description_raw = GetCardRulesText(card),
                    type = card.Type.ToString(),
                    rarity = card.Rarity.ToString(),
                    target = card.TargetType.ToString(),
                    cost = card.EnergyCost.Canonical,
                    is_x_cost = card.EnergyCost.CostsX,
                    star_cost = card.CanonicalStarCost >= 0 ? (int?)card.CanonicalStarCost : null,
                    is_x_star_cost = card.HasStarCostX,
                    color = GetCardColor(card),
                    damage = FindDynamicValue(dynamicValues, "damage"),
                    block = FindDynamicValue(dynamicValues, "block"),
                    keywords = card.Keywords.Select(keyword => keyword.ToString()).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    tags = card.Tags.Select(tag => tag.ToString()).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    vars = dynamicValues.Select(value => new
                    {
                        name = value.Name,
                        base_value = value.BaseValue,
                        current_value = value.CurrentValue,
                        enchanted_value = value.EnchantedValue,
                        is_modified = value.IsModified,
                        was_just_upgraded = value.WasJustUpgraded
                    }).ToArray(),
                    upgrade = BuildCardUpgradePreview(card)
                };
            })
            .ToArray();
    }

    private static object ExportRelics()
    {
        return ModelDb.AllRelics
            .OrderBy(relic => relic.Id.Entry, StringComparer.Ordinal)
            .Select(relic => new
            {
                id = relic.Id.Entry,
                name = ResolveText(relic.Title),
                description = GetDynamicFormattedTextProperty(relic, "DynamicDescription", "Description"),
                rarity = relic.Rarity.ToString(),
                pool = relic.Pool.ToString().ToLowerInvariant(),
                is_melted = relic.IsMelted
            })
            .ToArray();
    }

    private static object ExportPotions()
    {
        return ModelDb.AllPotions
            .OrderBy(potion => potion.Id.Entry, StringComparer.Ordinal)
            .Select(potion => new
            {
                id = potion.Id.Entry,
                name = ResolveText(potion.Title),
                description = GetDynamicFormattedTextProperty(potion, "DynamicDescription", "Description"),
                rarity = potion.Rarity.ToString(),
                pool = potion.Pool.ToString().ToLowerInvariant(),
                usage = potion.Usage.ToString(),
                target_type = potion.TargetType.ToString()
            })
            .ToArray();
    }

    private static object ExportEvents()
    {
        return ModelDb.AllEvents
            .OrderBy(eventModel => eventModel.Id.Entry, StringComparer.Ordinal)
            .Select(eventModel => new
            {
                id = eventModel.Id.Entry,
                name = ResolveText(eventModel.Title),
                type = eventModel is AncientEventModel ? "Ancient" : "Event",
                act = ResolveEventAct(eventModel),
                description = ResolveText(eventModel.InitialDescription),
                options = BuildEventOptions(eventModel)
            })
            .ToArray();
    }

    private static object ExportPowers()
    {
        return ModelDb.AllPowers
            .OrderBy(power => power.Id.Entry, StringComparer.Ordinal)
            .Select(power => new
            {
                id = power.Id.Entry,
                name = ResolveText(power.Title),
                description = ResolveText(power.Description),
                type = power.Type.ToString(),
                stack_type = power.StackType.ToString(),
                allow_negative = power.AllowNegative
            })
            .ToArray();
    }

    private static object ExportCharacters()
    {
        return ModelDb.AllCharacters
            .OrderBy(character => character.Id.Entry, StringComparer.Ordinal)
            .Select(character => new
            {
                id = character.Id.Entry,
                name = ResolveText(character.Title),
                description = LocString.GetIfExists("characters", character.Id.Entry + ".description")?.GetFormattedText(),
                starting_hp = character.StartingHp,
                starting_gold = character.StartingGold,
                max_energy = character.MaxEnergy,
                orb_slots = character.BaseOrbSlotCount,
                gender = character.Gender.ToString(),
                color = character.CardPool.Title.ToLowerInvariant(),
                starting_deck = character.StartingDeck.Select(card => card.Id.Entry).ToArray(),
                starting_relics = character.StartingRelics.Select(relic => relic.Id.Entry).ToArray(),
                starting_potions = character.StartingPotions.Select(potion => potion.Id.Entry).ToArray()
            })
            .ToArray();
    }

    private static object ExportMonsters()
    {
        return ModelDb.Monsters
            .OrderBy(monster => monster.Id.Entry, StringComparer.Ordinal)
            .Select(monster => new
            {
                id = monster.Id.Entry,
                name = ResolveText(monster.Title),
                type = ResolveMonsterType(monster),
                min_hp = monster.MinInitialHp,
                max_hp = monster.MaxInitialHp,
                moves = BuildMonsterMoves(monster),
                damage_values = (object?)null,
                block_values = (object?)null
            })
            .ToArray();
    }

    private static string GetCardColor(CardModel card)
    {
        if (card.Pool.IsColorless)
        {
            return "colorless";
        }

        return card.Pool.Title.ToLowerInvariant();
    }

    private static string ResolveEventAct(EventModel eventModel)
    {
        var act = ModelDb.Acts.FirstOrDefault(candidate => candidate.AllEvents.Contains(eventModel));
        return act == null ? "Shared" : ResolveText(act.Title) ?? "Shared";
    }

    private static string ResolveMonsterType(MonsterModel monster)
    {
        var roomType = ModelDb.AllEncounters
            .Where(encounter => encounter.AllPossibleMonsters.Contains(monster))
            .Select(encounter => encounter.RoomType)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        return roomType switch
        {
            RoomType.Boss => "Boss",
            RoomType.Elite => "Elite",
            RoomType.Monster => "Normal",
            _ => "Unknown"
        };
    }

    private static object[] BuildMonsterMoves(MonsterModel monster)
    {
        var prefix = $"{monster.Id.Entry}.moves.";
        var moveNamesProperty = monster.GetType().GetProperty("MoveNames", BindingFlags.Public | BindingFlags.Instance);
        if (moveNamesProperty?.GetValue(monster) is not IEnumerable moveNames)
        {
            return Array.Empty<object>();
        }

        return moveNames
            .Cast<object>()
            .Select(locString => new
            {
                id = ExtractKeySegment(GetLocEntryKey(locString), prefix),
                name = GetFormattedLocString(locString)
            })
            .ToArray<object>();
    }

    private static string GetLocEntryKey(object value)
    {
        if (value is LocString locString)
        {
            return locString.LocEntryKey;
        }

        return value.GetType().GetProperty("LocEntryKey", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value) as string
            ?? string.Empty;
    }

    /// <summary>
    /// Formats a localized string, or returns null when its table has no entry for it.
    /// <para>
    /// ModelDb carries entries the localization tables do not cover - a MOCK_ power, for example -
    /// and <c>GetFormattedText</c> throws for those. Formatting one such entry used to turn the whole
    /// collection into a 500, so every exported name goes through this check instead.
    /// </para>
    /// </summary>
    private static string? ResolveText(LocString? locString)
    {
        return locString != null && locString.Exists() ? locString.GetFormattedText() : null;
    }

    private static string GetFormattedLocString(object value)
    {
        if (value is LocString locString)
        {
            return ResolveText(locString) ?? string.Empty;
        }

        return value.GetType().GetMethod("GetFormattedText", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
            ?.Invoke(value, null) as string
            ?? string.Empty;
    }

    private static object[] BuildEventOptions(EventModel eventModel)
    {
        try
        {
            var prefix = $"{eventModel.Id.Entry}.pages.INITIAL.options.";
            return eventModel.GameInfoOptions
                .Select(locString => locString.LocEntryKey)
                .Select(key => TrimKnownSuffix(key, ".title"))
                .Select(key => TrimKnownSuffix(key, ".description"))
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(key => new
                {
                    id = ExtractKeySegment(key, prefix),
                    title = ResolveText(eventModel.GetOptionTitle(key)) ?? string.Empty,
                    description = ResolveText(eventModel.GetOptionDescription(key)) ?? string.Empty
                })
                .ToArray<object>();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private static string ExtractKeySegment(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return key;
        }

        var suffix = key[prefix.Length..];
        var separator = suffix.IndexOf('.');
        return separator >= 0 ? suffix[..separator] : suffix;
    }

    private static string TrimKnownSuffix(string value, string suffix)
    {
        return value.EndsWith(suffix, StringComparison.Ordinal)
            ? value[..^suffix.Length]
            : value;
    }

    private static object? BuildCardUpgradePreview(CardModel card)
    {
        try
        {
            var preview = NormalizeCardRulesText(card.GetDescriptionForUpgradePreview());
            if (!string.IsNullOrWhiteSpace(preview))
            {
                return new
                {
                    description = preview
                };
            }
        }
        catch
        {
        }

        return null;
    }

    private static int? FindDynamicValue(CardDynamicValueInfo[] values, string name)
    {
        foreach (var value in values)
        {
            if (value.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return value.CurrentValue;
            }
        }

        return null;
    }

    private static CardDynamicValueInfo[] BuildCardDynamicValuePayloads(CardModel? card)
    {
        if (card == null)
        {
            return Array.Empty<CardDynamicValueInfo>();
        }

        try
        {
            var previewSet = card.DynamicVars.Clone(card);
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, card.CurrentTarget, previewSet);

            return previewSet.Values
                .Select(dynamicVar => new CardDynamicValueInfo(
                    dynamicVar.Name,
                    (int)dynamicVar.BaseValue,
                    (int)dynamicVar.PreviewValue,
                    (int)dynamicVar.EnchantedValue,
                    (int)dynamicVar.PreviewValue != (int)dynamicVar.BaseValue
                        || (int)dynamicVar.EnchantedValue != (int)dynamicVar.BaseValue,
                    dynamicVar.WasJustUpgraded))
                .OrderBy(value => value.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<CardDynamicValueInfo>();
        }
    }

    private static string GetCardRulesText(CardModel? card)
    {
        if (card == null)
        {
            return string.Empty;
        }

        try
        {
            var rawDescription = card.Description?.GetRawText();
            if (!string.IsNullOrWhiteSpace(rawDescription))
            {
                return NormalizeCardRulesText(rawDescription);
            }
        }
        catch
        {
        }

        foreach (var memberName in new[]
        {
            "Description",
            "RulesText",
            "Body",
            "Text",
            "RawText",
            "DescriptionText"
        })
        {
            var text = TryReadCardTextMember(card, memberName);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return NormalizeCardRulesText(text);
            }
        }

        return string.Empty;
    }

    private static string GetResolvedCardRulesText(CardModel? card)
    {
        if (card == null)
        {
            return string.Empty;
        }

        try
        {
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, card.CurrentTarget, card.DynamicVars);
            var pileType = card.Pile?.Type ?? PileType.None;
            var resolved = card.GetDescriptionForPile(pileType, card.CurrentTarget);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return NormalizeCardRulesText(resolved);
            }
        }
        catch
        {
        }

        return GetCardRulesText(card);
    }

    private static string TryReadCardTextMember(object instance, string memberName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        try
        {
            var property = instance.GetType().GetProperty(memberName, flags);
            if (property != null)
            {
                return TryCoerceText(property.GetValue(instance));
            }

            var field = instance.GetType().GetField(memberName, flags);
            if (field != null)
            {
                return TryCoerceText(field.GetValue(instance));
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string TryCoerceText(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var valueType = value.GetType();

        try
        {
            var getRawText = valueType.GetMethod("GetRawText", flags, null, Type.EmptyTypes, null);
            if (getRawText != null && getRawText.ReturnType == typeof(string))
            {
                return getRawText.Invoke(value, null) as string ?? string.Empty;
            }
        }
        catch
        {
        }

        return value.ToString() ?? string.Empty;
    }

    private static string NormalizeCardRulesText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = CardMarkupRegex.Replace(value, string.Empty);
        normalized = CardWhitespaceRegex.Replace(normalized, " ");
        return normalized.Trim();
    }

    private static object? GetReflectedProperty(object target, string propertyName)
    {
        try
        {
            return target.GetType().GetProperty(propertyName)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetReflectedFormattedTextProperty(object target, string propertyName)
    {
        var value = GetReflectedProperty(target, propertyName);
        return value == null ? null : TryCoerceText(value);
    }

    private static string? GetDynamicFormattedTextProperty(object target, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetReflectedFormattedTextProperty(target, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private readonly record struct CardDynamicValueInfo(
        string Name,
        int BaseValue,
        int CurrentValue,
        int EnchantedValue,
        bool IsModified,
        bool WasJustUpgraded);
}
