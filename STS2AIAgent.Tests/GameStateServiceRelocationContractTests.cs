using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace STS2AIAgent.Tests;

/// <summary>
/// The raw <c>/state</c> builders moved out of <c>GameStateService.cs</c> by screen on 2026-09-20.
/// </summary>
/// <remarks>
/// The move was mechanical: same signatures, same bodies, same relative order, one <c>partial</c>
/// class either way, so nothing in the mod could observe it. What can observe it is source text --
/// and nearly every contract in this repo reads source text by path. This is the proof the predicate
/// split used, applied to the larger move: each declaration's SHA-256 is compared against the value
/// recorded from <c>GameStateService.cs</c> before the split. A body rewritten "equivalently" hashes
/// differently, so a rewrite cannot hide behind the word relocation.
///
/// The table also records <em>where</em> each member went. Regenerating it is a deliberate act: the
/// values only reproduce if the text is byte-identical to what the split moved, and the file column
/// has to be edited by hand, which is the friction a later reorganisation should feel.
///
/// Two entries were regenerated on purpose rather than by a relocation: <c>BuildStatePayload</c> and
/// <c>BuildAvailableActionNames</c> both changed when one state build started retaining the action
/// descriptors its own walk produced (<c>BuildDecisionSnapshotPayload</c>), so <c>/state</c> and the
/// action surface a decision reads can no longer describe two frames. Every other hash in the table
/// still reproduces the pre-split text, which is what makes the two edits visible instead of lost.
///
/// A third entry, <c>BuildCrystalSpherePayload</c>, was regenerated for a different reason: an item's
/// <c>kind</c> and <c>is_good</c> are now serialized only once every cell it occupies is revealed, so
/// the method that leaked them to hidden items on purpose no longer matches its pre-split text. The
/// new value is recorded here rather than the leak being restored to keep a hash stable.
///
/// <c>BuildCombatLethalRiskPayloads</c> was regenerated again when Doom and Magic Bomb joined the
/// flag the strategy text tells a model to trust. The player-played bomb stays out: it damages
/// enemies, not the player who played it.
///
/// <c>GetAvailableMapNodes</c> and <c>BuildMapPayload</c> were regenerated on 2026-09-23 when the
/// clickable-node filter moved from the stale <c>NMapPoint.IsEnabled</c> cache to the game's own
/// travelability rule (<c>IsMapNodeClickable</c>): during the travel animation the cache kept
/// <c>choose_map_node</c> advertised while the game ignored the click.
/// </remarks>
internal static class GameStateServiceRelocationContractTests
{
    private const string BaseFile = "STS2AIAgent/Game/GameStateService.cs";

