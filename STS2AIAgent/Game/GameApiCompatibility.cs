using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace STS2AIAgent.Game;

/// <summary>Public game APIs whose types, signatures, or enum values differ between the two Steam branches.</summary>
internal static class GameApiCompatibility
{
    private static readonly Assembly GameAssembly = typeof(RunState).Assembly;

    // The stable game has no Fool types or card tags.
    private static readonly Type? ArrowType = GameAssembly.GetType("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot");
    private static readonly Type? ArrowCommands = GameAssembly.GetType("MegaCrit.Sts2.Core.Commands.ArrowCmd");
    private static readonly Type? MelodyCommands = GameAssembly.GetType("MegaCrit.Sts2.Core.Commands.MelodyCmd");
    private static readonly PropertyInfo? Arrows = PublicProperty(typeof(PlayerCombatState), "Arrows");
    private static readonly PropertyInfo? ArrowStatus = PublicProperty(ArrowType, "Status");
    private static readonly PropertyInfo? ArrowAvailable = PublicProperty(ArrowType, "IsAvailable");
    private static readonly PropertyInfo? BaseArrowSlots = PublicProperty(typeof(CharacterModel), "BaseArrowSlotCount");
    private static readonly Func<Player, int>? ReadArrowCount =
        Method(ArrowCommands, "GetAvailableArrowCount", true, [typeof(Player)])?.CreateDelegate<Func<Player, int>>();
    private static readonly Func<Player, bool>? PlayedWah = ReadMelodyFlag("PlayedWahThisTurn");
    private static readonly Func<Player, bool>? PlayedCho = ReadMelodyFlag("PlayedChoThisTurn");
    private static readonly Func<Player, bool>? PlayedKi = ReadMelodyFlag("PlayedKiThisTurn");
    private static readonly CardTag? ShootTag = FindTag("Shoot");
    private static readonly CardTag? WahTag = FindTag("Wah");
    private static readonly CardTag? ChoTag = FindTag("Cho");
    private static readonly CardTag? KiTag = FindTag("Ki");

    // The beta adds two lobby screens and splits the ascension cap.
    private static readonly Type? BlitzScreen = GameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen");
    private static readonly Type? DreadScreen = GameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen");
    private static readonly PropertyInfo? BlitzLobby = PublicProperty(BlitzScreen, "Lobby");
    private static readonly PropertyInfo? DreadLobby = PublicProperty(DreadScreen, "Lobby");
    private static readonly Type? RotatingCharacter = GameAssembly.GetType("MegaCrit.Sts2.Core.Models.Characters.RotatingCharacter");
    private static readonly FieldInfo? StandardAscension = ReflectedMemberResolver.Field(typeof(StartRunLobbyPlayer), "maxStandardAscension", false)
        ?? ReflectedMemberResolver.Field(typeof(StartRunLobbyPlayer), "maxMultiplayerAscensionUnlocked", false);
    private static readonly FieldInfo? BlitzAscension = ReflectedMemberResolver.Field(typeof(StartRunLobbyPlayer), "maxBlitzAscension", false);
    private static readonly FieldInfo? RotatingDread = ReflectedMemberResolver.Field(typeof(StartRunLobbyPlayer), "isRotatingDread", false);
    private static readonly PropertyInfo? Dominion = PublicProperty(typeof(RunState), "Dominion");
    private static readonly PropertyInfo? CustomRun = PublicProperty(typeof(RunState), "IsCustomGame");
    private static readonly PropertyInfo? PlayingRotating = PublicProperty(typeof(ExtraPlayerFields), "IsPlayingRotatingCharacter");
    private static readonly Func<int, GameMode, LocString> AscensionTitle = BindAscensionText("GetTitle");
    private static readonly Func<int, GameMode, LocString> AscensionDescription = BindAscensionText("GetDescription");

