using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// The availability predicates moved to <c>GameStateService.Predicates.cs</c> on 2026-09-20.
/// </summary>
/// <remarks>
/// The move was mechanical: same signatures, same bodies, same relative order, and one
/// <c>partial</c> class either way, so nothing in the mod could observe it. What can observe it is
/// source text -- and most of this repo's verification reads source text by path. This contract is
/// the answer for this split, and it pins three things that a hand-copied list of names cannot:
/// <list type="number">
/// <item><see cref="MovedPredicatesMatchTheirPreSplitBodies"/> compares each declaration's SHA-256
/// against <see cref="PreSplitBodyHashes"/>, which was computed from the file at the parent commit.
/// A rewritten body fails here even if the signature and name are unchanged.</item>
/// <item><see cref="MovedPredicatesAreDeclaredInThePredicatePartialInSourceOrder"/> pins where each
/// member is <em>declared</em>, so a member put back in the wrong file fails instead of being
/// invisible.</item>
/// <item><see cref="SharedHelpersAreDeclaredOnceAcrossTheSplit"/> pins the members that are
/// legitimately read by the raw <c>/state</c> builders too. Those live somewhere in the builder
/// family rather than in the predicate partial, and a second copy of one is what this catches.</item>
/// </list>
/// The hash table is the durable part: regenerating it is a deliberate act, and it only matches if
/// the declaration text is byte-identical to what the split moved.
/// </remarks>
internal static class PredicateRelocationContractTests
{
    private const string BaseFile = "STS2AIAgent/Game/GameStateService.cs";
    private const string PredicateFile = "STS2AIAgent/Game/GameStateService.Predicates.cs";
    private const string ShapeContractFile = "STS2AIAgent.Tests/SourceShapeContractTests.cs";

    /// <summary>
    /// The pre-split budget for <see cref="BaseFile"/> in <c>SourceShapeContractTests</c>. The split
    /// has to come in under it, because budgets here go down and never up.
    /// </summary>
    private const int PreSplitBaseBudget = 6000;

    /// <summary>
    /// Every predicate the split moved, in the order the base file declared them. The two
    /// <c>IsPlayerActionPhase</c> entries are its two overloads.
    /// </summary>
    private static readonly string[] MovedPredicates =
    {
        "CanEndTurn",
        "CanPlayAnyCard",
        "CanChooseMapNode",
        "CanCollectRewardsAndProceed",
        "CanClaimReward",
        "CanChooseRewardCard",
        "CanSkipRewardCards",
        "CanSelectDeckCard",
        "CanCloseCardsView",
        "IsClosableCardViewer",
        "CanConfirmSelection",
        "CanProceed",
        "CanOpenChest",
        "CanChooseTreasureRelic",
        "CanChooseEventOption",
        "CanChooseCapstoneOption",
        "CanPlayCrystalSphere",
        "IsCapstonePageOverlay",
        "CanChooseBundle",
        "CanConfirmBundle",
        "CanChooseRestOption",
        "CanOpenShopInventory",
        "CanCloseShopInventory",
        "CanBuyShopCard",
        "CanBuyShopRelic",
        "CanBuyShopPotion",
        "CanRemoveCardAtShop",
        "CanSelectCharacter",
        "CanSwitchProfile",
        "CanContinueRun",
        "CanAbandonRun",
        "CanSaveAndQuit",
        "CanOpenCharacterSelect",
        "CanOpenTimeline",
        "CanCloseMainMenuSubmenu",
        "CanInviteAiTeammate",
        "CanContinueAiTeammate",
        "CanEmbark",
        "CanUnready",
        "CanHostMultiplayerLobby",
        "CanJoinMultiplayerLobby",
        "CanReadyMultiplayerLobby",
        "CanDisconnectMultiplayerLobby",
        "CanIncreaseAscension",
        "CanDecreaseAscension",
        "CanChooseTimelineEpoch",
        "CanConfirmTimelineOverlay",
        "CanUsePotion",
        "CanUsePotionAtIndex",
        "CanDiscardPotion",
        "CanDiscardPotionAtIndex",
        "CanConfirmModal",
        "CanDismissModal",
        "CanReturnToMainMenu",
        "CanContinueGameOver",
        "IsCardPlayable",
        "IsCombatActionReady",
        "CanUseCombatActions",
        "IsGameOverSummaryStarted",
        "CanConfirmUnlock",
        "CanAdjustAscension",
    };

