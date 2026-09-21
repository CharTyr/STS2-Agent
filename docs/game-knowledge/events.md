# Event Index

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-09-21 22:31:54 +08:00

Event lookup by internal name and base type, plus per-option consequence and risk grading derived from the event sources.

## Event Index

`Layout` is the event layout the source declares (`Combat` events start a fight; `Ancient` is the
AncientEventModel default). `Options` counts the distinct options the source builds for the event,
and `HighestRisk` is the strongest risk tag among them.

| Name | BaseType | Layout | Encounter | Options | HighestRisk |
| --- | --- | --- | --- | --- | --- |
| AbyssalBaths | EventModel | Default | - | 4 | lethal-possible |
| Amalgamator | EventModel | Default | - | 2 | costly |
| AromaOfChaos | EventModel | Default | - | 2 | none-detected |
| BattlewornDummy | EventModel | Default | - | 3 | none-detected |
| BrainLeech | EventModel | Default | - | 2 | lethal-possible |
| Bugslayer | EventModel | Default | - | 2 | none-detected |
| ByrdonisNest | EventModel | Default | - | 2 | none-detected |
| ColorfulPhilosophers | EventModel | Default | - | 1 | none-detected |
| ColossalFlower | EventModel | Default | - | 6 | lethal-possible |
| CrystalSphere | EventModel | Default | - | 2 | harmful |
| Darv | AncientEventModel | Ancient | - | 1 | none-detected |
| DenseVegetation | EventModel | Default | - | 3 | lethal-possible |
| DeprecatedAncientEvent | AncientEventModel | Ancient | - | 0 | unknown |
| DeprecatedEvent | EventModel | Default | - | 0 | unknown |
| DollRoom | EventModel | Default | - | 4 | lethal-possible |
| DoorsOfLightAndDark | EventModel | Default | - | 2 | costly |
| DrowningBeacon | EventModel | Default | - | 2 | harmful |
| EndlessConveyor | EventModel | Default | - | 4 | costly |
| FakeMerchant | EventModel | Custom | - | 0 | unknown |
| FieldOfManSizedHoles | EventModel | Default | - | 2 | harmful |
| GraveOfTheForgotten | EventModel | Default | - | 3 | harmful |
| HungryForMushrooms | EventModel | Default | - | 2 | none-detected |
| InfestedAutomaton | EventModel | Default | - | 2 | none-detected |
| JungleMazeAdventure | EventModel | Default | - | 2 | lethal-possible |
| LostWisp | EventModel | Default | - | 2 | none-detected |
| LuminousChoir | EventModel | Default | - | 3 | harmful |
| MorphicGrove | EventModel | Default | - | 2 | costly |
| Neow | AncientEventModel | Ancient | - | 21 | none-detected |
| Nonupeipe | AncientEventModel | Ancient | - | 10 | none-detected |
| Orobas | AncientEventModel | Ancient | - | 8 | none-detected |
| Pael | AncientEventModel | Ancient | - | 10 | none-detected |
| PotionCourier | EventModel | Default | - | 2 | none-detected |
| PunchOff | EventModel | Combat | PunchOffEventEncounter | 3 | harmful |
| RanwidTheElder | EventModel | Default | - | 4 | costly |
| Reflections | EventModel | Default | - | 2 | harmful |
| RelicTrader | EventModel | Default | - | 4 | none-detected |
| RoomFullOfCheese | EventModel | Default | - | 2 | lethal-possible |
| RoundTeaParty | EventModel | Default | - | 3 | harmful |
| SapphireSeed | EventModel | Default | - | 2 | none-detected |
| SelfHelpBook | EventModel | Default | - | 7 | none-detected |
| SlipperyBridge | EventModel | Default | - | 3 | lethal-possible |
| SpiralingWhirlpool | EventModel | Default | - | 2 | none-detected |
| SpiritGrafter | EventModel | Default | - | 2 | lethal-possible |
| StoneOfAllTime | EventModel | Default | - | 4 | lethal-possible |
| SunkenStatue | EventModel | Default | - | 2 | lethal-possible |
| SunkenTreasury | EventModel | Default | - | 2 | harmful |
| Symbiote | EventModel | Default | - | 3 | none-detected |
| TabletOfTruth | EventModel | Default | - | 4 | lethal-possible |
| Tanx | AncientEventModel | Ancient | - | 10 | none-detected |
| TeaMaster | EventModel | Default | - | 5 | costly |
| Tezcatara | AncientEventModel | Ancient | - | 10 | none-detected |
| TheArchitect | EventModel | Combat | TheArchitectEventEncounter | 2 | none-detected |
| TheFutureOfPotions | EventModel | Default | - | 1 | none-detected |
| TheLanternKey | EventModel | Combat | MysteriousKnightEventEncounter | 3 | none-detected |
| TheLegendsWereTrue | EventModel | Default | - | 2 | lethal-possible |
| ThisOrThat | EventModel | Default | - | 2 | lethal-possible |
| TinkerTime | EventModel | Default | - | 5 | none-detected |
| TrashHeap | EventModel | Default | - | 2 | lethal-possible |
| Trial | EventModel | Default | - | 10 | lethal-possible |
| UnrestSite | EventModel | Default | - | 2 | harmful |
| Vakuu | AncientEventModel | Ancient | - | 10 | none-detected |
| WarHistorianRepy | EventModel | Default | - | 2 | costly |
| WaterloggedScriptorium | EventModel | Default | - | 5 | costly |
| WelcomeToWongos | EventModel | Default | - | 7 | costly |
| Wellspring | EventModel | Default | - | 2 | costly |
| WhisperingHollow | EventModel | Default | - | 2 | lethal-possible |
| WoodCarvings | EventModel | Default | - | 4 | none-detected |
| ZenWeaver | EventModel | Default | - | 4 | costly |