    // The older game uses the native entry wrapper. The beta synchronizes purchases explicitly.
    private static readonly PropertyInfo? MerchantSynchronizer = PublicProperty(typeof(RunManager), "MerchantSynchronizer");
    private static readonly MethodInfo? SynchronizedPurchase = Method(
        MerchantSynchronizer?.PropertyType, "PurchaseLocalEntry", false, [typeof(MerchantEntry)]);
    private static readonly MethodInfo? LegacyPurchase = Method(
        typeof(MerchantEntry), "OnTryPurchaseWrapper", false, [typeof(MerchantInventory), typeof(bool)]);
    private static readonly Type? BlitzEntryType = GameAssembly.GetType("MegaCrit.Sts2.Core.Entities.Merchant.BlitzMerchantEntry");
    private static readonly PropertyInfo? BlitzShop = PublicProperty(typeof(MerchantInventory), "IsBlitzShop");
    private static readonly PropertyInfo? BlitzChoices = PublicProperty(typeof(MerchantInventory), "BlitzChoicesRemaining");
    private static readonly PropertyInfo? BlitzEntries = PublicProperty(typeof(MerchantInventory), "BlitzEntries");
    private static readonly PropertyInfo? BlitzEntryKind = PublicProperty(BlitzEntryType, "MerchantEntryType");
    private static readonly PropertyInfo? BlitzEntryTitle = PublicProperty(BlitzEntryType, "Title");
    private static readonly PropertyInfo? BlitzEntryDescription = PublicProperty(BlitzEntryType, "Description");
    private static readonly PropertyInfo? BlitzEntryStatus = PublicProperty(BlitzEntryType, "Status");
    private static readonly PropertyInfo? BlitzEntryCursed = PublicProperty(BlitzEntryType, "IsCursed");
    private static readonly PropertyInfo? BlitzEntryRare = PublicProperty(BlitzEntryType, "IsRare");

    private static readonly MethodInfo? ProceedRewards = Method(typeof(RunManager), "ProceedFromTerminalRewardsScreen", false, Type.EmptyTypes);
    private static readonly Func<ICombatState, Creature, decimal, ValueProp, Creature, Creature?>? PreBlockTarget =
        Method(typeof(Hook), "ModifyPreBlockDamageTarget", true,
            [typeof(ICombatState), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(Creature)])
        ?.CreateDelegate<Func<ICombatState, Creature, decimal, ValueProp, Creature, Creature?>>();

    // C# inlines enum values. Resolve the names on the loaded game for the phases that moved.
    public static readonly HpLossHookPhase BeforeOsty = Enum.Parse<HpLossHookPhase>(nameof(HpLossHookPhase.BeforeOsty));
    public static readonly HpLossHookPhase AfterOsty = Enum.Parse<HpLossHookPhase>(nameof(HpLossHookPhase.AfterOsty));

    public static CombatArrowPayload[] BuildArrows(PlayerCombatState combat)
    {
        if (Arrows?.GetValue(combat) is not IEnumerable arrows)
        {
            return [];
        }
        return arrows.Cast<object>().Select((arrow, index) => new CombatArrowPayload
        {
            index = index,
            status = ArrowStatus?.GetValue(arrow)?.ToString()?.ToLowerInvariant() ?? string.Empty,
            available = ArrowAvailable?.GetValue(arrow) is true,
            destroy_after_impaled = false
        }).ToArray();
    }

    public static CombatMelodyPayload BuildMelody(Player player, CombatState combat)
    {
        if (MelodyCommands == null)
        {
            return new();
        }
        var wah = PlayedWah?.Invoke(player) == true;
        var cho = PlayedCho?.Invoke(player) == true;
        var ki = PlayedKi?.Invoke(player) == true;
        return new CombatMelodyPayload
        {
            wah_played = wah,
            cho_played = cho,
            ki_played = ki,
            melodies_this_turn = (wah ? 1 : 0) + (cho ? 1 : 0) + (ki ? 1 : 0),
            melody_cards_this_turn = CombatManager.Instance.History.CardPlaysFinished.Count(entry =>
                entry.CardPlay.Player == player && entry.HappenedThisTurn(combat) &&
                entry.CardPlay.Card.Tags.Any(IsMelodyTag))
        };
    }

    public static bool ShootsArrow(CardModel card) => ShootTag is CardTag tag && card.Tags.Contains(tag);
    private static bool IsMelodyTag(CardTag tag) => tag == WahTag || tag == ChoTag || tag == KiTag;
    public static int AvailableArrows(Player player) => ReadArrowCount?.Invoke(player) ?? 0;
    public static int InitialArrows(CharacterModel character) => (int?)BaseArrowSlots?.GetValue(character) ?? 0;

    public static string? ExtraRunMode(IScreenContext? screen) =>
        BlitzScreen?.IsInstanceOfType(screen) == true ? "blitz" :
        DreadScreen?.IsInstanceOfType(screen) == true ? "dread" : null;

    public static StartRunLobby? ExtraRunLobby(IScreenContext? screen) => ExtraRunMode(screen) switch
    {
        "blitz" => BlitzLobby?.GetValue(screen) as StartRunLobby,
        "dread" => DreadLobby?.GetValue(screen) as StartRunLobby,
        _ => null
    };

