# Relic Index

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-09-21 21:57:15 +08:00

Rarity, owner, and readable effect for every relic, keyed by the internal name behind `relic_id` (the class name; the id the game reports is its slugified upper-case form, e.g. `BurningBlood` -> `BURNING_BLOOD`). Use it when deciding whether to take a relic from a chest, buy one in a shop, or accept one from an event. `Owner` is the relic pool that declares the relic: a character name means that character's pool, `Shared` is the pool every character draws from, and `Event`/`Fallback`/`Deprecated` are the pools no character owns. `Effect` names the hook that produces each clause, because for a relic the difference between `AfterObtained` (once, on pickup) and `AfterSideTurnStart` (every turn) is the whole decision; a hook a base class declares is prefixed with that class name. Numbers come from the relic's dynamic vars; `?` marks an amount only computed at run time, and `Amount` is the relic's own counter where it has one.

| Name | Rarity | Owner | Effect |
| --- | --- | --- | --- |
| Akabeko | Uncommon | Shared | AfterSideTurnStart: Gain 8 Vigor |
| AlchemicalCoffer | Ancient | Event | AfterObtained: gain Max Potion Count; AfterObtained: create Random Potions Out Of Combat; AfterObtained: Obtain a random potion |
| AmethystAubergine | Common | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); TryModifyRewards: returns false or true |
| Anchor | Common | Shared | BeforeCombatStart: Gain 10 Block |
| ArcaneScroll | Ancient | Event | AfterObtained: Offer a card reward; AfterObtained: Put a card into your deck |
| ArchaicTooth | Ancient | Event | AfterObtained: Transform a card |
| ArtOfWar | Rare | Shared | AfterEnergyReset: Gain 1 Energy |
| Astrolabe | Ancient | Event | AfterObtained: Transform a card in your deck; AfterObtained: create Random Card For Transform; AfterObtained: Upgrade a card; AfterObtained: Transform a card |
| BagOfMarbles | Uncommon | Shared | BeforeSideTurnStart: Apply 1 Vulnerable to the target |
| BagOfPreparation | Common | Shared | ModifyHandDraw: returns count or count + 2 |
| BeatingRemnant | Rare | Shared | ModifyHpLostAfterOsty: returns amount or Math.Min(amount, 20 - DamageReceivedThisTurn) |
| BeautifulBracelet | Ancient | Event | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card with Swift |
| Bellows | Uncommon | Shared | AfterPlayerTurnStart: Upgrade a card |
| BeltBuckle | Shop | Shared | AfterObtained: Gain 2 Dexterity; BeforeCombatStart: Gain 2 Dexterity; AfterPotionProcured: Gain -2 Dexterity; AfterPotionDiscarded: Gain 2 Dexterity; AfterPotionUsed: Gain 2 Dexterity |
| BigHat | Rare | Necrobinder | AfterSideTurnStart: get Distinct For Combat; AfterSideTurnStart: Add a generated card to your hand |
| BigMushroom | Event | Event | AfterObtained: Gain 20 Max HP; ModifyHandDraw: returns cardsToDraw or cardsToDraw - 2 |
| BiiigHug | Ancient | Event | AfterObtained: Remove a card from your deck; AfterShuffle: Add a generated card to your draw pile |
| BingBong | Event | Event | AfterCardChangedPiles: Put a card into your deck |
| BlackBlood | Starter | Event | AfterCombatVictory: Heal 12 HP |
| BlackStar | Ancient | Event | TryModifyRewards: returns false or true |
| BlessedAntler | Ancient | Event | ModifyMaxEnergy: returns amount or amount + 1; BeforeHandDraw: Add a generated card to your draw pile |
| BloodSoakedRose | Ancient | Event | AfterObtained: Add the curse Enthralled to your deck; ModifyMaxEnergy: returns amount or amount + 1 |
| BloodVial | Common | Shared | AfterPlayerTurnStartLate: Heal 2 HP |
| BoneFlute | Common | Necrobinder | AfterAttack: Gain 2 Block |
| BoneTea | Event | Event | AfterSideTurnStart: Upgrade a card |
| Bookmark | Rare | Necrobinder | no gameplay command in an overridden hook (passive in source) |
| BookOfFiveRings | Common | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); AfterCardChangedPiles: Heal 15 HP |
| BookRepairKnife | Uncommon | Necrobinder | AfterDiedToDoom: Heal ? HP |
| BoomingConch | Ancient | Event | ModifyHandDraw: returns count or count + 2 |
| BoundPhylactery | Starter | Necrobinder | BeforeCombatStart: Summon 1; AfterEnergyResetLate: Summon 1 |
| BowlerHat | Uncommon | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); ShouldGainGold: returns true; AfterGoldGained: Gain ? Gold |
| Bread | Shop | Shared | ModifyMaxEnergy: returns amount or amount + 1; AfterSideTurnStart: lose Energy |
| BrilliantScarf | Ancient | Event | TryModifyEnergyCostInCombat: returns false or true; TryModifyStarCost: returns false or true |
| Brimstone | Shop | Ironclad | AfterSideTurnStart: Gain 2 Strength; AfterSideTurnStart: Apply 1 Strength to the target |
| BronzeScales | Common | Shared | AfterRoomEntered: Gain 3 Thorns |
| BurningBlood | Starter | Ironclad | AfterCombatVictory: Heal 6 HP |
| BurningSticks | Shop | Shared | AfterCardExhausted: Add a generated card to your hand |
| Byrdpip | Event | Event | AfterObtained: Transform a card into Byrd Swoop; AfterObtained: add Pet Mega Crit Sts2 Core Models Monsters Byrdpip; BeforeCombatStart: add Pet Mega Crit Sts2 Core Models Monsters Byrdpip |
| CallingBell | Ancient | Event | AfterObtained: Add the curse Curse Of The Bell to your deck; AfterObtained: Offer extra rewards |
| Candelabra | Uncommon | Shared | AfterSideTurnStart: Gain 2 Energy |
| CaptainsWheel | Rare | Shared | AfterBlockCleared: Gain 18 Block |
| Cauldron | Shop | Shared | AfterObtained: Offer extra rewards |
| CentennialPuzzle | Common | Shared | AfterDamageReceived: Draw 1 card |
| Chandelier | Rare | Shared | AfterSideTurnStart: Gain 3 Energy |
| CharonsAshes | Rare | Ironclad | AfterCardExhausted: Lose ? HP |
| ChemicalX | Shop | Shared | ModifyXValue: returns originalValue or originalValue + 2 |
| ChoicesParadox | Ancient | Event | AfterPlayerTurnStart: get Distinct For Combat; AfterPlayerTurnStart: Give a card Retain; AfterPlayerTurnStart: Choose a card from a pile; AfterPlayerTurnStart: Add a generated card to your hand |
| ChosenCheese | Event | Event | AfterCombatEnd: Gain 1 Max HP |
| Circlet | None | Fallback | no gameplay command in an overridden hook (passive in source) |
| Claws | Ancient | Event | AfterObtained: Transform a card in your deck; AfterObtained: Transform a card |
| CloakClasp | Rare | Shared | BeforeTurnEnd: Gain ? Block |
| CrackedCore | Starter | Defect | BeforeSideTurnStart: Channel a Lightning orb |
| Crossbow | Ancient | Event | AfterSideTurnStart: get Distinct For Combat; AfterSideTurnStart: Add a generated card to your hand |
| CursedPearl | Ancient | Event | AfterObtained: Add the curse Greed to your deck; AfterObtained: Gain 333 Gold |
| DarkstonePeriapt | Event | Event | AfterCardChangedPiles: Gain 6 Max HP |
| DataDisk | Common | Defect | AfterRoomEntered: Gain 1 Focus |
| DaughterOfTheWind | Event | Event | AfterCardPlayed: Gain 1 Block |
| DelicateFrond | Ancient | Event | BeforeCombatStart: create Random Potion Out Of Combat; BeforeCombatStart: Obtain a random potion |
| DemonTongue | Rare | Ironclad | AfterDamageReceived: Heal ? HP |
| DeprecatedRelic | None | Deprecated | no gameplay command in an overridden hook (passive in source) |
| DiamondDiadem | Ancient | Event | BeforeTurnEnd: Gain 1 Diamond Diadem |
| DingyRug | Shop | Shared | ModifyCardRewardCreationOptions: returns options or options.WithCustomPool(list) |
| DistinguishedCape | Ancient | Event | AfterObtained: Lose 9 Max HP; AfterObtained: Put a card into your deck |
| DivineDestiny | Starter | Event | AfterSideTurnStart: Gain 6 Stars |
| DivineRight | Starter | Regent | AfterRoomEntered: Gain 3 Stars |
| DollysMirror | Shop | Shared | AfterObtained: Choose a card in your deck; AfterObtained: Put a card into your deck |
| DragonFruit | Shop | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); AfterGoldGained: Gain 1 Max HP |
| DreamCatcher | Event | Event | TryModifyRestSiteHealRewards: returns false or true; ModifyExtraRestSiteHealText: returns currentExtraText or new global::_003C_003Ez__ReadOnlyArray<LocString>(array) |
| Driftwood | Ancient | Event | TryModifyRewardsLate: returns false or true |
| DustyTome | Ancient | Event | AfterObtained: Upgrade a card; AfterObtained: Put a card into your deck |
| Ectoplasm | Ancient | Event | ShouldGainGold: returns player != base.Owner; ModifyMaxEnergy: returns amount or amount + 1 |
| ElectricShrymp | Ancient | Event | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card |
| EmberTea | Event | Event | AfterRoomEntered: Gain 2 Strength |
| EmotionChip | Rare | Defect | AfterPlayerTurnStart: Trigger an orb passive |
| EmptyCage | Ancient | Event | AfterObtained: Remove a card from your deck |
| EternalFeather | Uncommon | Shared | AfterRoomEntered: Heal ? HP |
| FakeAnchor | Event | Event | BeforeCombatStart: Gain 4 Block |
| FakeBloodVial | Event | Event | AfterPlayerTurnStartLate: Heal 1 HP |
| FakeHappyFlower | Event | Event | AfterSideTurnStart: Gain 1 Energy |
| FakeLeesWaffle | Event | Event | AfterObtained: Heal ? HP |
| FakeMango | Event | Event | AfterObtained: Gain 3 Max HP |
| FakeMerchantsRug | Event | Event | no gameplay command in an overridden hook (passive in source) |
| FakeOrichalcum | Event | Event | BeforeTurnEnd: Gain 3 Block |
| FakeSneckoEye | Event | Event | AfterObtained: Gain 1 Confused; BeforeCombatStart: Gain 1 Confused |
| FakeStrikeDummy | Event | Event | ModifyDamageAdditive: returns 0 or 1 |
| FakeVenerableTeaSet | Event | Event | AfterEnergyReset: Gain 1 Energy |
| FencingManual | Common | Regent | AfterSideTurnStart: Forge 10 |
| FestivePopper | Common | Shared | AfterPlayerTurnStart: Deal 9 damage to the target |
| Fiddle | Ancient | Event | ModifyHandDrawLate: returns count or count + 2; ShouldDraw: returns true or false |
| ForgottenSoul | Event | Event | AfterCardExhausted: Deal 1 damage to the target |
| FragrantMushroom | Event | Event | AfterObtained: Lose 15 HP; AfterObtained: Upgrade a card |
| FresnelLens | Event | Shared | TryModifyCardRewardOptionsLate: returns false or true; TryModifyCardBeingAddedToDeck: returns false or true |
| FrozenEgg | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); TryModifyCardRewardOptionsLate: returns false or true; TryModifyCardBeingAddedToDeck: Upgrade a card |
| FuneraryMask | Uncommon | Necrobinder | AfterSideTurnStart: Add a generated card to your draw pile |
| FurCoat | Ancient | Event | ModifyGeneratedMapLate: returns AddMarkedRooms(map); BeforeCombatStart: set Current HP |
| GalacticDust | Uncommon | Regent | AfterStarsSpent: Gain ? Block |
| GamblingChip | Rare | Shared | AfterPlayerTurnStart: Discard 999999999 cards from your hand; AfterPlayerTurnStart: Discard your hand and draw that many cards |
| GamePiece | Rare | Shared | AfterCardPlayed: Draw 1 card |
| GhostSeed | Shop | Shared | AfterCardEnteredCombat: Give a card Ethereal; AfterRoomEntered: Give a card Ethereal |
| Girya | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); AfterRoomEntered: Gain ? Strength; TryModifyRestSiteOptions: returns false or true |
| GlassEye | Ancient | Event | AfterObtained: Offer extra rewards |
| Glitter | Ancient | Event | TryModifyCardRewardOptionsLate: Enchant a card with Glam |
| GnarledHammer | Shop | Shared | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card |
| GoldenCompass | Ancient | Event | ModifyGeneratedMap: returns map or new GoldenPathActMap(runState); ModifyUnknownMapPointRoomTypes: returns roomTypes or new HashSet<RoomType> { RoomType.Event } |
| GoldenPearl | Ancient | Event | AfterObtained: Gain 150 Gold |
| GoldPlatedCables | Uncommon | Defect | ModifyOrbPassiveTriggerCounts: returns triggerCount or triggerCount + 1 |
| Gorget | Common | Shared | AfterRoomEntered: Gain 4 Plating |
| GremlinHorn | Uncommon | Shared | AfterDeath: Gain 1 Energy; AfterDeath: Draw 1 card |
| HandDrill | Event | Event | AfterDamageGiven: Apply 2 Vulnerable to the target |
| HappyFlower | Common | Shared | AfterSideTurnStart: Gain 1 Energy |
| HelicalDart | Rare | Silent | AfterCardPlayed: Gain 1 Helical Dart |
| HistoryCourse | Event | Event | AfterPlayerTurnStartEarly: Auto-play a card |
| HornCleat | Uncommon | Shared | AfterBlockCleared: Gain 14 Block |
| IceCream | Rare | Shared | ShouldPlayerResetEnergy: returns true or false |
| InfusedCore | Starter | Event | AfterSideTurnStart: Channel a Lightning orb |
| IntimidatingHelmet | Rare | Shared | BeforeCardPlayed: Gain 4 Block |
| IronClub | Ancient | Event | AfterCardPlayed: Draw 1 card |
| IvoryTile | Rare | Necrobinder | AfterCardPlayed: Gain 1 Energy |
| JeweledMask | Ancient | Event | BeforeHandDraw: Put a card into your hand |
| JewelryBox | Ancient | Event | AfterObtained: Put a card into your deck |
| JossPaper | Uncommon | Shared | no gameplay command in an overridden hook (passive in source) |
| JuzuBracelet | Common | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); ModifyUnknownMapPointRoomTypes: returns hashSet2 |
| Kifuda | Shop | Shared | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card |
| Kunai | Rare | Shared | AfterCardPlayed: Gain 1 Dexterity |
| Kusarigama | Uncommon | Shared | AfterCardPlayed: Deal 6 damage to the target |
| Lantern | Common | Shared | AfterSideTurnStart: Gain 1 Energy |
| LargeCapsule | Ancient | Event | AfterObtained: Obtain a random relic; AfterObtained: Obtain a relic; AfterObtained: Put a card into your deck |
| LastingCandy | Rare | Event/Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); TryModifyCardRewardOptions: Offer a card reward |
| LavaLamp | Shop | Shared | TryModifyCardRewardOptionsLate: Upgrade a card |
| LavaRock | Ancient | Event | TryModifyRewards: returns false or true |
| LeadPaperweight | Ancient | Event | AfterObtained: Offer a card reward; AfterObtained: Choose a card from an offered set; AfterObtained: Put a card into your deck |
| LeafyPoultice | Ancient | Event | AfterObtained: Lose 10 Max HP; AfterObtained: Transform a card |
| LeesWaffle | Shop | Shared | AfterObtained: Gain 7 Max HP; AfterObtained: Heal ? HP |
| LetterOpener | Uncommon | Shared | AfterCardPlayed: Lose 5 HP |
| LizardTail | Rare | Shared | ShouldDieLate: returns true or false; AfterPreventingDeath: Heal ? HP |
| LoomingFruit | Ancient | Shared | AfterObtained: Gain 31 Max HP |
| LordsParasol | Ancient | Event | no gameplay command in an overridden hook (passive in source) |
| LostCoffer | Ancient | Event | AfterObtained: Offer extra rewards |
| LostWisp | Event | Event | AfterCardPlayed: Lose 8 HP |
| LuckyFysh | Uncommon | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); AfterCardChangedPiles: Gain 15 Gold |
| LunarPastry | Rare | Regent | AfterTurnEnd: Gain 1 Stars |
| Mango | Rare | Shared | AfterObtained: Gain 14 Max HP |
| MassiveScroll | Ancient | Event | AfterObtained: Offer a card reward; AfterObtained: Choose a card from an offered set; AfterObtained: Put a card into your deck |
| MawBank | Event | Event | AfterRoomEntered: Gain 12 Gold |
| MealTicket | Common | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); AfterRoomEntered: Heal 15 HP |
| MeatCleaver | Ancient | Event | TryModifyRestSiteOptions: returns false or true |
| MeatOnTheBone | Rare | Shared | BeforeCombatStart: returns creature.CurrentHp <= num; AfterCurrentHpChanged: returns creature.CurrentHp <= num; AfterCombatVictoryEarly: Heal 12 HP |
| MembershipCard | Shop | Shared | ModifyMerchantPrice: returns originalPrice or originalPrice * (50 / 100) |
| MercuryHourglass | Uncommon | Shared | AfterPlayerTurnStart: Deal 3 damage to the target |
| Metronome | Rare | Defect | AfterOrbChanneled: Lose 30 HP |
| MiniatureCannon | Uncommon | Shared | ModifyDamageAdditive: returns 0 or 3 |
| MiniatureTent | Shop | Shared | ShouldDisableRemainingRestSiteOptions: returns true or false |
| MiniRegent | Rare | Regent | AfterStarsSpent: Gain 1 Strength |
| MoltenEgg | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); TryModifyCardRewardOptionsLate: returns false or true; TryModifyCardBeingAddedToDeck: Upgrade a card |
| MrStruggles | Event | Event | AfterPlayerTurnStart: Deal ? damage to the target |
| MummifiedHand | Rare | Shared | no gameplay command in an overridden hook (passive in source) |
| MusicBox | Ancient | Event | AfterCardPlayed: Give a card Ethereal; AfterCardPlayed: Add a generated card to your hand |
| MysticLighter | Shop | Shared | ModifyDamageAdditive: returns 0 or 9 |
| NeowsTorment | Ancient | Event | AfterObtained: Put a card into your deck |
| NewLeaf | Ancient | Event | AfterObtained: Transform a card in your deck; AfterObtained: Transform a card into a random card |
| NinjaScroll | Shop | Silent | BeforeHandDraw: Add 3 Shivs to your hand |
| Nunchaku | Uncommon | Shared | AfterCardPlayed: Gain 1 Energy |
| NutritiousOyster | Ancient | Event | AfterObtained: Gain 11 Max HP |
| NutritiousSoup | Ancient | Event | AfterObtained: Enchant a card with Tezcataras Ember |
| OddlySmoothStone | Common | Shared | AfterRoomEntered: Gain 1 Dexterity |
| OldCoin | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); AfterObtained: Gain 300 Gold |
| OrangeDough | Rare | Regent | AfterSideTurnStart: get Distinct For Combat; AfterSideTurnStart: Add a generated card to your hand |
| Orichalcum | Uncommon | Shared | BeforeTurnEnd: Gain 6 Block |
| OrnamentalFan | Uncommon | Shared | AfterCardPlayed: Gain 4 Block |
| Orrery | Shop | Shared | AfterObtained: Offer extra rewards |
| PaelsBlood | Ancient | Event | ModifyHandDraw: returns count or count + 1 |
| PaelsClaw | Ancient | Event | AfterObtained: Enchant a card with Goopy |
| PaelsEye | Ancient | Event | ShouldTakeExtraTurn: returns player == base.Owner or false; BeforeTurnEndEarly: Exhaust a card |
| PaelsFlesh | Ancient | Event | AfterSideTurnStart: Gain 1 Energy |
| PaelsGrowth | Ancient | Event | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card with Clone; TryModifyRestSiteOptions: returns false or true |
| PaelsHorn | Ancient | Event | AfterObtained: Put a card into your deck |
| PaelsLegion | Ancient | Event | AfterObtained: add Pet Mega Crit Sts2 Core Models Monsters Paels Legion; BeforeCombatStart: add Pet Mega Crit Sts2 Core Models Monsters Paels Legion; ModifyBlockMultiplicative: returns 1 or 2 |
| PaelsTears | Ancient | Event | AfterSideTurnStart: Gain 2 Energy |
| PaelsTooth | Ancient | Event | AfterObtained: Remove a card from your deck; AfterCombatEnd: Upgrade a card; AfterCombatEnd: Put a card into your deck |
| PaelsWing | Ancient | Event | TryModifyCardRewardAlternatives: returns false or true |
| PandorasBox | Ancient | Event | AfterObtained: create Random Card For Transform; AfterObtained: Transform a card |
| Pantograph | Uncommon | Shared | AfterRoomEntered: Heal 25 HP |
| PaperKrane | Rare | Silent | no gameplay command in an overridden hook (passive in source) |
| PaperPhrog | Uncommon | Ironclad | no gameplay command in an overridden hook (passive in source) |
| ParryingShield | Uncommon | Shared | AfterTurnEnd: Deal 6 damage to the target |
| Pear | Uncommon | Shared | AfterObtained: Gain 10 Max HP |
| Pendulum | Common | Shared | AfterShuffle: Draw 1 card |
| PenNib | Uncommon | Shared | ModifyDamageMultiplicative: returns 1 or 2 |
| Permafrost | Common | Shared | AfterCardPlayed: Gain 6 Block |
| PetrifiedToad | Uncommon | Shared | BeforeCombatStartLate: Obtain a random potion |
| PhilosophersStone | Ancient | Event | ModifyMaxEnergy: returns amount or amount + 1; AfterCreatureAddedToCombat: Apply 1 Strength to the target; AfterRoomEntered: Apply 1 Strength to the target |
| PhylacteryUnbound | Starter | Event | BeforeCombatStart: Summon 5; AfterSideTurnStart: Summon 2 |
| Planisphere | Uncommon | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); AfterRoomEntered: Heal 4 HP |
| Pocketwatch | Rare | Shared | ModifyHandDraw: returns count or count + 3 |
| PollinousCore | Event | Event | ModifyHandDraw: returns count or count + 2 |
| Pomander | Ancient | Event | AfterObtained: Upgrade a card in your deck; AfterObtained: Upgrade a card |
| PotionBelt | Common | Shared | AfterObtained: gain Max Potion Count |
| PowerCell | Rare | Defect | BeforeSideTurnStart: Put a card into your hand |
| PrayerWheel | Rare | Shared | TryModifyRewards: returns false or true |
| PrecariousShears | Ancient | Event | AfterObtained: Remove a card from your deck; AfterObtained: Lose 13 HP |
| PreciseScissors | Ancient | Event | AfterObtained: Remove a card from your deck |
| PreservedFog | Ancient | Event | AfterObtained: Remove a card from your deck; AfterObtained: Add the curse Folly to your deck |
| PrismaticGem | Ancient | Event | ModifyMaxEnergy: returns amount or amount + 1; ModifyCardRewardCreationOptions: returns options or options.WithCardPools(pools, options.CardPoolFilter) |
| PumpkinCandle | Ancient | Event | ModifyMaxEnergy: returns amount or amount + 1 |
| PunchDagger | Shop | Shared | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card |
| RadiantPearl | Ancient | Event | BeforeHandDraw: Add a generated card to your hand |
| RainbowRing | Rare | Shared | AfterCardPlayed: Gain 1 Strength; AfterCardPlayed: Gain 1 Dexterity |
| RazorTooth | Rare | Event/Shared | AfterCardPlayed: Upgrade a card |
| RedMask | Uncommon | Shared | BeforeSideTurnStart: Apply 1 Weak to the target |
| RedSkull | Common | Ironclad | AfterRoomEntered: Apply -3 Strength to the target; AfterRoomEntered: Apply 3 Strength to the target; AfterCurrentHpChanged: Apply -3 Strength to the target; AfterCurrentHpChanged: Apply 3 Strength to the target |
| Regalite | Uncommon | Regent | AfterCardEnteredCombat: Gain 2 Block |
| RegalPillow | Common | Shared | ModifyRestSiteHealAmount: returns amount or amount + 15; ModifyExtraRestSiteHealText: returns currentExtraText or new global::_003C_003Ez__ReadOnlyArray<LocString>(array) |
| ReptileTrinket | Uncommon | Shared | AfterPotionUsed: Gain 3 Reptile Trinket |
| RingingTriangle | Shop | Shared | ShouldFlush: returns true or player.Creature.CombatState.RoundNumber > 1 |
| RingOfTheDrake | Starter | Event | ModifyHandDraw: returns count or count + 2 |
| RingOfTheSnake | Starter | Silent | ModifyHandDraw: returns count or count + 2 |
| RippleBasin | Uncommon | Shared | BeforeTurnEnd: Gain 4 Block |
| RoyalPoison | Event | Event | AfterPlayerTurnStart: Lose 4 HP |
| RoyalStamp | Shop | Shared | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card with Royally Approved |
| RuinedHelmet | Rare | Ironclad | TryModifyPowerAmountReceived: returns false or true |
| RunicCapacitor | Shop | Defect | AfterSideTurnStart: Add 3 orb slot(s) |
| RunicPyramid | Ancient | Event | ShouldFlush: returns true or false |
| Sai | Ancient | Event | AfterSideTurnStart: Gain 7 Block |
| SandCastle | Ancient | Event | AfterObtained: Upgrade a card |
| ScreamingFlagon | Shop | Shared | BeforeTurnEnd: Lose 20 HP |
| ScrollBoxes | Ancient | Event | AfterObtained: Lose ? Gold; AfterObtained: from Choose A Bundle Screen; AfterObtained: Put a card into your deck |
| SeaGlass | Ancient | Event | AfterObtained: Offer a card reward; AfterObtained: Choose a card from an offered set; AfterObtained: Put a card into your deck |
| SealOfGold | Ancient | Event | AfterSideTurnStart: Gain 1 Energy; AfterSideTurnStart: Lose 5 Gold |
| SelfFormingClay | Uncommon | Ironclad | AfterDamageReceived: Gain 3 Self Forming Clay |
| SereTalon | Ancient | Event | AfterObtained: Put a card into your deck |
| Shovel | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); TryModifyRestSiteOptions: returns false or true |
| Shuriken | Rare | Shared | AfterCardPlayed: Gain 1 Strength |
| SignetRing | Ancient | Event | AfterObtained: Gain 999 Gold |
| SilverCrucible | Ancient | Event | TryModifyCardRewardOptionsLate: Upgrade a card; ShouldGenerateTreasure: returns true or TreasureRoomsEntered > 1 |
| SlingOfCourage | Shop | Shared | AfterRoomEntered: Gain 2 Strength |
| SmallCapsule | Ancient | Event | AfterObtained: Offer extra rewards |
| SneckoEye | Ancient | Event | AfterObtained: Gain 1 Confused; BeforeCombatStart: Gain 1 Confused; ModifyHandDraw: returns count or count + 2 |
| SneckoSkull | Common | Silent | ModifyPowerAmountGiven: returns amount or amount + 1 |
| Sozu | Ancient | Event | ShouldProcurePotion: returns player != base.Owner; ModifyMaxEnergy: returns amount or amount + 1 |
| SparklingRouge | Uncommon | Event/Shared | AfterBlockCleared: Gain 1 Strength; AfterBlockCleared: Gain 1 Dexterity |
| SpikedGauntlets | Ancient | Event | ModifyMaxEnergy: returns amount or amount + 1; TryModifyEnergyCostInCombat: returns false or true |
| StoneCalendar | Rare | Shared | BeforeTurnEnd: Lose 52 HP |
| StoneCracker | Uncommon | Shared | AfterRoomEntered: Upgrade a card |
| StoneHumidifier | Ancient | Event | AfterRestSiteHeal: Gain 5 Max HP; ModifyExtraRestSiteHealText: returns currentExtraText or new global::_003C_003Ez__ReadOnlyArray<LocString>(array) |
| Storybook | Ancient | Event | AfterObtained: Put a card into your deck |
| Strawberry | Common | Shared | AfterObtained: Gain 7 Max HP |
| StrikeDummy | Common | Shared | ModifyDamageAdditive: returns 0 or 3 |
| SturdyClamp | Rare | Shared | ShouldClearBlock: returns true or false; AfterPreventingBlockClear: Lose all Block |
| SwordOfJade | Event | Event | AfterRoomEntered: Gain 3 Strength |
| SwordOfStone | Event | Event | AfterCombatVictory: replace |
| SymbioticVirus | Uncommon | Defect | AfterSideTurnStart: Channel a Dark orb |
| TanxsWhistle | Ancient | Event | AfterObtained: Put a card into your deck |
| TeaOfDiscourtesy | Event | Event | BeforeCombatStart: add To Combat And Preview Dazed |
| TheAbacus | Shop | Shared | AfterShuffle: Gain 6 Block |
| TheBoot | Event | Event | ModifyHpLostBeforeOsty: returns amount or 5 |
| TheCourier | Rare | Shared | ModifyMerchantPrice: returns originalPrice or originalPrice * (1 - 20 / 100); ShouldRefillMerchantEntry: returns player == base.Owner |
| ThrowingAxe | Ancient | Event | ModifyCardPlayCount: returns playCount or playCount + 1 |
| Tingsha | Uncommon | Silent | AfterCardDiscarded: Deal 3 damage to the target |
| TinyMailbox | Common | Shared | TryModifyRestSiteHealRewards: returns false or true; ModifyExtraRestSiteHealText: returns currentExtraText or new global::_003C_003Ez__ReadOnlyArray<LocString>(array) |
| ToastyMittens | Ancient | Event | BeforeHandDraw: Shuffle your draw pile; BeforeHandDraw: Exhaust a card; BeforeHandDraw: Apply 1 Strength to the target |
| Toolbox | Shop | Shared | BeforeHandDraw: get Distinct For Combat; BeforeHandDraw: Choose a card from an offered set; BeforeHandDraw: Add a generated card to your hand |
| TouchOfOrobas | Ancient | Event | AfterObtained: replace |
| ToughBandages | Rare | Silent | AfterCardDiscarded: Gain 3 Block |
| ToxicEgg | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); TryModifyCardRewardOptionsLate: returns false or true; TryModifyCardBeingAddedToDeck: Upgrade a card |
| ToyBox | Ancient | Event | AfterObtained: Obtain a random relic; AfterObtained: Offer extra rewards; AfterCombatEnd: melt |
| TriBoomerang | Ancient | Event | AfterObtained: Enchant a card in your deck; AfterObtained: Enchant a card with Instinct |
| TungstenRod | Rare | Shared | ModifyHpLostAfterOsty: returns amount or Math.Max(0, amount - 1) |
| TuningFork | Uncommon | Shared | AfterCardPlayed: Gain 7 Block |
| TwistedFunnel | Uncommon | Silent | BeforeSideTurnStart: Apply 4 Poison to the target |
| UnceasingTop | Rare | Shared | AfterHandEmptied: Draw 1 card |
| UndyingSigil | Shop | Necrobinder | ModifyDamageMultiplicative: returns 1 or 0.5 |
| UnsettlingLamp | Rare | Shared | ModifyPowerAmountGiven: returns amount or amount * 2 |
| Vajra | Common | Shared | AfterRoomEntered: Gain 1 Strength |
| Vambrace | Uncommon | Shared | ModifyBlockMultiplicative: returns 1 or 2 |
| VelvetChoker | Ancient | Event | ModifyMaxEnergy: returns amount or amount + 1; ShouldPlay: returns true or !ShouldPreventCardPlay |
| VenerableTeaSet | Common | Shared | AfterEnergyReset: Gain 2 Energy |
| VeryHotCocoa | Ancient | Shared | AfterSideTurnStart: Gain 4 Energy |
| VexingPuzzlebox | Rare | Shared | AfterPlayerTurnStart: get Distinct For Combat; AfterPlayerTurnStart: Add a generated card to your hand |
| VitruvianMinion | Shop | Regent | ModifyDamageMultiplicative: returns 1 or 2; ModifyBlockMultiplicative: returns 1 or 2 |
| WarHammer | Ancient | Event | AfterCombatVictory: Upgrade a card |
| WarPaint | Common | Shared | AfterObtained: Upgrade a card |
| Whetstone | Common | Shared | AfterObtained: Upgrade a card |
| WhisperingEarring | Ancient | Event | ModifyMaxEnergy: returns amount or amount + 1; BeforePlayPhaseStart: push Selector; BeforePlayPhaseStart: Auto-play a card; BeforePlayPhaseStart: play |
| WhiteBeastStatue | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); ShouldForcePotionReward: returns false or true |
| WhiteStar | Rare | Shared | IsAllowed: returns RelicModel.IsBeforeAct3TreasureChest(runState); TryModifyRewards: returns false or true |
| WingCharm | Shop | Shared | TryModifyCardRewardOptionsLate: Enchant a card with Swift |
| WongoCustomerAppreciationBadge | Event | Event | no gameplay command in an overridden hook (passive in source) |
| WongosMysteryTicket | Event | Event | TryModifyRewards: returns false or true |
| YummyCookie | Ancient | Event | AfterObtained: Upgrade a card in your deck; AfterObtained: Upgrade a card |