## Option Risk Details

Every distinct option the event builds, in source order. `Option` is the option's localization key
without the `<EVENT>.pages.` prefix; options an Ancient creates through `RelicOption<T>` are
labelled after that helper instead of a literal key.

`Risk` is graded from the handler body and the option's own markers:

- `lethal-possible` - the source marks the option with `ThatDoesDamage`/`ThatWillKillPlayerIf`,
  so the game itself can treat the choice as fatal.
- `harmful` - the handler damages the player, burns Max HP, adds a curse, or takes a relic/potion.
- `costly` - the handler only spends Gold or removes/downgrades a card.
- `none-detected` - no harmful command was found in the handler body. This is an absence of
  evidence, not a guarantee.
- `locked` - the option has no handler and cannot be chosen.
- `unknown` - the handler could not be read from the source.

`Cost` and `Effect` only list what the handler body shows; `none detected` means nothing of
that kind was found, `?` means the amount is computed at runtime. `Continuation` records whether
choosing the option ends the event, leads to another page, or offers the same option again.

| Event | Option | Handler | Effect | Cost | Risk | Continuation |
| --- | --- | --- | --- | --- | --- | --- |
| AbyssalBaths | INITIAL.options.IMMERSE | Immerse | Gain 2 Max HP | Lose 3 HP; event is marked lethal at ? current HP | lethal-possible | next page |
| AbyssalBaths | INITIAL.options.ABSTAIN | Abstain | Heal 10 HP | none detected | none-detected | ends event |
| AbyssalBaths | ALL.options.LINGER | Linger | Gain 2 Max HP | Lose 3 HP; event is marked lethal at ? current HP | lethal-possible | repeats this option (pages) |
| AbyssalBaths | ALL.options.EXIT_BATHS | ExitBaths | none detected | none detected | none-detected | ends event |
| Amalgamator | INITIAL.options.COMBINE_STRIKES | CombineStrikes | Put a card into your deck | Remove a card from your deck | costly | ends event |
| Amalgamator | INITIAL.options.COMBINE_DEFENDS | CombineDefends | Put a card into your deck | Remove a card from your deck | costly | ends event |
| AromaOfChaos | INITIAL.options.LET_GO | LetGo | Transform a card in your deck; Transform a card into a random card | none detected | none-detected | ends event |
| AromaOfChaos | INITIAL.options.MAINTAIN_CONTROL | MaintainControl | Upgrade a card in your deck; Upgrade a card | none detected | none-detected | ends event |
| BattlewornDummy | INITIAL.options.SETTING_1 | Setting1 | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BattlewornDummy | INITIAL.options.SETTING_2 | Setting2 | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BattlewornDummy | INITIAL.options.SETTING_3 | Setting3 | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BrainLeech | INITIAL.options.SHARE_KNOWLEDGE | ShareKnowledge | Offer a card reward; Choose a card from an offered set; Put a card into your deck | none detected | none-detected | ends event |
| BrainLeech | INITIAL.options.RIP | Rip | Offer extra rewards | Lose ? HP; event is marked lethal at 5 current HP | lethal-possible | ends event |
| Bugslayer | INITIAL.options.EXTERMINATION | Extermination | none detected | none detected | none-detected | unknown (handler not resolvable) |
| Bugslayer | INITIAL.options.SQUASH | Squash | none detected | none detected | none-detected | unknown (handler not resolvable) |
| ByrdonisNest | INITIAL.options.EAT | Eat | Gain 7 Max HP | none detected | none-detected | ends event |
| ByrdonisNest | INITIAL.options.TAKE | Take | Put a card into your deck | none detected | none-detected | ends event |
| ColorfulPhilosophers | unknown | (inline) | Offer extra rewards | none detected | none-detected | ends event |
| ColossalFlower | INITIAL.options.EXTRACT_CURRENT_PRIZE_* | ExtractCurrentPrize | Gain ? Gold | none detected | none-detected | ends event |
| ColossalFlower | INITIAL.options.REACH_DEEPER_* | ReachDeeper | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| ColossalFlower | REACH_DEEPER_*.options.EXTRACT_CURRENT_PRIZE_* | ExtractCurrentPrize | Gain ? Gold | none detected | none-detected | ends event |
| ColossalFlower | REACH_DEEPER_*.options.REACH_DEEPER_* | ReachDeeper | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| ColossalFlower | REACH_DEEPER_2.options.EXTRACT_INSTEAD | ExtractInstead | Gain ? Gold | none detected | none-detected | ends event |
| ColossalFlower | REACH_DEEPER_2.options.POLLINOUS_CORE | ObtainPollinousCore | Obtain the relic Pollinous Core | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | ends event |
| CrystalSphere | INITIAL.options.UNCOVER_FUTURE | UncoverFuture | none detected | Lose 50 Gold | costly | ends event |
| CrystalSphere | INITIAL.options.PAYMENT_PLAN | PaymentPlan | none detected | Add the curse Debt to your deck | harmful | ends event |
| Darv | INITIAL.options.DUSTY_TOME | RelicOption<DustyTome> | Obtain the relic Dusty Tome; the source finishes the event after it | none detected | none-detected | ends event |
| DenseVegetation | INITIAL.options.TRUDGE_ON | TrudgeOn | none detected | Remove a card from your deck; Lose 11 HP; event is marked lethal at 11 current HP | lethal-possible | ends event |
| DenseVegetation | INITIAL.options.REST | Rest | Heal as if you had rested | none detected | none-detected | next page |
| DenseVegetation | REST.options.FIGHT | Fight | none detected | none detected | none-detected | unknown (handler not resolvable) |
| DollRoom | INITIAL.options.RANDOM | ChooseRandom | none detected | none detected | none-detected | unknown (handler not resolvable) |
| DollRoom | INITIAL.options.TAKE_SOME_TIME | TakeSomeTime | none detected | Lose ? HP; event is marked lethal at 5 current HP | lethal-possible | next page |
| DollRoom | INITIAL.options.EXAMINE | Examine | none detected | Lose ? HP; event is marked lethal at 15 current HP | lethal-possible | next page |
| DollRoom | unknown | Func | none detected | none detected | none-detected | unknown (handler not resolvable) |
| DoorsOfLightAndDark | INITIAL.options.LIGHT | Light | Upgrade a card | none detected | none-detected | ends event |
| DoorsOfLightAndDark | INITIAL.options.DARK | Dark | none detected | Remove a card from your deck | costly | ends event |
| DrowningBeacon | INITIAL.options.BOTTLE | BottleOption | Offer extra rewards | none detected | none-detected | ends event |
| DrowningBeacon | INITIAL.options.CLIMB | ClimbOption | Obtain the relic Fresnel Lens | Lose 13 Max HP | harmful | ends event |
| EndlessConveyor | INITIAL.options.OBSERVE_CHEF | ObserveChef | Upgrade a card | none detected | none-detected | ends event |
| EndlessConveyor | GRAB_SOMETHING_OFF_THE_BELT.options.LEAVE | Leave | none detected | none detected | none-detected | ends event |
| EndlessConveyor | unknown | GrabSomethingOffTheBelt | none detected | Lose 35 Gold | costly | next page |
| EndlessConveyor | ALL.options.LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| FieldOfManSizedHoles | INITIAL.options.RESIST | Resist | none detected | Remove a card from your deck; Add curses to your deck | harmful | ends event |
| FieldOfManSizedHoles | INITIAL.options.ENTER_YOUR_HOLE | EnterYourHole | Enchant a card in your deck; Enchant a card with Perfect Fit | none detected | none-detected | ends event |
| GraveOfTheForgotten | INITIAL.options.CONFRONT_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| GraveOfTheForgotten | INITIAL.options.CONFRONT | Confront | Enchant a card in your deck; Enchant a card with Souls Power | Add the curse Decay to your deck | harmful | ends event |
| GraveOfTheForgotten | INITIAL.options.ACCEPT | Accept | Obtain the relic Forgotten Soul | none detected | none-detected | ends event |
| HungryForMushrooms | INITIAL.options.BIG_MUSHROOM | RelicOption<BigMushroom> | Obtain the relic Big Mushroom; the source finishes the event after it | none detected | none-detected | ends event |
| HungryForMushrooms | INITIAL.options.FRAGRANT_MUSHROOM | RelicOption<FragrantMushroom> | Obtain the relic Fragrant Mushroom; the source finishes the event after it | none detected | none-detected | ends event |
| InfestedAutomaton | INITIAL.options.STUDY | Study | Offer a card reward; Put a card into your deck | none detected | none-detected | ends event |
| InfestedAutomaton | INITIAL.options.TOUCH_CORE | TouchCore | Offer a card reward; Put a card into your deck | none detected | none-detected | ends event |
| JungleMazeAdventure | INITIAL.options.SOLO_QUEST | DontNeedHelp | Gain 150 Gold | Lose 18 HP; event is marked lethal at 18 current HP | lethal-possible | ends event |
| JungleMazeAdventure | INITIAL.options.JOIN_FORCES | SafetyInNumbers | Gain 50 Gold | none detected | none-detected | ends event |
| LostWisp | INITIAL.options.CLAIM | onChosen | none detected | none detected | none-detected | unknown (handler not resolvable) |
| LostWisp | INITIAL.options.SEARCH | Search | Gain 60 Gold | none detected | none-detected | ends event |
| LuminousChoir | INITIAL.options.REACH_INTO_THE_FLESH | ReachIntoTheFlesh | none detected | Remove a card from your deck; Add the curse Spore Mind to your deck | harmful | ends event |
| LuminousChoir | INITIAL.options.OFFER_TRIBUTE | OfferTribute | Obtain a random relic; Obtain a relic | Lose 149 Gold | costly | ends event |
| LuminousChoir | INITIAL.options.OFFER_TRIBUTE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| MorphicGrove | INITIAL.options.GROUP | Group | Transform a card in your deck; Transform a card into a random card | Lose 100 Gold | costly | ends event |
| MorphicGrove | INITIAL.options.LONER | Loner | Gain 5 Max HP | none detected | none-detected | ends event |
| Neow | unknown | (inline) | none detected | none detected | none-detected | ends event |
| Neow | INITIAL.options.ARCANE_SCROLL | RelicOption<ArcaneScroll> | Obtain the relic Arcane Scroll; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.BOOMING_CONCH | RelicOption<BoomingConch> | Obtain the relic Booming Conch; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.POMANDER | RelicOption<Pomander> | Obtain the relic Pomander; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.GOLDEN_PEARL | RelicOption<GoldenPearl> | Obtain the relic Golden Pearl; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.LEAD_PAPERWEIGHT | RelicOption<LeadPaperweight> | Obtain the relic Lead Paperweight; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.NEW_LEAF | RelicOption<NewLeaf> | Obtain the relic New Leaf; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.NEOWS_TORMENT | RelicOption<NeowsTorment> | Obtain the relic Neows Torment; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.PRECISE_SCISSORS | RelicOption<PreciseScissors> | Obtain the relic Precise Scissors; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.LOST_COFFER | RelicOption<LostCoffer> | Obtain the relic Lost Coffer; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.NUTRITIOUS_OYSTER | RelicOption<NutritiousOyster> | Obtain the relic Nutritious Oyster; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.STONE_HUMIDIFIER | RelicOption<StoneHumidifier> | Obtain the relic Stone Humidifier; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.MASSIVE_SCROLL | RelicOption<MassiveScroll> | Obtain the relic Massive Scroll; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.LAVA_ROCK | RelicOption<LavaRock> | Obtain the relic Lava Rock; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.SMALL_CAPSULE | RelicOption<SmallCapsule> | Obtain the relic Small Capsule; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.SILVER_CRUCIBLE | RelicOption<SilverCrucible> | Obtain the relic Silver Crucible; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.CURSED_PEARL | RelicOption<CursedPearl> | Obtain the relic Cursed Pearl; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.LARGE_CAPSULE | RelicOption<LargeCapsule> | Obtain the relic Large Capsule; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.LEAFY_POULTICE | RelicOption<LeafyPoultice> | Obtain the relic Leafy Poultice; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.PRECARIOUS_SHEARS | RelicOption<PrecariousShears> | Obtain the relic Precarious Shears; the source finishes the event after it | none detected | none-detected | ends event |
| Neow | INITIAL.options.SCROLL_BOXES | RelicOption<ScrollBoxes> | Obtain the relic Scroll Boxes; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.BLESSED_ANTLER | RelicOption<BlessedAntler> | Obtain the relic Blessed Antler; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.BRILLIANT_SCARF | RelicOption<BrilliantScarf> | Obtain the relic Brilliant Scarf; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.DELICATE_FROND | RelicOption<DelicateFrond> | Obtain the relic Delicate Frond; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.DIAMOND_DIADEM | RelicOption<DiamondDiadem> | Obtain the relic Diamond Diadem; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.FUR_COAT | RelicOption<FurCoat> | Obtain the relic Fur Coat; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.GLITTER | RelicOption<Glitter> | Obtain the relic Glitter; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.JEWELRY_BOX | RelicOption<JewelryBox> | Obtain the relic Jewelry Box; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.LOOMING_FRUIT | RelicOption<LoomingFruit> | Obtain the relic Looming Fruit; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.SIGNET_RING | RelicOption<SignetRing> | Obtain the relic Signet Ring; the source finishes the event after it | none detected | none-detected | ends event |
| Nonupeipe | INITIAL.options.BEAUTIFUL_BRACELET | RelicOption<BeautifulBracelet> | Obtain the relic Beautiful Bracelet; the source finishes the event after it | none detected | none-detected | ends event |
| Orobas | INITIAL.options.OPTION_POOL_3_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| Orobas | INITIAL.options.ELECTRIC_SHRYMP | RelicOption<ElectricShrymp> | Obtain the relic Electric Shrymp; the source finishes the event after it | none detected | none-detected | ends event |
| Orobas | INITIAL.options.GLASS_EYE | RelicOption<GlassEye> | Obtain the relic Glass Eye; the source finishes the event after it | none detected | none-detected | ends event |
| Orobas | INITIAL.options.SAND_CASTLE | RelicOption<SandCastle> | Obtain the relic Sand Castle; the source finishes the event after it | none detected | none-detected | ends event |
| Orobas | INITIAL.options.ALCHEMICAL_COFFER | RelicOption<AlchemicalCoffer> | Obtain the relic Alchemical Coffer; the source finishes the event after it | none detected | none-detected | ends event |
| Orobas | INITIAL.options.DRIFTWOOD | RelicOption<Driftwood> | Obtain the relic Driftwood; the source finishes the event after it | none detected | none-detected | ends event |
| Orobas | INITIAL.options.RADIANT_PEARL | RelicOption<RadiantPearl> | Obtain the relic Radiant Pearl; the source finishes the event after it | none detected | none-detected | ends event |
| Orobas | INITIAL.options.PRISMATIC_GEM | RelicOption<PrismaticGem> | Obtain the relic Prismatic Gem; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_CLAW | RelicOption<PaelsClaw> | Obtain the relic Paels Claw; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_TOOTH | RelicOption<PaelsTooth> | Obtain the relic Paels Tooth; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_GROWTH | RelicOption<PaelsGrowth> | Obtain the relic Paels Growth; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_LEGION | RelicOption<PaelsLegion> | Obtain the relic Paels Legion; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_FLESH | RelicOption<PaelsFlesh> | Obtain the relic Paels Flesh; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_HORN | RelicOption<PaelsHorn> | Obtain the relic Paels Horn; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_TEARS | RelicOption<PaelsTears> | Obtain the relic Paels Tears; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_WING | RelicOption<PaelsWing> | Obtain the relic Paels Wing; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_EYE | RelicOption<PaelsEye> | Obtain the relic Paels Eye; the source finishes the event after it | none detected | none-detected | ends event |
| Pael | INITIAL.options.PAELS_BLOOD | RelicOption<PaelsBlood> | Obtain the relic Paels Blood; the source finishes the event after it | none detected | none-detected | ends event |
| PotionCourier | INITIAL.options.GRAB_POTIONS | GrabPotions | Offer extra rewards | none detected | none-detected | ends event |
| PotionCourier | INITIAL.options.RANSACK | Ransack | Offer extra rewards | none detected | none-detected | ends event |
| PunchOff | INITIAL.options.NAB | Nab | Offer extra rewards | Add the curse Injury to your deck | harmful | ends event |
| PunchOff | INITIAL.options.I_CAN_TAKE_THEM | TakeThem | none detected | none detected | none-detected | next page |
| PunchOff | I_CAN_TAKE_THEM.options.FIGHT | Fight | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RanwidTheElder | unknown | (inline) | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RanwidTheElder | INITIAL.options.POTION_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| RanwidTheElder | INITIAL.options.GOLD | GiveGold | Obtain a random relic; Obtain a relic | Lose 100 Gold | costly | ends event |
| RanwidTheElder | INITIAL.options.RELIC_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| Reflections | INITIAL.options.TOUCH_A_MIRROR | TouchAMirror | Upgrade a card | Downgrade a card | costly | ends event |
| Reflections | INITIAL.options.SHATTER | Shatter | Put a card into your deck | Add the curse Bad Luck to your deck | harmful | ends event |
| RelicTrader | INITIAL.options.TOP | Top | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RelicTrader | INITIAL.options.MIDDLE | Middle | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RelicTrader | INITIAL.options.BOTTOM | Bottom | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RelicTrader | PROCEED | Done | none detected | none detected | none-detected | ends event |
| RoomFullOfCheese | INITIAL.options.GORGE | Gorge | Offer a card reward; Choose a card from an offered set; Put a card into your deck | none detected | none-detected | ends event |
| RoomFullOfCheese | INITIAL.options.SEARCH | Search | Obtain the relic Chosen Cheese | Lose 14 HP; event is marked lethal at 14 current HP | lethal-possible | ends event |
| RoundTeaParty | INITIAL.options.ENJOY_TEA | EnjoyTea | Obtain the relic Royal Poison; Heal ? HP | none detected | none-detected | ends event |
| RoundTeaParty | INITIAL.options.PICK_FIGHT | PickFight | none detected | none detected | none-detected | next page |
| RoundTeaParty | PICK_FIGHT.options.CONTINUE_FIGHT | ContinueFight | Obtain a relic | Lose 11 HP | harmful | ends event |
| SapphireSeed | INITIAL.options.EAT | Eat | Heal 9 HP; Upgrade a card in your deck; Upgrade a card | none detected | none-detected | ends event |
| SapphireSeed | INITIAL.options.PLANT | Plant | Enchant a card in your deck; Enchant a card with Sown | none detected | none-detected | ends event |
| SelfHelpBook | INITIAL.options.READ_THE_BACK | ReadTheBack | none detected | none detected | none-detected | unknown (handler not resolvable) |
| SelfHelpBook | INITIAL.options.READ_THE_BACK_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| SelfHelpBook | INITIAL.options.READ_PASSAGE | ReadPassage | none detected | none detected | none-detected | unknown (handler not resolvable) |
| SelfHelpBook | INITIAL.options.READ_PASSAGE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| SelfHelpBook | INITIAL.options.READ_ENTIRE_BOOK | ReadEntireBook | none detected | none detected | none-detected | unknown (handler not resolvable) |
| SelfHelpBook | INITIAL.options.READ_ENTIRE_BOOK_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| SelfHelpBook | INITIAL.options.NO_OPTIONS | SkipBook | none detected | none detected | none-detected | ends event |
| SlipperyBridge | INITIAL.options.OVERCOME | Overcome | none detected | Remove a card from your deck | costly | ends event |
| SlipperyBridge | INITIAL.options.HOLD_ON_0 | HoldOn | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| SlipperyBridge | unknown | HoldOn | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| SpiralingWhirlpool | INITIAL.options.OBSERVE | ObserveTheSpiral | Enchant a card in your deck; Enchant a card with Spiral | none detected | none-detected | ends event |
| SpiralingWhirlpool | INITIAL.options.DRINK | Drink | Heal 0 HP | none detected | none-detected | ends event |
| SpiritGrafter | INITIAL.options.LET_IT_IN | StepInside | Heal 25 HP; Put a card into your deck | none detected | none-detected | ends event |
| SpiritGrafter | INITIAL.options.REJECTION | StickArmIn | none detected | Remove a card from your deck; Lose 9 HP; event is marked lethal at 9 current HP | lethal-possible | ends event |
| StoneOfAllTime | INITIAL.options.LIFT | Lift | Gain 10 Max HP | Discard a potion | harmful | ends event |
| StoneOfAllTime | INITIAL.options.LIFT_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| StoneOfAllTime | INITIAL.options.PUSH_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| StoneOfAllTime | INITIAL.options.PUSH | Push | Enchant a card in your deck; Enchant a card | Lose 6 HP; event is marked lethal at 6 current HP | lethal-possible | ends event |
| SunkenStatue | INITIAL.options.GRAB_SWORD | GrabSword | Obtain the relic Sword Of Stone | none detected | none-detected | ends event |
| SunkenStatue | INITIAL.options.DIVE_INTO_WATER | DiveIntoWater | Gain 111 Gold | Lose 7 HP; event is marked lethal at 7 current HP | lethal-possible | ends event |
| SunkenTreasury | INITIAL.options.FIRST_CHEST | FirstChest | Gain 60 Gold | none detected | none-detected | ends event |
| SunkenTreasury | INITIAL.options.SECOND_CHEST | SecondChest | Gain 333 Gold | Add the curse Greed to your deck | harmful | ends event |
| Symbiote | INITIAL.options.APPROACH_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| Symbiote | INITIAL.options.APPROACH | Approach | Enchant a card in your deck; Enchant a card with Corrupted | none detected | none-detected | ends event |
| Symbiote | INITIAL.options.KILL_WITH_FIRE | KillWithFire | Transform a card in your deck; Transform a card into a random card | none detected | none-detected | ends event |
| TabletOfTruth | INITIAL.options.DECIPHER_1 | Decipher | none detected | none detected | lethal-possible | ends event |
| TabletOfTruth | INITIAL.options.SMASH | Smash | Heal 20 HP | none detected | none-detected | ends event |
| TabletOfTruth | DECIPHER_*.options.DECIPHER | Decipher | none detected | none detected | lethal-possible | repeats this option (pages) |
| TabletOfTruth | DECIPHER.options.GIVE_UP | GiveUp | none detected | none detected | none-detected | ends event |
| Tanx | INITIAL.options.CLAWS | RelicOption<Claws> | Obtain the relic Claws; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.CROSSBOW | RelicOption<Crossbow> | Obtain the relic Crossbow; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.IRON_CLUB | RelicOption<IronClub> | Obtain the relic Iron Club; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.MEAT_CLEAVER | RelicOption<MeatCleaver> | Obtain the relic Meat Cleaver; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.SAI | RelicOption<Sai> | Obtain the relic Sai; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.SPIKED_GAUNTLETS | RelicOption<SpikedGauntlets> | Obtain the relic Spiked Gauntlets; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.TANXS_WHISTLE | RelicOption<TanxsWhistle> | Obtain the relic Tanxs Whistle; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.THROWING_AXE | RelicOption<ThrowingAxe> | Obtain the relic Throwing Axe; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.WAR_HAMMER | RelicOption<WarHammer> | Obtain the relic War Hammer; the source finishes the event after it | none detected | none-detected | ends event |
| Tanx | INITIAL.options.TRI_BOOMERANG | RelicOption<TriBoomerang> | Obtain the relic Tri Boomerang; the source finishes the event after it | none detected | none-detected | ends event |
| TeaMaster | INITIAL.options.BONE_TEA | BoneTea | Obtain the relic Bone Tea | Lose 50 Gold | costly | ends event |
| TeaMaster | INITIAL.options.BONE_TEA_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| TeaMaster | INITIAL.options.EMBER_TEA | EmberTea | Obtain the relic Ember Tea | Lose 150 Gold | costly | ends event |
| TeaMaster | INITIAL.options.EMBER_TEA_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| TeaMaster | INITIAL.options.TEA_OF_DISCOURTESY | TeaOfDiscourtesy | Obtain the relic Tea Of Discourtesy | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.NUTRITIOUS_SOUP | RelicOption<NutritiousSoup> | Obtain the relic Nutritious Soup; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.VERY_HOT_COCOA | RelicOption<VeryHotCocoa> | Obtain the relic Very Hot Cocoa; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.YUMMY_COOKIE | RelicOption<YummyCookie> | Obtain the relic Yummy Cookie; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.BIIIG_HUG | RelicOption<BiiigHug> | Obtain the relic Biiig Hug; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.STORYBOOK | RelicOption<Storybook> | Obtain the relic Storybook; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.SEAL_OF_GOLD | RelicOption<SealOfGold> | Obtain the relic Seal Of Gold; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.TOASTY_MITTENS | RelicOption<ToastyMittens> | Obtain the relic Toasty Mittens; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.GOLDEN_COMPASS | RelicOption<GoldenCompass> | Obtain the relic Golden Compass; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.PUMPKIN_CANDLE | RelicOption<PumpkinCandle> | Obtain the relic Pumpkin Candle; the source finishes the event after it | none detected | none-detected | ends event |
| Tezcatara | INITIAL.options.TOY_BOX | RelicOption<ToyBox> | Obtain the relic Toy Box; the source finishes the event after it | none detected | none-detected | ends event |
| TheArchitect | unknown | (inline) | none detected | none detected | none-detected | unknown (handler not resolvable) |
| TheArchitect | PROCEED | WinRun | none detected | none detected | none-detected | unknown (handler not resolvable) |
| TheFutureOfPotions | unknown | (inline) | none detected | none detected | none-detected | unknown (handler not resolvable) |
| TheLanternKey | INITIAL.options.RETURN_THE_KEY | ReturnTheKey | Gain 100 Gold | none detected | none-detected | repeats this option (pages) |
| TheLanternKey | INITIAL.options.KEEP_THE_KEY | KeepTheKey | none detected | none detected | none-detected | next page |
| TheLanternKey | KEEP_THE_KEY.options.FIGHT | Fight | none detected | none detected | none-detected | unknown (handler not resolvable) |
| TheLegendsWereTrue | INITIAL.options.NAB_THE_MAP | NabTheMap | Put a card into your deck | none detected | none-detected | ends event |
| TheLegendsWereTrue | INITIAL.options.SLOWLY_FIND_AN_EXIT | SlowlyFindAnExit | Offer extra rewards | Lose 8 HP; event is marked lethal at 8 current HP | lethal-possible | ends event |
| ThisOrThat | INITIAL.options.PLAIN | Plain | Gain 0 Gold | Lose 6 HP; event is marked lethal at 6 current HP | lethal-possible | ends event |
| ThisOrThat | INITIAL.options.ORNATE | Ornate | Obtain a random relic; Obtain a relic | Add the curse Clumsy to your deck | harmful | ends event |
| TinkerTime | INITIAL.options.CHOOSE_CARD_TYPE | ChooseCardType | none detected | none detected | none-detected | next page |
| TinkerTime | CHOOSE_CARD_TYPE.options.ATTACK | Attack | none detected | none detected | none-detected | next page |
| TinkerTime | CHOOSE_CARD_TYPE.options.SKILL | Skill | none detected | none detected | none-detected | next page |
| TinkerTime | CHOOSE_CARD_TYPE.options.POWER | Power | none detected | none detected | none-detected | next page |
| TinkerTime | unknown | (inline) | Put a card into your deck | none detected | none-detected | ends event |
| TrashHeap | INITIAL.options.DIVE_IN | DiveIn | Obtain a relic | Lose 8 HP; event is marked lethal at 8 current HP | lethal-possible | ends event |
| TrashHeap | INITIAL.options.GRAB | Grab | Gain 100 Gold; Put a card into your deck | none detected | none-detected | ends event |
| Trial | INITIAL.options.ACCEPT | Accept | none detected | none detected | none-detected | next page |
| Trial | INITIAL.options.REJECT | Reject | none detected | none detected | none-detected | next page |
| Trial | MERCHANT.options.GUILTY | MerchantGuilty | Obtain a relic | Add the curse Regret to your deck | harmful | unknown (handler not resolvable) |
| Trial | MERCHANT.options.INNOCENT | MerchantInnocent | Upgrade a card in your deck; Upgrade a card | Add the curse Shame to your deck | harmful | unknown (handler not resolvable) |
| Trial | NOBLE.options.GUILTY | NobleGuilty | Heal 10 HP | none detected | none-detected | unknown (handler not resolvable) |
| Trial | NOBLE.options.INNOCENT | NobleInnocent | Gain 300 Gold | Add the curse Regret to your deck | harmful | unknown (handler not resolvable) |
| Trial | NONDESCRIPT.options.GUILTY | NondescriptGuilty | Offer extra rewards | Add the curse Doubt to your deck | harmful | unknown (handler not resolvable) |
| Trial | NONDESCRIPT.options.INNOCENT | NondescriptInnocent | Transform a card in your deck; Transform a card into a random card | Add the curse Doubt to your deck | harmful | unknown (handler not resolvable) |
| Trial | REJECT.options.ACCEPT | Accept | none detected | none detected | none-detected | next page |
| Trial | REJECT.options.DOUBLE_DOWN | DoubleDown | none detected | event is marked lethal at 9999 current HP | lethal-possible | unknown (handler not resolvable) |
| UnrestSite | INITIAL.options.REST | Rest | Heal 0 HP | Add curses to your deck | harmful | ends event |
| UnrestSite | INITIAL.options.KILL | Kill | Obtain a random relic; Obtain a relic | Lose 8 Max HP | harmful | ends event |
| Vakuu | INITIAL.options.BLOOD_SOAKED_ROSE | RelicOption<BloodSoakedRose> | Obtain the relic Blood Soaked Rose; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.WHISPERING_EARRING | RelicOption<WhisperingEarring> | Obtain the relic Whispering Earring; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.FIDDLE | RelicOption<Fiddle> | Obtain the relic Fiddle; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.PRESERVED_FOG | RelicOption<PreservedFog> | Obtain the relic Preserved Fog; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.SERE_TALON | RelicOption<SereTalon> | Obtain the relic Sere Talon; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.DISTINGUISHED_CAPE | RelicOption<DistinguishedCape> | Obtain the relic Distinguished Cape; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.CHOICES_PARADOX | RelicOption<ChoicesParadox> | Obtain the relic Choices Paradox; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.MUSIC_BOX | RelicOption<MusicBox> | Obtain the relic Music Box; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.LORDS_PARASOL | RelicOption<LordsParasol> | Obtain the relic Lords Parasol; the source finishes the event after it | none detected | none-detected | ends event |
| Vakuu | INITIAL.options.JEWELED_MASK | RelicOption<JeweledMask> | Obtain the relic Jeweled Mask; the source finishes the event after it | none detected | none-detected | ends event |
| WarHistorianRepy | INITIAL.options.UNLOCK_CAGE | UnlockCage | Obtain the relic History Course; complete Quest | Remove a card from your deck | costly | ends event |
| WarHistorianRepy | INITIAL.options.UNLOCK_CHEST | UnlockChest | Offer extra rewards; complete Quest | Remove a card from your deck | costly | ends event |
| WaterloggedScriptorium | INITIAL.options.BLOODY_INK | BloodyInk | Gain 6 Max HP | none detected | none-detected | ends event |
| WaterloggedScriptorium | INITIAL.options.TENTACLE_QUILL | TentacleQuill | Enchant a card in your deck; Enchant a card with Steady | Lose 65 Gold | costly | ends event |
| WaterloggedScriptorium | INITIAL.options.TENTACLE_QUILL_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WaterloggedScriptorium | INITIAL.options.PRICKLY_SPONGE | PricklySponge | Enchant a card in your deck; Enchant a card with Steady | Lose 155 Gold | costly | ends event |
| WaterloggedScriptorium | INITIAL.options.PRICKLY_SPONGE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WelcomeToWongos | INITIAL.options.BARGAIN_BIN | BuyBargainBin | Obtain a random relic; Obtain a relic | Lose 100 Gold | costly | ends event |
| WelcomeToWongos | INITIAL.options.BARGAIN_BIN_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WelcomeToWongos | INITIAL.options.FEATURED_ITEM | BuyFeaturedItem | Obtain a relic | Lose 200 Gold | costly | ends event |
| WelcomeToWongos | INITIAL.options.FEATURED_ITEM_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WelcomeToWongos | INITIAL.options.MYSTERY_BOX | BuyMysteryBox | Obtain the relic Wongos Mystery Ticket | Lose 300 Gold | costly | ends event |
| WelcomeToWongos | INITIAL.options.MYSTERY_BOX_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WelcomeToWongos | INITIAL.options.LEAVE | Leave | none detected | Downgrade a card | costly | ends event |
| Wellspring | INITIAL.options.BOTTLE | Bottle | Offer extra rewards | none detected | none-detected | ends event |
| Wellspring | INITIAL.options.BATHE | Bathe | none detected | Remove a card from your deck | costly | ends event |
| WhisperingHollow | INITIAL.options.GOLD | Gold | Offer extra rewards | Lose 50 Gold | costly | ends event |
| WhisperingHollow | INITIAL.options.HUG | Hug | Transform a card in your deck; Transform a card into a random card | Lose 9 HP; event is marked lethal at 9 current HP | lethal-possible | ends event |
| WoodCarvings | INITIAL.options.SNAKE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WoodCarvings | INITIAL.options.SNAKE | Snake | Enchant a card in your deck; Enchant a card with Slither | none detected | none-detected | ends event |
| WoodCarvings | INITIAL.options.BIRD | Bird | Choose a card in your deck; Transform a card into Peck | none detected | none-detected | ends event |
| WoodCarvings | INITIAL.options.TORUS | Torus | Choose a card in your deck; Transform a card into Toric Toughness | none detected | none-detected | ends event |
| ZenWeaver | INITIAL.options.BREATHING_TECHNIQUES | BreathingTechniques | Put a card into your deck | Lose 50 Gold | costly | ends event |
| ZenWeaver | INITIAL.options.EMOTIONAL_AWARENESS | EmotionalAwareness | none detected | none detected | none-detected | ends event |
| ZenWeaver | INITIAL.options.ARACHNID_ACUPUNCTURE | ArachnidAcupuncture | none detected | none detected | none-detected | ends event |
| ZenWeaver | INITIAL.options.LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