    /// <summary>
    /// SHA-256 of each moved declaration's exact text (trimmed of trailing whitespace, line endings
    /// normalised to <c>\n</c>), computed from <c>GameStateService.cs</c> at the commit before the
    /// split. A body rewritten "equivalently" hashes differently.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PreSplitBodyHashes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CanEndTurn"] = "01b2761afc9d0da0ea840351abe2e6edc2fbbacd34bd17727c3d33e1f6a11aab",
        ["CanPlayAnyCard"] = "37f592d4a60daee27113d7ec269eb5319cd48decad23d9e4c52b7160691d8634",
        ["CanChooseMapNode"] = "2e3afc2310ac13a3cefec4d2583e2cbc3b5848ee290c6e8a823dda99fbe2f924",
        ["CanCollectRewardsAndProceed"] = "497bd2aba4a11deecc2751e196137e9b361e46a0c84edbdf66d3946001601a24",
        ["CanClaimReward"] = "b8d35aaa52bb905c3227cb94d770f56930abe905f25cfa25d0659c0a05d7c860",
        ["CanChooseRewardCard"] = "3f72e5d7a0235d7d1e5f460fd5d49c2141161001e6ff531c82bcd4790c23c787",
        ["CanSkipRewardCards"] = "8e1e009f6eb3f55acc84d57ac12f141edce3bb55b5855dd82703d366bc46ec6b",
        ["CanSelectDeckCard"] = "431892fd136e65182a29484538e54313f4be2988103607d90bf9cc87aec09824",
        ["CanCloseCardsView"] = "d62940a1d25ecee3d06e38ecb7cca95956a609a84f0d5ee0f561a58976999569",
        ["IsClosableCardViewer"] = "0cce7036722ee64baf4b3722427fbfc2288bac9b86006f458029d649dd39bcc1",
        ["CanConfirmSelection"] = "c3d20b4369c9b9407b89520821923ed48f5932092a4aa2696ea5b710e1522e7f",
        ["CanProceed"] = "d98f91ad14646d407b62ff240b059bedf71cbd51d937cec05758cdc9a24f9de2",
        ["CanOpenChest"] = "9bd22c228b9f7400fb78c69e4429dfcb68e295f0d3a118735a596cac43cc8803",
        ["CanChooseTreasureRelic"] = "feee28963f1a3a358f56f00f950bdeaa35002e8f2db5505e02eeb0124fc06a9b",
        ["CanChooseEventOption"] = "a7de7a34adf270cf6cf23ed736f539df5fc11639575edad5b1091bd59e54a1ab",
        ["CanChooseCapstoneOption"] = "72ab4e2828075e9608556f5bc2685afa4c98405ceb5f5682ac9690200b598c1a",
        ["CanPlayCrystalSphere"] = "3500e749f8c3a66bb062f157fa925152f14757922b8273345cd0a82d176fc66d",
        ["IsCapstonePageOverlay"] = "1446b517f1e2c7e222d27bc8cd1cefe989d23b731ba117b8070a7d0c63d477e9",
        ["CanChooseBundle"] = "a0f97ac4ef8c6c377260ddf3c4c3cd6a3077371facccaa16d608acaf2c35684f",
        ["CanConfirmBundle"] = "acdf28c89791b00ceb13e1a496fee940aa365c40a098b63754cdd05a343565d2",
        ["CanChooseRestOption"] = "99423e345e27e9889fb2127f3d05cb06360050bc5712748234a1bd5b6f750a01",
        ["CanOpenShopInventory"] = "eca86362a15f83b1fbe69273834049616f5eb71bc47352140b37ed630a55d3fc",
        ["CanCloseShopInventory"] = "90b6342b6b47d8c7cb4017253acee73940483cda8ebae4c45f1d9376d56ce051",
        ["CanBuyShopCard"] = "95f5b6dfa50dc64cccda7ad5caaefade9af848fac0d36e286b6258d2b924aa83",
        ["CanBuyShopRelic"] = "7b21db2ceabd051d68c3fe489b55f4e4a2f71c3d1c6a7c7f0a62d486bd51e324",
        ["CanBuyShopPotion"] = "c3c9c1cc4ef3a51cf79e17a999fa9d9ba97823beb443050227156d38e4ebf2e7",
        ["CanRemoveCardAtShop"] = "cb25daba5c50bd937412c614d71a2058729a7b2eca94edf1e1a05761ffb660b7",
        ["CanSelectCharacter"] = "dd5617d96984230fc2e801dc34abb71dfcec53684fd0105e6d4cbc2eba5235d5",
        ["CanSwitchProfile"] = "1b04286d541410a7d0c03e2eb4c18e740e4d356a135a032269e2a8b014102362",
        ["CanContinueRun"] = "c71dadd195a28495564ef486a520752a5787a516ec55e644b91c6bb0cd8ac755",
        ["CanAbandonRun"] = "f2a28179cf285bfb8d6bf3138de50e6c833dc3981d2d29a3cbee3fa97ffe7e2b",
        ["CanSaveAndQuit"] = "35faf007df19c5a9ebebe6cf74ebbd8ab49bf431ac2abb4dadc97415545a70cf",
        ["CanOpenCharacterSelect"] = "92ffab8f2ffa78d5c19f87a0b33eef3c5f786bef42ebce3a37f2478241462485",
        ["CanOpenTimeline"] = "81883b112d5c97ec915bf4b1fca09a844e2288d32582d695e02cdf655b81c0c9",
        ["CanCloseMainMenuSubmenu"] = "631ab45013f334501d2dae2963a9f2cffa8fb7fb17df9d70e8c6c0116c336d0f",
        ["CanInviteAiTeammate"] = "72932b5cf41771677470b99740abc2f07f19a731361310e21182d328f048acfc",
        ["CanContinueAiTeammate"] = "7938e106f94baacf65fd4772a8aa0d1d937e8de6c5ae6c34e6f31b53c6d51a19",
        ["CanEmbark"] = "48a9f6b0e3470f0c486788ae25a944a257edb2973e4649fca6472889667458e9",
        ["CanUnready"] = "e15c655f98a0c783ca20bd92db047f4088af4c91261e9ca557210ab284132f27",
        ["CanHostMultiplayerLobby"] = "03b6043ddf89fccf90a6cea3ab1404af396f5c000c3967f96cb91fe04f16d7e1",
        ["CanJoinMultiplayerLobby"] = "07b4d5c2ec4447bff30158d7650a28e541de3c50c0eae449d2a5ded2563b49ab",
        ["CanReadyMultiplayerLobby"] = "7670509b0514a29797a14d358118a33f935098438a61c44fc0dfaeaaae6af3d4",
        ["CanDisconnectMultiplayerLobby"] = "015c9a4f5b046b5450d9c367829ec0564c51c639f78a00367e0c53047e43d9bc",
        ["CanIncreaseAscension"] = "9bba754382b93be36c57d357f115c922e2bd308993c619d4e3ce464dd207457b",
        ["CanDecreaseAscension"] = "3606056ba0875d594c0c4ec4818c4ac7ee9954d09ca3a37ae833df091dca4402",
        ["CanChooseTimelineEpoch"] = "f022a53db85d83df318bff3c378599391742d177b29bf8f920cea63b43fa2322",
        ["CanConfirmTimelineOverlay"] = "c59abf7a838504bd11c29be5ee7b4f8f9a36c04963e684c6b7084ff7464474c3",
        ["CanUsePotion"] = "08ff48b408d28112d1fc84a2b466023c7f6292356dfe7d203e7ec3298e0d4832",
        ["CanUsePotionAtIndex"] = "c8ec09380de352bdd7f6c5c6f50cc07f10820da453cdbefa3111c82ca337124e",
        ["CanDiscardPotion"] = "5ec9dde437e187e378f988dbcebe103e34ac15bea5b76303eed6a93a0b8a7295",
        ["CanDiscardPotionAtIndex"] = "524ec0f81ffc91899e49beace83daa9a4a722fc1220614b33ea7f4eb2f606969",
        ["CanConfirmModal"] = "e72f353f909b3d9855a57dd3f46cecc6350e8d67bfaef7b7d572a5a0a94023ba",
        ["CanDismissModal"] = "8272443dc069b9cbbb82f01889f7d76fa129765b68b13a1441d07d8379c053c5",
        ["CanReturnToMainMenu"] = "da1d0eb5ec737f37568c0466c59dcdabcba6987f98ba04dc57175444e3f9e813",
        ["CanContinueGameOver"] = "7bd240995cc9fada888502671a2d319472a42ce2216dfff898353b205a07782e",
        ["IsCardPlayable"] = "281ba6c97384d9226fa53c3fac33ececda83c743719b5130e7b6bc6be76cbbe9",
        ["IsCombatActionReady"] = "dd9a5fe152ec19ff45ec2590938f08323a11365947ce462f5a3311837edce2a3",
        ["CanUseCombatActions"] = "521708d9dafa05a421be19a403cf8ab7be50c9a5f52ce076379a550c5318e87e",
        ["IsGameOverSummaryStarted"] = "443827cfc15a1026dcab12f45a2e482b40182393b9001e8af77d728d98de26b3",
        ["CanConfirmUnlock"] = "e391d0846d8c1927e584df5f978438a88210bed8de3c4283dafe0b451bd92315",
        ["CanAdjustAscension"] = "5500d5f39d68e084dc963b3fb6a7ac8d983541b63816f766609126880bde113e",
    };

    /// <summary>
    /// Helpers the raw <c>/state</c> builders and the action services read as well as the
    /// predicates, so they stayed in the base file. Moving one of these would be a reorganisation,
    /// not a predicate split.
    /// </summary>
    private static readonly string[] SharedHelpersLeftInBase =
    {
        "IsPlayerActionPhase",
        "IsCardTargetSupported",
        "IsEndTurnButtonReady",
        "IsWaitingForOtherPlayers",
        "CanPurchaseShopPotion",
        "CanDiscardPotionsInCurrentScreen",
        "IsLocalCombatTurnReady",
        "IsCombatActionSnapshotStable",
        "IsPotionUsable",
        "IsPotionDiscardable",
        "IsPotionTargetSupported",
        "IsGameOverButtonReady",
        "IsKnownCapstoneContainerPage",
    };

    /// <summary>
    /// The relocation proof: every moved declaration in the predicate partial still hashes to the
    /// text the base file carried before the split, so nobody can quietly rewrite a body and keep
    /// calling it a move.
    /// </summary>
    public static void MovedPredicatesMatchTheirPreSplitBodies()
    {
        var predicates = AgentSourceFixture.Read(PredicateFile);
        var mismatches = new List<string>();
        var missing = new List<string>();

        foreach (var (name, expectedHash) in PreSplitBodyHashes)
        {
            var body = DeclarationText(predicates, name, 0);
            if (body == null)
            {
                missing.Add(name);
                continue;
            }

            var actual = Hash(body);
            if (!string.Equals(actual, expectedHash, StringComparison.Ordinal))
            {
                mismatches.Add($"{name}: expected {expectedHash[..12]}, actual {actual[..12]}");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{PredicateFile} no longer declares: " + string.Join(", ", missing));
        Assert.True(
            mismatches.Count == 0,
            "These predicates no longer match the text the split moved. A relocation may not rewrite "
            + "a body; if the change is intentional, regenerate PreSplitBodyHashes and say so in the "
            + "commit message:\n  " + string.Join("\n  ", mismatches));
    }

    public static void MovedPredicatesAreDeclaredInThePredicatePartialInSourceOrder()
    {
        var predicates = AgentSourceFixture.Read(PredicateFile);
        var baseFile = AgentSourceFixture.Read(BaseFile);

        var searchFrom = 0;
        var baseOffenders = new List<string>();

        foreach (var name in MovedPredicates)
        {
            var index = DeclarationIndex(predicates, name, searchFrom);
            Assert.True(
                index >= 0,
                $"{PredicateFile} does not declare {name} at or after offset {searchFrom}. Either the "
                + "predicate moved back, or the members were reordered -- both make this a "
                + "reorganisation, not the relocation the split claimed to be.");
            searchFrom = index + 1;

            if (DeclarationIndex(baseFile, name, 0) >= 0)
            {
                baseOffenders.Add(name);
            }
        }

        Assert.True(
            baseOffenders.Count == 0,
            $"{BaseFile} still declares predicate(s) that the split moved: "
            + string.Join(", ", baseOffenders)
            + ". The predicates live in " + PredicateFile + " now; a second copy in the base file is "
            + "how two sources of truth start.");
    }

    public static void SharedHelpersAreDeclaredOnceAcrossTheSplit()
    {
        var predicates = AgentSourceFixture.Read(PredicateFile);
        var builders = BuilderPartials()
            .Select(AgentSourceFixture.Read)
            .ToList();

        var missing = new List<string>();
        var swept = new List<string>();
        var duplicated = new List<string>();

        foreach (var name in SharedHelpersLeftInBase)
        {
            // "In exactly one file" is the invariant, not "declared once": `IsPlayerActionPhase` is
            // an overload pair, and two overloads in one file is a signature, not a duplicate. What
            // must not happen is the same helper appearing in two partials, or in the predicate
            // partial, whose members all have to be about action availability.
            var declaringFiles = builders.Count(source => DeclarationIndex(source, name, 0) >= 0);
            if (declaringFiles == 0)
            {
                missing.Add(name);
            }
            else if (declaringFiles > 1)
            {
                duplicated.Add($"{name} ({declaringFiles} files)");
            }

            if (DeclarationIndex(predicates, name, 0) >= 0)
            {
                swept.Add(name);
            }
        }

        Assert.True(
            missing.Count == 0,
            "the GameStateService builder partials no longer declare shared helper(s): "
            + string.Join(", ", missing)
            + ". The raw /state builders read these too, so they belong beside the builders.");
        Assert.True(
            duplicated.Count == 0,
            "these shared helpers are declared in more than one builder partial: "
            + string.Join(", ", duplicated)
            + ". A partial class compiles a duplicate in silence until someone calls it, which is "
            + "how two sources of truth start.");
        Assert.True(
            swept.Count == 0,
            $"{PredicateFile} declares shared helper(s) the raw builders also read: "
            + string.Join(", ", swept)
            + ". Only members whose whole job is action availability belong in the predicate file.");
    }

    /// <summary>
    /// The builder side of the family: every <c>GameStateService*.cs</c> partial except the three
    /// that own something else (predicates, the compact view, the payload declarations).
    /// </summary>
    private static IReadOnlyList<string> BuilderPartials()
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            PredicateFile,
            "STS2AIAgent/Game/GameStateService.AgentView.cs",
            "STS2AIAgent/Game/GameStateService.Payloads.cs",
        };
        var files = AgentSourceFixture.SourceFiles()
            .Select(path => Path.GetRelativePath(AgentSourceFixture.Root, path).Replace('\\', '/'))
            .Where(path => path.StartsWith("STS2AIAgent/Game/GameStateService", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !excluded.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            files.Count >= 6,
            $"the GameStateService builder family is {files.Count} file(s): the split is no longer "
            + "there, or this walk stopped seeing it.");
        return files;
    }

    public static void TheSplitLoweredTheBaseBudgetInsteadOfRaisingIt()
    {
        var shape = AgentSourceFixture.Read(ShapeContractFile);
        var match = Regex.Match(
            shape,
            @"\[""STS2AIAgent/Game/GameStateService\.cs""\]\s*=\s*(\d+)\s*,");
        Assert.True(
            match.Success,
            $"{ShapeContractFile} no longer carries a size budget for {BaseFile}. A split that "
            + "removes its own ratchet entry is a split that removed the evidence.");

        var budget = int.Parse(match.Groups[1].Value);
        var baseLines = LineCount(BaseFile);
        Assert.True(
            budget < PreSplitBaseBudget,
            $"the budget for {BaseFile} is {budget}, at or above the {PreSplitBaseBudget} it carried "
            + "before the predicates moved out. Budgets here go down, never up.");
        Assert.True(
            budget >= baseLines,
            $"the budget for {BaseFile} is {budget} but the file is {baseLines} lines, so the entry "
            + "no longer guards anything.");

        // The predicate partial came in under the default cap, so it must not have been handed a
        // named budget: declaring a new file a monolith is exactly what the ratchet asks you not
        // to do. If it ever grows past the cap, split it rather than listing it.
        var predicateLines = LineCount(PredicateFile);
        Assert.False(
            Regex.IsMatch(shape, @"\[""STS2AIAgent/Game/GameStateService\.Predicates\.cs""\]"),
            $"{ShapeContractFile} gives {PredicateFile} a named budget. It fits the default cap; "
            + "if it stops fitting, move a concern out instead of raising the number.");
        Assert.True(
            predicateLines <= 1000,
            $"{PredicateFile} is {predicateLines} lines and no longer fits the 1000-line default "
            + "cap, so the split moved the problem rather than solving it.");
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// The full declaration text at the declaration of <paramref name="methodName"/> at or after
    /// <paramref name="startIndex"/>, normalised the way the baseline hashes were, or <c>null</c>.
    /// </summary>
    private static string? DeclarationText(string source, string methodName, int startIndex)
    {
        var start = DeclarationIndex(source, methodName, startIndex);
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var seenBrace = false;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
                seenBrace = true;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (seenBrace && depth == 0)
                {
                    return source[start..(i + 1)].Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Index of the declaration of <paramref name="methodName"/> at or after
    /// <paramref name="startIndex"/>, or -1. The modifier prefix is what separates a declaration
    /// from a call site, which is the distinction every source contract in this repo has to make.
    /// </summary>
    private static int DeclarationIndex(string source, string methodName, int startIndex)
    {
        var pattern = new Regex(
            @"^[ \t]*(?:public|private|internal|protected)[ \t]+(?:static[ \t]+)?[^\n(]*?\b"
            + Regex.Escape(methodName)
            + @"[ \t]*\(",
            RegexOptions.Multiline);
        var match = pattern.Match(source, startIndex);
        return match.Success ? match.Index : -1;
    }

    private static int LineCount(string relativePath)
    {
        var absolute = Path.Combine(
            AgentSourceFixture.Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllLines(absolute).Length;
    }
}