    /// <summary>
    /// Every member the split moved, its new file, and the SHA-256 of its pre-split declaration.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string File, string Hash)> PreSplitDeclarationHashes =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["StateVersion"] = ("STS2AIAgent/Game/GameStateService.cs", "f49686a81fa6b522f58a5b93fed2b6dc85610f692c6890cf588494bfd3a68261"),
        ["FromMilliseconds"] = ("STS2AIAgent/Game/GameStateService.cs", "fd87082c9a12d32d8fb7b84654edfcebad89721af9a194f75b52fbecc1ff88cb"),
        ["_lastCombatActionReadinessSignature"] = ("STS2AIAgent/Game/GameStateService.cs", "2d0ca3660a9668761ec0ea4c0cc3255437322b01ad6a31fb23c9ad6fcd38a086"),
        ["_lastCombatActionReadinessSinceUtc"] = ("STS2AIAgent/Game/GameStateService.cs", "0bd295930bd58e4a30a8b5eba394a85153b8ec9d10ef646058c9c4ab62937c6d"),
        ["_lastUnlockConfirmProbeSignature"] = ("STS2AIAgent/Game/GameStateService.cs", "13285be7ac4d5d9f479ff19de3704f8ecf87713bb248437fab15227a4ad3933d"),
        ["_crystalSphereEntityLookupWarningLogged"] = ("STS2AIAgent/Game/GameStateService.cs", "8cfbf4ecee8d49d9b44920162e162489a0d3efd8e49f94016e9dd3e42a7354af"),
        ["_crystalSphereButtonLookupWarningLogged"] = ("STS2AIAgent/Game/GameStateService.cs", "c16d410de6049f3e70e37296414fe3bd8af1ec1751280ab8f8e96eb0c9f86c82"),
        ["StartRunLobbyMaxPlayersField"] = ("STS2AIAgent/Game/GameStateService.cs", "19edb0e3c9f24ab60b3a0446ceb73aac1f773e4b31d22142439eb6ca3dcf973a"),
        ["BuildStatePayload"] = ("STS2AIAgent/Game/GameStateService.cs", "819a16b1a8a7994557570d3317291a290f751a4934d85925b6c1f3308ebd9710"),
        ["BuildSessionPayload"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "c5ba61aed0a963ac73c60c7ac9890a010329625a5db703557d300b66ffc53795"),
        ["BuildAvailableActionsPayload"] = ("STS2AIAgent/Game/GameStateService.cs", "f94bfce253aebe76836b96be74844eac681be85f9448f50c96a7617bcd7fb1ad"),
        ["EnumerateAvailableActions"] = ("STS2AIAgent/Game/GameStateService.cs", "933b45a4c8c765a5bfd09471d850e956f54385168d3dc3ab02eed2643b9a66da"),
        ["ResolveScreen"] = ("STS2AIAgent/Game/GameStateService.cs", "2d5f3c899e556f73ecd366debaee5110c253eedcb86b0b85de591e5a95d37ed3"),
        ["FindActiveCombatRoom"] = ("STS2AIAgent/Game/GameStateService.cs", "42717fd1bfc51e6798bf5336d03ee7dad905a31236f6424d0c0702a16de6b60f"),
        ["GetLocalPlayer"] = ("STS2AIAgent/Game/GameStateService.cs", "4d521866d4f33356a64c6346f8151bc401181c256b001360a729386efbf612df"),
        ["GetLocalPlayer#2"] = ("STS2AIAgent/Game/GameStateService.cs", "462f017ef2324cf43d8e6cec16af0b5178da779a41f7b24631ff4d11cacf9c45"),
        ["GetTreasureRelicCollection"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "bbe95267aa1f472ed776a0b3fcef9a769b0290b7d9fbd684edb3338618e488cf"),
        ["CrystalSphereEntityField"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "2875b24c4b16fbf5d71a3290006bbf2129bd2a30f53a9405935335878293abd2"),
        ["GetCrystalSphereMinigame"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "ac439be4cd9a2ad4603b781e47eea5cbe44c19ee735d920d0a87d8c4819e3ff9"),
        ["LogCrystalSphereEntityLookupFailure"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "1d4dafc71009a3ca616be51da8438578cf2bdcc5f583528c0055e7036912d446"),
        ["TrySetCrystalSphereTool"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "56624750df6162799c30961d90a2e7fd490f39a895df6f665d099bf68fb164b3"),
        ["LogCrystalSphereButtonLookupFailure"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "80e6f2c7640512e473fa880fb9cb10e02e828e5b49a749bc724a4f7ae7329027"),
        ["IsKnownCapstoneContainerPage"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "d9e252c066cffffc863b23b08632ad7f1aebc60e9e4028b676d53d9b7460c168"),
        ["GetClosableCapstonePage"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "dba80117b736d672465f86d7297a44cb5978747c1195f057125859cb4d2396f8"),
        ["GetCapstoneButtons"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "8c8ab48c555188bd507509c7f5a7b7a7859d8c118bc90bf628ab6d5bb01b2d25"),
        ["BuildCapstonePayload"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "01b8a371535c17896d207e3ee5976aebe89a6363632581c3e926466b78f41026"),
        ["GetBundleConfirmButtons"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "661299b6ef79e6c810c325402bcf023ad677e441218dbdcaf634ac24a217729c"),
        ["GetBundleOptions"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "b0a150308269400420ee1b0b9990bd22ffcb706ab8619316313dad25bff41d77"),
        ["GetFakeMerchantButton"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "b549065768ec19025b0d247cea4764ae3d8f83fac807f1a4508e8db2f4b1f2be"),
        ["GetAvailableMapNodes"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "f67c78291928132ffcc53ea1e39a5784d1d2754ccc7a71994296918bbccec9ab"),
        ["GetRewardButtons"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "8b6886a96eb7478c811f70fbb3319c40da7a1dbc870474cf3ac1b2db0bb4d1bd"),
        ["GetRewardProceedButton"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "677bbc3328f28aec6b93761c2df5db2b8af2c08cf529da142b861d37ef4a9eff"),
        ["GetCardRewardOptions"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "0d32022188d35d1eb975a3454e698d9212de16bbb6a6a718481f8825949b3c57"),
        ["GetCardRewardAlternativeButtons"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "1a29988fa6c7eb7a9569201f1580ab110681a2ae9ed5edd567fed165852f75ea"),
        ["GetRewardSetId"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "d34df6df70766440e30595e35f22d45c648a5e3e08888816a6e0938944fbaafb"),
        ["GetDeckSelectionOptions"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "d35a028e941efd43a66d263a4f18cc3c23624fd17e41c20c7fcb19b3cda1f33d"),
        ["TryGetCardGridSelectionMetadata"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "e421197e9e1e94c33360b451055ecae8649ed18ee8e0b2dad7cff06d0ed21640"),
        ["GetDeckSelectionPrompt"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "5a1bdce01a3088d1d059133d6db6385551b89bffe06c0b93f335b8093e239ea0"),
        ["TryGetCombatHandSelection"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "01bf3fe0bb9b32d9c85a7f28043fbd8e87c748f23e514e50ab105f1fe3c800a3"),
        ["TryGetCombatHandSelectionPrefs"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "cfab6598bf4a45c0f50afe83b2486e08561241a30e7bbb596402dd82788f1312"),
        ["TryGetCombatHandSelectionMetadata"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "b01c098c7b1084469d51c4f32a54e32ea9ca5864339e6e50e557e92aeb9e2e0a"),
        ["GetCombatHandSelectedCount"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "3e7e088c6ab30026059da637883d68da79aa2aa9d85a9d9e242384ce5af1f76b"),
        ["TryGetCombatHandConfirmButton"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "be044bf686380cde9736260bd451967f7311da8052a48d0b38af9ad6047db2d4"),
        ["SafeReadString"] = ("STS2AIAgent/Game/GameStateService.cs", "6ed41507cedaa50ace5edc406cfa4dbb59f73587b1e5d3d32626b7ec1e8dbc93"),
        ["SafeReadBool"] = ("STS2AIAgent/Game/GameStateService.cs", "eab7d3fb5f284577fac4340f8dbfe47026dff9131ebfba9579d4446af494c938"),
        ["GetCardRulesText"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "506dbaee5b3640bbf9daaeeafa27b35ea08cffd57f9ca95bd407430c8fbc4e44"),
        ["GetResolvedCardRulesText"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "b88f83a47ffc60f13ed0eab6218f415e99462d25d45aff0d1b5b88df53ce4bca"),
        ["BuildCardDynamicValuePayloads"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "ad1b70543b3a7dabfd053fdc19f1779e7f50111114a56381ef53eabff933755f"),
        ["BuildAscensionEffectPayloads"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "5ed14eb94a37b66e63ac7412ae0ff234c75250bb7afde037fdd2061fc1f89b99"),
        ["TryCoerceText"] = ("STS2AIAgent/Game/GameStateService.cs", "bcbd545c25921f20cf18e1887341635b364af4685b7c631290832b09e74cd08d"),
        ["NormalizeCardRulesText"] = ("STS2AIAgent/Game/GameStateService.cs", "9038d00cc5f79647568411e56dd18468d8b5104a1f19afd67cbbb578c1893e2a"),
        ["GetProceedButton"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "3677b9f438d88ed0cd3d9851593dd6bb406088d4bc58c450ae16af334349375d"),
        ["GetCardsViewBackButton"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "ea37a038aa53400f48b4134d9cdaffcb496f7bbffe32ad1cf687026bc3058553"),
        ["ResolveEnemyTarget"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "cc081fad7e89fd5ea752f7e9fbc0772614ad6cae2c0a59c810dea2d205a86897"),
        ["ResolvePlayerTarget"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "1cc933fbfac4980177b3d94523f3d01d6b1511447e11972a368ef27a38d9bd6a"),
        ["CardRequiresTarget"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "4d0c58ace59ca18000ad707d21815eb2f0fac43d0535c5ef082006040d285bd1"),
        ["RestOptionRequiresTarget"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "a94bb716b62780612319c68f1009db3b381e447e4c919f90e6eab32ee62b15bf"),
        ["GetRestOptionTargetIndexSpace"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "93b57d720181cf3ac7122a7f76be2fd43f485afaf3426d4dce605882997c4e9b"),
        ["GetRestOptionTargetIndices"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "d06f8e2d7042c4d640f9d5d64604b16a0de087f500f0683bf340afa5df5f2074"),
        ["ResolveRunPlayerTarget"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "f1b1bb2355e8857c0dcd4e747a6f4e2c78ba691aa20de13a4e0b8f73b7cf0146"),
        ["GetUnplayableReasonCode"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "0afac547ec8eb0d184ac3a3de57e8918c2a3600a119a7751adc3151612742194"),
        ["GetUnplayableReasonCode#2"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "41aec19da04ed61910a5186f1aea8c2b725d2ee2de0a037c136475d8d5e44ae3"),
        ["IsLocalCombatTurnReady"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "0569a1f7fdcf5f22d42f1c53624b211404f2983acbc2830dbc53e9d2be61381b"),
        ["CombatActionGate"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "60e18aea6fded1729ec71943faa42445a5054ce5d49d541e1327ecd629a89817"),
        ["EvaluateCombatActionGate"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "cd07893ae61e7c0ca0c75799e9290af303ca1afe29b2ae8006625d9bacb5f223"),
        ["IsCombatActionSnapshotStable"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "719ab4e82888669247f820383b436ace78a58e3b6b3b51a73b9ecedfaf7976d6"),
        ["BuildCombatActionReadinessPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "456c83c5293e7cc36842d198e4c442caf5f8adb51bdbb4ee51de6d295ca34cd4"),
        ["BuildCombatActionReadinessSignature"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "9907c7517e3c7043c40d4ce211f65eb6f3e5170fd1526b6a07ece364c0931279"),
        ["ResetCombatActionReadiness"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "aa3bec113453f87628ba0577d450cca0dd4c11b0c4c23c6dc7425ee343732cf8"),
        ["GetEndTurnButton"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "8f76bfc88b7b47c46491119d5d891be8a53afda09d94b127e64a548c57c53b0f"),
        ["BuildAvailableActionNames"] = ("STS2AIAgent/Game/GameStateService.cs", "b95d51aabf1bee52abb8ee13f1c27e166135bd2a33ce927504160b6cff111863"),
        ["BuildCombatPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "2b54101bd86e153369f9c157d59e4eaf62126e9a0a85caf146a0dd3a23918ead"),
        ["BuildCombatLethalRiskPayloads"] = ("STS2AIAgent/Game/GameStateService.CombatRisks.cs", "f8085db67fa7a5e0affb594b75fdfb232d34985b267badd28ea6024e346b10be"),
        ["IsSandpitPower"] = ("STS2AIAgent/Game/GameStateService.CombatRisks.cs", "c423a99fb0c84cd3e162998b8b4c44c59ded64397b1577867da1e608c3f82696"),
        ["BuildRunPayload"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "508e174d94ea305e2302be42f5db73510ebe2da5b4ac9c7008e972048d20cd11"),
        ["ResolveBossId"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "70aecade3371139e3fdb777685e7b1648c90ad859bc9d90a8de5592e1283949d"),
        ["IsCardSelected"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "205981806775d8b3741743ab0d2d9d07459ba3484dc5b3b9d3265bd08dd23ece"),
        ["TryGetMemberValue"] = ("STS2AIAgent/Game/GameStateService.cs", "e836d94766bdb94033d236744ba1614100023dc4fcfa20c663e207041f9022d9"),
        ["BuildMultiplayerPayload"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "fd638afff65048407a7b5a592a642176c7f8b8867ab0022c471b55ea25c158cd"),
        ["BuildMultiplayerLobbyPayload"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "0370cd7fb6abc690af47ac5b0f3cc7db1424d214f5efa840af620d5761add1aa"),
        ["BuildMapPayload"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "fe1149246895f8ccb82a3e6627328ac4442037e59e73f540bf3accf4580785e2"),
        ["BuildSelectionPayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "9043e2ec637810972058b06ea3d437ebc2b428539922bdbbd4feadbcb0c067e3"),
        ["BuildCharacterSelectPayload"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "c7e28df2cbbdb89eb640d940a41803dc11bf66babcaef667e55b6a8e2a3e1d96"),
        ["BuildEventPayload"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "072fb8972e2738c52551e7c6e4902daa8d62747f826cfeb60060a58f5c6f5da6"),
        ["BuildCrystalSpherePayload"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "c25d3e74c33cb001be8e1d6dbdceda1fc15c77f49149793c26f6b45eb1b66fcd"),
        ["BuildRestPayload"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "378745fd9c1683082eda3041e03a6f6b445ca3df1a180fd5d4ddaa9333e92830"),
        ["BuildShopPayload"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "1b4998ed24c5a309edd3b6d9056b2b7f825a0a548307f0d875ae35fb7dc8946d"),
        ["BuildTimelinePayload"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "55429d92bc128646dbe1fd75b3fe7ec0557b5a09893811da0988f4a7fc11b198"),
        ["BuildUnlockPayload"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "381074670c939c328228fa0fb3211e2a6cc85817f6cea702bcb143a395b4bc0c"),
        ["BuildChestPayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "74bc5f11e0d71eec527e0b00b040a2b7d614459d24673f45e1300618b5c6a339"),
        ["BuildTreasureRelicOptions"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "a8ff8817b414c1380a77ec3c224ccaf954ffd3b7bfbecadabe83a1ea2819502d"),
        ["BuildRewardPayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "b81266997e9f77fc59e5b8d7233f610f77e0f28f3b6dc0b4c0d9936b6f27ef9a"),
        ["BuildBundlePayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "0e53cc5c87fec6b0d83235d0facb6e6ae401270949d5551b3df3abef536909fa"),
        ["BuildModalPayload"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "e50fe34507468878d3af92cf543889a8dbc320d1a93a086db164e3c0b973d7b6"),
        ["BuildGameOverPayload"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "b152e93a13d1239bf1c9817b9cb33220f397ac755ea3667968fdf6175c2cc1ce"),
        ["VerifyGameOverProgressSave"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "f417813a51c483c31608d612aefbe040cce4bdf6a97e4a6ec5f81f49b9dbfe0d"),
        ["BuildHandCardPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "a87ed5050ecad5b3090e532eb44314d1517664e6e260bde204b1e9810da61353"),
        ["GetModelIdEntry"] = ("STS2AIAgent/Game/GameStateService.cs", "a6bc7fd47caee58200cf2b4af006c24a62f11e89f3154de19bec4fb4abe8e745"),
        ["BuildEnemyPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "61cf445651f34aa4415d3861c84c2fbf8aebb6de39f790eb3456cded6735b6f6"),
        ["BuildCreaturePowerPayloads"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "7ea2af1581dd4849d763f28fc645165e99d43ad3f8374857a3cf7942e4409042"),
        ["PlayerSpawnsPets"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "5998f0868d8fcb58cfa48018370e63151a60e61477a5b0b22f05d128b02cd257"),
        ["BuildCombatPetPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "a0b7fbc1030e1b4d2af45d2268459ea67a871937d4f9e62043fed644263d50f1"),
        ["BuildEnemyIntentPayloads"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "a3fd437bf64cd5e3f6fee92d0186cfeeb5cf313a8414a760365eb5e51ad669cb"),
        ["BuildEnemyIntentPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "3869b8b88e5aaf1579e9a6624321e97424d0840dcc11d9bb1ea6374aa32b22a3"),
        ["SafeReadNullableInt"] = ("STS2AIAgent/Game/GameStateService.cs", "63731edba710ffa8c3a6cd03bc74eb860bbab20bda847a1cc5361b1afbb32f03"),
        ["BuildCombatOrbPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "ac86fdbe341831684add797a33d6067a70a9d11b6f633de2f0fa65af74c05eb9"),
        ["BuildMapNodePayload"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "47dc16ece3a1a42f4f916a7b60a659a32a63b01438a2f8d67594cc7ddf2ad01e"),
        ["BuildMapGraphNodePayload"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "a280650fdba804e8fa40d0f6255180ae8a237c4f8166ffa036a58e2e873ddc97"),
        ["BuildMapCoordPayload"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "ea80e369530f0a982ed9a6a3d386594a555bda5733c130852e49f0d1ece9a78a"),
        ["BuildMapPlayerVotePayloads"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "f8a3bcd61468c4f32da3a1dfbb8f2df8ad8d2c792f5f9f7129252d33cc116517"),
        ["GetAllMapPoints"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "d25fc058d5da8eb9a80cae810b50536cf03cdc58c7b9b632fe82d00773a1ee81"),
        ["ResolveMapPointState"] = ("STS2AIAgent/Game/GameStateService.Map.cs", "1d93a6fe13a72428558ea3467559ebaf49bbd0de2caf58b1fdaab62e6cc97fee"),
        ["BuildRewardOptionPayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "333389b86b09dc2bc66c65db06af5b92563b4e4e2d75169be5cbc8abe7355120"),
        ["BuildRewardCardOptionPayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "81419a2b5f20245538f98f68f1f49935404f874fb567330c7f205f99cd8e5a4a"),
        ["BuildBundleCardPayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "0e7bab63649d7b4cc9f7253a0467dcce4980bc27090df0fb812d82bac3d49d81"),
        ["BuildRewardAlternativePayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "8d867e11fadb06fd2c15b4f52f5e15231fcdd96741d1c5d73ca620bbd8ff38a2"),
        ["BuildRunRelicPayload"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "a636c4d8583df7923a0e74cf971b8a617e7490e67056337817b5bbda2f4f1bdf"),
        ["BuildRunPotionPayload"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "afcda41a6fb11ca24f1f38bb776de8b13237df6f27c8b1aea67ca6d94573ce16"),
        ["RawTextOrNull"] = ("STS2AIAgent/Game/GameStateService.cs", "18842f331110953fa0b4999e7efe9383feedc6b654f8a3f91c62670c1cb95579"),
        ["GetEventOptionWillKillPlayer"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "14b90dc6f74d6704bba17aa3c1c627e498dafb0096c2ba73e80c1e77510dad6e"),
        ["BuildCharacterSelectPlayerPayload"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "3d8050b088e35e015cc63476fdf7a2ea5a75f1b9c144d5b616f1af9e6841ca00"),
        ["BuildRunPlayerSummaryPayload"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "cebd0b5eb78ee4f0028d3bbef2db3694be84694d190d7d8399897348ea50e003"),
        ["BuildCombatPlayerSummaryPayload"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "f3a846917b3a8fe485a377a164a2e9bd3e649684150da7a42c6d3a46df3312e0"),
        ["GetCardTargetIndexSpace"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "df45d23fd058b00cec7086bba4173505b24d06bc266174689090914d58cb292a"),
        ["GetPotionTargetIndexSpace"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "86faccbf9996fcf2bf11576bd3855533efe2b3e13a92abe658e49b2cf3d06a06"),
        ["GetCardTargetIndices"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "7195b047180d7c0e157357744404eac01315c0aee8081420cd75e4bd42b1d73c"),
        ["GetPotionTargetIndices"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "b287e273ee92973549d5959efa6013371a9be50bf33b567ff9e107e7834f3305"),
        ["BuildShopCardPayload"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "6e96745b18c8b66daf49e965b79ac2080d4deb3c151d09e6bb68450569040c46"),
        ["BuildShopRelicPayload"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "2ccdea27f9141aeb13ae80559bf3898432301e42147c8b21851a4e75c4df1800"),
        ["BuildShopPotionPayload"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "9b5701474ccd07ffa6ae609d0efc07f4e38c6fb19a9cf441122f1494db291ce7"),
        ["CanPurchaseShopPotion"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "aac9a1749118ecf0a5921b65b03bc0bced6c74de6ee7e3cbfea985c096e18846"),
        ["BuildShopCardRemovalPayload"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "66755a6d0c4a1501767b88c976aad0c42eb6e71de82f9cdef8e3945b26c77675"),
        ["BuildDeckCardPayload"] = ("STS2AIAgent/Game/GameStateService.Run.cs", "bbfc22dd36826543fcc6ef0c53ae8ae596204c5bbebd8b1567b9e76f36585842"),
        ["BuildSelectionCardPayload"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "038f0cc8d0656672f0cc1b0deb3f878d6f9b90fd677f46bd94e59eb8768fcc18"),
        ["IsProceedButtonUsable"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "b8eeab21313e1b8f10b04724fef5948ca66237a4681bd056d90f4d02f0795294"),
        ["GetRewardTypeName"] = ("STS2AIAgent/Game/GameStateService.Rewards.cs", "c836bc3619e72de1c1d4911ff040dc0af3d7b31fa49d2da3773166e049ab00a6"),
        ["IsPotionUsable"] = ("STS2AIAgent/Game/GameStateService.Potions.cs", "439c7d17437a8e61f64c224f3d726cb2c7998498ea78c1f35ed158b9433c6b00"),
        ["CanDiscardPotionsInCurrentScreen"] = ("STS2AIAgent/Game/GameStateService.Potions.cs", "032023b8cc4ddcb38158332146ebd854d106c8c4dd2d0cb81015aea0a609a0a4"),
        ["IsPotionDiscardable"] = ("STS2AIAgent/Game/GameStateService.Potions.cs", "1cb542386a99e33fbc9394c4d8ccf718dd9fa74ddaac7b1f27f0feea92dda36a"),
        ["PotionRequiresTarget"] = ("STS2AIAgent/Game/GameStateService.Potions.cs", "3d5f00e50ec8b436422ec9f612c4bd2a7da2cb8b30cd920add4db3486f80b120"),
        ["IsPotionTargetSupported"] = ("STS2AIAgent/Game/GameStateService.Potions.cs", "3f0c2f3ab637a5a5d66f84840cf6fcd10f99d3bc20d88d9cf3d10c909ff7fbb2"),
        ["PotionRequiresExplicitPlayerSelection"] = ("STS2AIAgent/Game/GameStateService.Potions.cs", "8f23607b8ba539ff519f7eb87a0e0e3d1e1485b80ac8671c7a26bd820acd13cb"),
        ["RequiresIndexedCardTarget"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "4166cefab104dba12342f588485c3a2833d0c424daa7096d375d1a3e4d6faee6"),
        ["GetTargetableEnemyIndices"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "722609aeb66ca388cf5e07cb87fedeb4756b47d66afcb0950430a1301eeb8efd"),
        ["GetTargetablePlayerIndices"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "81032dc2faed60177a0031465bf078d2f1f5001bd4453b266381656d6226ba4a"),
        ["GetOrderedCombatPlayers"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "24792733ed0d9ea740fc2872d2de82d6167477c23cdea430a19f72bee0887c64"),
        ["GetMerchantRoom"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "15cdd5a7f25e5cab39b7038fa5ac1f3e4e19b110a69c541a5797623bb6bb9027"),
        ["GetMerchantInventoryScreen"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "d3c692341a9192680b0be589dd394c74bdf30f4bdefe8c4b319a1b4548dd1f9d"),
        ["GetMerchantInventory"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "0991c7f91b64ed6ec0fc1aa5227673dbee180e2456a4608f080b0cec2f875ada"),
        ["GetMerchantCardEntries"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "61896f6a2881717b6e39ae9cee17bc44294916a1cd06e618c02d2189f9371a05"),
        ["GetMerchantRelicEntries"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "b6ba00403a7c85989526ce08e293a08e15b00e3d392a6e5308963e9ca8194d0f"),
        ["GetMerchantPotionEntries"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "b4cc7c678c6cc60a1af5c4f8c81f84b094321d670e617d9017c00697dd03cdd3"),
        ["GetMerchantCardRemovalEntry"] = ("STS2AIAgent/Game/GameStateService.Shop.cs", "12b03a90e02a8b84c8245531efd05672f2ce6ce7b33f5955f42100d69afdf43b"),
        ["GetCharacterSelectScreen"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "48690441c208956725e82f25ac8d6bf88efb93682807ca267f8e85d07b5934fc"),
        ["GetMultiplayerTestScene"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "a1eed6dd486e980cd805f38db2d6e6126f6c5b9e91f39961f3d74ceae1e71fd8"),
        ["GetMultiplayerTestLobby"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "c64e5bf1b51863d40e03b682a7c41d59bdb8aa28a7e51a371b5ae684d3e1b5a4"),
        ["GetStartRunLobbyMaxPlayers"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "6c78e846744306380892377fb260a4fbf40c404796ec20b81e8fe86be8d7417d"),
        ["EnsureFourPlayerLobby"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "2675349e298e9fcdcdc4310ef9b40f434439d416f4450202ca5aa552c5042be7"),
        ["GetMultiplayerTestCharacterPaginator"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "3ab4d485ac3edcebfd40a1e5c050779e1b923bc02a93d0525f55c8cc5a55be33"),
        ["GetMultiplayerLobbyJoinHost"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "60783bd6e5a7900de14e6a49854db52e1f09849b039062668b075a9656347ae6"),
        ["GetMultiplayerLobbyJoinPort"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "d7dc9e3729c38ab6218a59e6bfdf85a2adfaaf599f3966c840cd7c72f93274cc"),
        ["GetMultiplayerLobbyJoinNetIdHint"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "478ef2ee66a4a5bc1ba69bc81b9487a68b7e216d696a109d36f0bb66b1d51fef"),
        ["GetMultiplayerLobbyCharacters"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "bf6c3f53aaf371a42b61f8a00613fa6096aafcc36acbfcdfbe9fbcf24a2bcfbb"),
        ["GetCharacterSelectButtons"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "e5083e254571b710cdad2e127185e66c74f40dec903b4af1f5d2014192859bc9"),
        ["GetMultiplayerLoadScreen"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "449ae04cf053cee8427063bc7a1cdd61ede8a16c55647eeacf808c290cd9d430"),
        ["GetCharacterEmbarkButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "533cfc6f34534d630619a62ee7ea7eb29a4531a533dbbbf7e3ffe4007c6d3727"),
        ["GetCharacterUnreadyButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "5eefa883510027190c7d5d15c1ea0685980d20f3bc878c2410d865b772065644"),
        ["GetMainMenuContinueButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "1f1fa9b53c05387d223e337ca6e9ca3be060ccaa5c278e4085fc602564858527"),
        ["GetGameOverContinueButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "3af619208a99a4acc7ee5939fb364fe6d546b3b7a61b64cc7967866a44c032a7"),
        ["GetWaitingForOtherPlayersOverlay"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "e7db9a64d235f514a700d18c74f2701ce4b030b8087fbb59ab8e6f12dc1ad4bf"),
        ["HideWaitingForOtherPlayers"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "acf81dc02cf498fe54dfa82199bf543d7802ffbf886260b3284b0470b913ad26"),
        ["GetGameOverMainMenuButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "567f18272996567ddffe988809de649f1ea9ca6378d622573ac4f157375dd268"),
        ["IsGameOverButtonReady"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "9d1e9aa4df2b06771b98345bba7beb414b4a748a07ad81d475bc95d463a0d676"),
        ["GetMainMenuAbandonRunButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "52d50986f7a9dae9e7983ef105beecfb88d759b794b2dfeb082a7c6637a390cf"),
        ["GetMainMenuSingleplayerButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "baba3c1c4ba21b525f69f48f64fb4a0b4a0f87562e293f484db6878177421378"),
        ["GetSingleplayerStandardButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "dc459de11658ae012960cefdec378d38c305a4df6c811707d5393479cfd2e6cd"),
        ["GetMainMenuTimelineButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "13a43da3f3b6124269eb9126c03c8f9e279d14573320c86bc7f27e31e7e10c92"),
        ["GetTimelineScreen"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "9dbeed5ecf8c7f6e955660d7b374699b603ef77d44dcfad9451273f92bfb5b36"),
        ["GetTimelineTutorial"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "927381c393652ab36c8e6ac9f074fa326c8171b600d58086148a2fb38a6d5125"),
        ["GetTimelineTutorialAcknowledgeButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "9948ff755b5627ef771b44adb5a14144cfa25c66961d91ef7284aa12f884495d"),
        ["GetTimelineSlots"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "9da337de3646fda039428c7b7e525e6746fdb06ac0c541c06ebf1fff8f9e8543"),
        ["GetTimelineInspectScreen"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "c8156d2eba19f00fd7cc51de20eeca315d5e6c21af50988222a9d73645c11eda"),
        ["GetTimelineUnlockScreen"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "5629aa9da50bd6c18af7daf40c521eb75b9454f871d18f4032dd8b86ced6ea01"),
        ["GetActiveUnlockScreen"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "eee5ab8c7ceac258865054230f3c0f072314faafa1dd0ebfbf40a93b66c32b8d"),
        ["GetUnlockConfirmButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "62bebc6303eea80e9beb95523f209d5c1859513a4b3e026bec9c53bb3e519e46"),
        ["IsUnlockConfirmButtonUsable"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "d2fb2142d2b1f3826e9b41ceea1aca7bc453170da6881d13800f856c19f2ebc1"),
        ["LogUnlockConfirmProbe"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "b2480b799797a1fa2ca54e264da0f3fd4081af2d327c5ceac5e25fbbcdde8cb2"),
        ["GetUnlockItemNames"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "a7eb70439ba6f3d838d416f2325e7ba090518caa81177dc53e5e4f80f7d0751c"),
        ["SafeModelTitle"] = ("STS2AIAgent/Game/GameStateService.cs", "bc3c7e4468de06622a53636724fadc6ead3912a53f8b93af5d1f5aaed17e9b8b"),
        ["GetTimelineBackButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "3da84f75dc97860bd0440d1e5bc3cd9724162244cc501217b2317d5b5da7a795"),
        ["GetTimelineInspectCloseButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "a47939649b81fed486f3f7ec67f3477cb8a0aad35a3c6baaa4b1f6495dbf2075"),
        ["GetTimelineUnlockConfirmButton"] = ("STS2AIAgent/Game/GameStateService.Menus.cs", "fd23f0cdbca841c1e436ba890531cc67c09c24797a4999430c1e77a30da9756a"),
        ["GetSubmenuStack"] = ("STS2AIAgent/Game/GameStateService.cs", "53e71946ef8d3dd26a26c0a8630c2a5116fe21c923a9c18c62265b54e04f41ff"),
        ["GetOpenModal"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "a748e4513a27fb9e83fdadec207d02b63e34e59c3cf03289b950373a62e509dd"),
        ["TryCloseOpenFtue"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "cb78ed3acc6dea7b60acfe98bd19b150365f8e49ff333c6d85b6d6253dda8816"),
        ["IsModalGone"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "650ed6af35bf545f58b2e5292243b1e1e3d36675b5315313b85058b3e46a5bbf"),
        ["FindInstanceMethod"] = ("STS2AIAgent/Game/GameStateService.cs", "b564d061d844a6ee3120a89ddcc929853d6ca9e09a55f3df09d0b256bb80e8d7"),
        ["GetModalConfirmButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "9787f1b0e622c953ac11e11d64f6abdb1c52591e863862f64542e477bb7e333a"),
        ["GetModalCancelButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "512834c7656990d42afa719dd136d4b9e140c1a08e502065dc1db9dc6da65332"),
        ["FindModalButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "c3defe0adae4f23e922a5f34b8a5e070d7bd676d28741cc39400a20feba9dfe1"),
        ["IsUsableModalButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "0ee9e4a6b2974c1420110b0ce89723c1f0f8667e904f29f651832980fae7edb3"),
        ["IsConfirmNamedModalButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "7e99ee458fa674f293f648a399bf823880802e444c3c02b85faf0b25a87c6dea"),
        ["IsDismissNamedModalButton"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "bc11d30837ba20f850fa471b3e38f5b43dd47b3e1a7563f9e5389190ea07665e"),
        ["ModalButtonNameContains"] = ("STS2AIAgent/Game/GameStateService.Rooms.cs", "1e5d0143fd6f33c1de424a527fda0059a4771df8f8d683b20e58cc55b89476bd"),
        ["ResolveUnderlyingScreen"] = ("STS2AIAgent/Game/GameStateService.cs", "64f82250ecbd2761b4a1357c0338d52098ff20199e830088335aa7187fb0ea81"),
        ["ResolveNonModalScreen"] = ("STS2AIAgent/Game/GameStateService.cs", "ad17d9bc792af44dffbd9a99182e89c8db6a3f347c583eef7a36c2382f86c42c"),
        ["GetButtonLabel"] = ("STS2AIAgent/Game/GameStateService.cs", "9b0d763716172010536f3bb1c9fa377876ce7e70f607735a2efdc4baf62a5603"),
        ["TryGetMapScreen"] = ("STS2AIAgent/Game/GameStateService.cs", "b46f520ac4004903cbb21e1d8a7a851d59804d9daafc0ee8d61f075af933ec0e"),
        ["FindDescendants"] = ("STS2AIAgent/Game/GameStateService.cs", "723b112cc6fade648b6bf51f33278dcaa3568efc3c855efd3369801410d699fe"),
        ["GetVisibleGridCardHolders"] = ("STS2AIAgent/Game/GameStateService.cs", "5c33e2a822320ab02c5007809ed6ee1aafbb7914f9a8472cffebd9b9b83cfbec"),
        ["FindDescendantsRecursive"] = ("STS2AIAgent/Game/GameStateService.cs", "a39f4a7d6231ef41f6b122839c0195734176634da6af733e92e5154120b161be"),
        ["GetConnectedPlayerIds"] = ("STS2AIAgent/Game/GameStateService.cs", "d0989a988289a00c03b2cd969fc90d98028ca9bccf1fc1c81f8a7855d398a35b"),
        ["NetIdToString"] = ("STS2AIAgent/Game/GameStateService.cs", "26fc8f9e127d4051bce69317e7bfdcdd98e2d1785e5a1f1531ea5c5323d29596"),
        ["IsPlayerActionPhase"] = ("STS2AIAgent/Game/GameStateService.cs", "e0b1067fddbfe369566c24fd7d3cbf40c98ab5f122b8ef25a7246c5703cc2409"),
        ["IsPlayerActionPhase#2"] = ("STS2AIAgent/Game/GameStateService.cs", "f8b44d34bd2dac42a703c030e5b6b84dfb87adeff12c2cf74fc03c7df16637a8"),
        ["IsCardTargetSupported"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "651467727236a2758edc33903d3e096e9e5a409fcff4ff4a87d43197fa602dea"),
        ["IsEndTurnButtonReady"] = ("STS2AIAgent/Game/GameStateService.Combat.cs", "dd7fad1ec061d7fa8fed33d7347df9ebbd925480c28e3e244f3f8ae1ff3fbba5"),
        ["IsWaitingForOtherPlayers"] = ("STS2AIAgent/Game/GameStateService.cs", "d29427080c8ff60de168a363cc78389ebde42d3abb55a058fc3522d069d33d4b"),
    };

    public static void EveryMovedDeclarationStillMatchesItsPreSplitText()
    {
        var mismatches = new List<string>();
        var missing = new List<string>();
        var byFile = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var (key, entry) in PreSplitDeclarationHashes)
        {
            if (!byFile.TryGetValue(entry.File, out var declarations))
            {
                declarations = Declarations(AgentSourceFixture.Read(entry.File));
                byFile[entry.File] = declarations;
            }

            if (!declarations.TryGetValue(key, out var text))
            {
                missing.Add($"{key} ({entry.File})");
                continue;
            }

            var actual = Hash(text);
            if (!string.Equals(actual, entry.Hash, StringComparison.Ordinal))
            {
                mismatches.Add($"{key}: expected {entry.Hash[..12]}, actual {actual[..12]}");
            }
        }

        Assert.True(
            missing.Count == 0,
            "these declarations are gone from the file the split put them in: "
            + string.Join(", ", missing)
            + ". If one moved again, update the file column deliberately rather than deleting it.");
        Assert.True(
            mismatches.Count == 0,
            "these declarations no longer match the text the split moved. A relocation may not "
            + "rewrite a body; if the change is intentional, regenerate the table and say so in the "
            + "commit message:\n  " + string.Join("\n  ", mismatches));
    }

    public static void TheSplitActuallyShrankTheBaseFile()
    {
        // The budget is read from the ratchet rather than restated here, so a later edit cannot
        // re-raise it and still satisfy this test.
        var shape = AgentSourceFixture.Read("STS2AIAgent.Tests/SourceShapeContractTests.cs");
        var match = Regex.Match(
            shape,
            @"\[""STS2AIAgent/Game/GameStateService\.cs""\]\s*=\s*(\d+)\s*,");
        Assert.True(match.Success, "SourceShapeContractTests no longer budgets " + BaseFile + ".");
        var budget = int.Parse(match.Groups[1].Value);

        var baseLines = LineCount(BaseFile);
        Assert.True(
            baseLines < budget,
            $"{BaseFile} is {baseLines} lines against a {budget}-line budget. The budget was lowered "
            + "with the split; if it is now at or under the file, the ratchet entry stopped guarding "
            + "anything.");

        var files = PreSplitDeclarationHashes.Values
            .Select(entry => entry.File)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !string.Equals(path, BaseFile, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            files.Count >= 6,
            $"the split spread the builders across {files.Count} screen file(s); the table no longer "
            + "describes a per-screen split.");
        foreach (var file in files)
        {
            var lines = LineCount(file);
            Assert.True(
                lines <= 1000,
                $"{file} is {lines} lines, over the 1000-line default budget, so the split moved the "
                + "problem rather than solving it.");
        }
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static int LineCount(string relativePath)
    {
        return Declarations(AgentSourceFixture.Read(relativePath)).Count > 0
            ? AgentSourceFixture.Read(relativePath).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length
            : 0;
    }

    /// <summary>
    /// Every top-level member of the <c>GameStateService</c> partial, keyed the way the pre-split
    /// table keys them (<c>Foo</c>, then <c>Foo#2</c> for an overload), with the declaration text
    /// normalised exactly as the recorded hashes were.
    /// </summary>
    private static Dictionary<string, string> Declarations(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var (depths, opens, code) = ScanLines(lines);

        var bodyStart = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Contains("partial class GameStateService", StringComparison.Ordinal))
            {
                while (index < lines.Length && !lines[index].Contains('{'))
                {
                    index++;
                }

                bodyStart = index + 1;
                break;
            }
        }

        if (bodyStart < 0)
        {
            throw new InvalidOperationException("no GameStateService partial class in this source");
        }

        var bodyEnd = lines.Length;
        for (var index = lines.Length - 1; index > bodyStart; index--)
        {
            if (depths[index] == 1 && lines[index].Trim() == "}")
            {
                bodyEnd = index;
                break;
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var index2 = bodyStart;
        while (index2 < bodyEnd)
        {
            var start = index2;
            var seenBlock = false;
            while (index2 < bodyEnd)
            {
                seenBlock |= opens[index2] > 0;
                var depthAfter = depths[index2 + 1];
                index2++;
                if (seenBlock ? depthAfter == 1 : EndsStatement(code[index2 - 1]))
                {
                    break;
                }
            }

            var chunk = lines[start..index2].ToList();
            var name = NameOf(chunk);
            occurrences[name] = occurrences.TryGetValue(name, out var seen) ? seen + 1 : 1;
            var key = occurrences[name] == 1 ? name : $"{name}#{occurrences[name]}";
            result[key] = Normalise(chunk);
        }

        return result;
    }

    /// <summary>The declared name, read from the signature line rather than from the whole body.</summary>
    private static string NameOf(List<string> chunk)
    {
        var line = chunk
            .SkipWhile(candidate =>
            {
                var trimmed = candidate.Trim();
                return trimmed.Length == 0
                    || trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("[", StringComparison.Ordinal);
            })
            .FirstOrDefault();

        if (line == null)
        {
            throw new InvalidOperationException("a member chunk holds no declaration line");
        }

        line = line.Trim();
        var paren = line.IndexOf('(');
        if (paren >= 0)
        {
            var head = line[..paren];
            // `Bar<T>(` declares Bar. Only a generic list that ends the head is the member's own;
            // the return type's `<...>` sits in the middle and must be left alone.
            if (head.EndsWith(">", StringComparison.Ordinal))
            {
                var depth = 0;
                for (var position = head.Length - 1; position >= 0; position--)
                {
                    if (head[position] == '>')
                    {
                        depth++;
                    }
                    else if (head[position] == '<')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            head = head[..position];
                            break;
                        }
                    }
                }
            }

            var match = Regex.Match(head, @"([A-Za-z_][A-Za-z0-9_]*)\s*$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        var field = Regex.Split(line, "=>|[=;{]")[0];
        var fieldMatch = Regex.Match(field, @"([A-Za-z_][A-Za-z0-9_]*)\s*$");
        if (fieldMatch.Success)
        {
            return fieldMatch.Groups[1].Value;
        }

        throw new InvalidOperationException("cannot name member: " + line);
    }

    private static string Normalise(List<string> chunk)
    {
        while (chunk.Count > 0 && string.IsNullOrWhiteSpace(chunk[0]))
        {
            chunk.RemoveAt(0);
        }

        while (chunk.Count > 0 && string.IsNullOrWhiteSpace(chunk[^1]))
        {
            chunk.RemoveAt(chunk.Count - 1);
        }

        return string.Join("\n", chunk.Select(line => line.TrimEnd()));
    }

    private static bool EndsStatement(string codeLine)
    {
        return codeLine.TrimEnd().EndsWith(";", StringComparison.Ordinal);
    }

    /// <summary>
    /// Per-line brace depth at the line's start, how many real <c>{</c> each line opens, and each
    /// line with string/char literals and comments blanked. One literal-aware walk, so a brace in a
    /// string cannot be mistaken for a block and a semicolon in a literal cannot end a member.
    /// </summary>
    private static (int[] Depths, int[] Opens, string[] Code) ScanLines(string[] lines)
    {
        var depths = new int[lines.Length + 1];
        var opens = new int[lines.Length];
        var code = new string[lines.Length];
        var depth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            depths[index] = depth;
            var builder = new StringBuilder();
            var line = lines[index];
            for (var position = 0; position < line.Length; position++)
            {
                var character = line[position];
                if (character == '/' && position + 1 < line.Length && line[position + 1] == '/')
                {
                    break;
                }

                if (character == '@' && position + 1 < line.Length && line[position + 1] == '"')
                {
                    position++;
                    while (position + 1 < line.Length)
                    {
                        position++;
                        if (line[position] == '"')
                        {
                            if (position + 1 < line.Length && line[position + 1] == '"')
                            {
                                position++;
                                continue;
                            }

                            break;
                        }
                    }

                    builder.Append("\"\"");
                    continue;
                }

                if (character is '"' or '\'')
                {
                    var quote = character;
                    while (position + 1 < line.Length)
                    {
                        position++;
                        if (line[position] == '\\')
                        {
                            position++;
                            continue;
                        }

                        if (line[position] == quote)
                        {
                            break;
                        }
                    }

                    builder.Append("\"\"");
                    continue;
                }

                if (character == '{')
                {
                    depth++;
                    opens[index]++;
                }
                else if (character == '}')
                {
                    depth--;
                }

                builder.Append(character);
            }

            code[index] = builder.ToString();
        }

        depths[lines.Length] = depth;
        return (depths, opens, code);
    }
}