    public static bool IsRotatingCharacter(CharacterModel? character) => RotatingCharacter?.IsInstanceOfType(character) == true;
    public static bool IsPlayingRotatingCharacter(ExtraPlayerFields fields) => PlayingRotating?.GetValue(fields) is true;
    public static int MaxStandardAscension(StartRunLobbyPlayer player) => (int?)StandardAscension?.GetValue(player) ?? 0;
    public static int MaxBlitzAscension(StartRunLobbyPlayer player) => (int?)BlitzAscension?.GetValue(player) ?? 0;
    public static bool IsRotatingDread(StartRunLobbyPlayer player) => RotatingDread?.GetValue(player) is true;
    public static string? DominionId(RunState run) => (Dominion?.GetValue(run) as AbstractModel)?.Id.Entry;
    public static bool IsCustomGame(RunState run) => (bool?)CustomRun?.GetValue(run) ?? run.GameMode.ToString() == "Custom";
    public static LocString GetAscensionTitle(int level, GameMode mode) => AscensionTitle(level, mode);
    public static LocString GetAscensionDescription(int level, GameMode mode) => AscensionDescription(level, mode);

    public static bool IsBlitzShop(MerchantInventory inventory) => BlitzShop?.GetValue(inventory) is true;
    public static int RemainingBlitzChoices(MerchantInventory inventory) => (int?)BlitzChoices?.GetValue(inventory) ?? 0;
    public static IReadOnlyList<MerchantEntry> ReadBlitzEntries(MerchantInventory? inventory) =>
        inventory != null && BlitzEntries?.GetValue(inventory) is IReadOnlyList<MerchantEntry> entries ? entries : [];

    public static ShopBlitzEntryPayload BuildBlitzEntry(MerchantEntry entry, int index) => new()
    {
        index = index,
        entry_type = BlitzEntryKind?.GetValue(entry)?.ToString() ?? string.Empty,
        name = (BlitzEntryTitle?.GetValue(entry) as LocString)?.GetFormattedText() ?? string.Empty,
        description = (BlitzEntryDescription?.GetValue(entry) as LocString)?.GetFormattedText() ?? string.Empty,
        status = BlitzEntryStatus?.GetValue(entry)?.ToString()?.ToLowerInvariant() ?? string.Empty,
        is_stocked = entry.IsStocked,
        is_cursed = BlitzEntryCursed?.GetValue(entry) is true,
        is_rare = BlitzEntryRare?.GetValue(entry) is true
    };

    public static Task<bool> Purchase(MerchantInventory inventory, MerchantEntry entry)
    {
        if (MerchantSynchronizer?.GetValue(RunManager.Instance) is { } synchronizer)
        {
            return SynchronizedPurchase?.Invoke(synchronizer, [entry]) as Task<bool>
                ?? Task.FromException<bool>(new MissingMethodException("PurchaseLocalEntry"));
        }
        if (MerchantSynchronizer == null && LegacyPurchase?.Invoke(entry, [inventory, false]) is Task<bool> legacy)
        {
            return legacy;
        }
        return Task.FromException<bool>(new InvalidOperationException("No compatible merchant purchase entry point."));
    }

    public static Task? ProceedTerminalRewards(RunManager manager) =>
        (ProceedRewards ?? throw new MissingMethodException("ProceedFromTerminalRewardsScreen")).Invoke(manager, null) as Task;

    public static bool RedirectsPreBlockDamage(ICombatState combat, Creature target, decimal amount, ValueProp props, Creature source) =>
        PreBlockTarget?.Invoke(combat, target, amount, props, source) != null;

    private static CardTag? FindTag(string name) => Enum.TryParse<CardTag>(name, out var tag) ? tag : null;

    private static Func<Player, bool>? ReadMelodyFlag(string name) =>
        Method(MelodyCommands, name, true, [typeof(Player)])?.CreateDelegate<Func<Player, bool>>();

    private static Func<int, GameMode, LocString> BindAscensionText(string name)
    {
        var type = typeof(AscensionHelper);
        if (Method(type, name, true, [typeof(int), typeof(GameMode)]) is { } current)
        {
            return current.CreateDelegate<Func<int, GameMode, LocString>>();
        }
        var legacy = Method(type, name, true, [typeof(int)])
            ?.CreateDelegate<Func<int, LocString>>();
        return (level, _) => (legacy ?? throw new MissingMethodException(type.Name, name))(level);
    }

    private static PropertyInfo? PublicProperty(Type? type, string name) =>
        type != null && ReflectedMemberResolver.Property(type, name, isStatic: false) is { GetMethod.IsPublic: true } property
            ? property : null;

    private static MethodInfo? Method(Type? type, string name, bool isStatic, Type[] parameters) =>
        type != null && ReflectedMemberResolver.Method(type, name, isStatic, parameters) is { IsPublic: true } method
            ? method : null;
}
