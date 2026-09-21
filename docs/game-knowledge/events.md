# Event Index

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-09-22 00:28:59 +08:00

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

Every distinct option the event builds, in source order.

`Event` is the canonical `event_id` the live payload reports -- the class name through the same
slug algorithm the game's `StringHelper.Slugify` applies (`Neow` -> `NEOW`,
`DoorsOfLightAndDark` -> `DOORS_OF_LIGHT_AND_DARK`) -- so a live `event.event_id` joins this
column without a case-sensitive comparison against a class name. `Option` is the option's
localization key without the leading `<EVENT>.pages.` segment: the game builds the full key as
`Slugify(EventTypeName).pages.<PAGE>.options.<OPTION>`, so a live `event.options[].text_key` of
`NEOW.pages.INITIAL.options.ARCANE_SCROLL` is this table's `NEOW` row with the option
`INITIAL.options.ARCANE_SCROLL`. Options an Ancient creates through `RelicOption<T>` are labelled
after that helper instead of a literal key, and their option name is the relic's own id entry.

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
| ABYSSAL_BATHS | INITIAL.options.IMMERSE | Immerse | Gain 2 Max HP | Lose 3 HP; event is marked lethal at ? current HP | lethal-possible | next page |
| ABYSSAL_BATHS | INITIAL.options.ABSTAIN | Abstain | Heal 10 HP | none detected | none-detected | ends event |
| ABYSSAL_BATHS | ALL.options.LINGER | Linger | Gain 2 Max HP | Lose 3 HP; event is marked lethal at ? current HP | lethal-possible | repeats this option (pages) |
| ABYSSAL_BATHS | ALL.options.EXIT_BATHS | ExitBaths | none detected | none detected | none-detected | ends event |
| AMALGAMATOR | INITIAL.options.COMBINE_STRIKES | CombineStrikes | Put a card into your deck | Remove a card from your deck | costly | ends event |
| AMALGAMATOR | INITIAL.options.COMBINE_DEFENDS | CombineDefends | Put a card into your deck | Remove a card from your deck | costly | ends event |
| AROMA_OF_CHAOS | INITIAL.options.LET_GO | LetGo | Transform a card in your deck; Transform a card into a random card | none detected | none-detected | ends event |
| AROMA_OF_CHAOS | INITIAL.options.MAINTAIN_CONTROL | MaintainControl | Upgrade a card in your deck; Upgrade a card | none detected | none-detected | ends event |
| BATTLEWORN_DUMMY | INITIAL.options.SETTING_1 | Setting1 | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BATTLEWORN_DUMMY | INITIAL.options.SETTING_2 | Setting2 | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BATTLEWORN_DUMMY | INITIAL.options.SETTING_3 | Setting3 | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BRAIN_LEECH | INITIAL.options.SHARE_KNOWLEDGE | ShareKnowledge | Offer a card reward; Choose a card from an offered set; Put a card into your deck | none detected | none-detected | ends event |
| BRAIN_LEECH | INITIAL.options.RIP | Rip | Offer extra rewards | Lose ? HP; event is marked lethal at 5 current HP | lethal-possible | ends event |
| BUGSLAYER | INITIAL.options.EXTERMINATION | Extermination | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BUGSLAYER | INITIAL.options.SQUASH | Squash | none detected | none detected | none-detected | unknown (handler not resolvable) |
| BYRDONIS_NEST | INITIAL.options.EAT | Eat | Gain 7 Max HP | none detected | none-detected | ends event |
| BYRDONIS_NEST | INITIAL.options.TAKE | Take | Put a card into your deck | none detected | none-detected | ends event |
| COLORFUL_PHILOSOPHERS | unknown | (inline) | Offer extra rewards | none detected | none-detected | ends event |
| COLOSSAL_FLOWER | INITIAL.options.EXTRACT_CURRENT_PRIZE_* | ExtractCurrentPrize | Gain ? Gold | none detected | none-detected | ends event |
| COLOSSAL_FLOWER | INITIAL.options.REACH_DEEPER_* | ReachDeeper | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| COLOSSAL_FLOWER | REACH_DEEPER_*.options.EXTRACT_CURRENT_PRIZE_* | ExtractCurrentPrize | Gain ? Gold | none detected | none-detected | ends event |
| COLOSSAL_FLOWER | REACH_DEEPER_*.options.REACH_DEEPER_* | ReachDeeper | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| COLOSSAL_FLOWER | REACH_DEEPER_2.options.EXTRACT_INSTEAD | ExtractInstead | Gain ? Gold | none detected | none-detected | ends event |
| COLOSSAL_FLOWER | REACH_DEEPER_2.options.POLLINOUS_CORE | ObtainPollinousCore | Obtain the relic Pollinous Core | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | ends event |
| CRYSTAL_SPHERE | INITIAL.options.UNCOVER_FUTURE | UncoverFuture | none detected | Lose 50 Gold | costly | ends event |
| CRYSTAL_SPHERE | INITIAL.options.PAYMENT_PLAN | PaymentPlan | none detected | Add the curse Debt to your deck | harmful | ends event |
| DARV | INITIAL.options.DUSTY_TOME | RelicOption<DustyTome> | Obtain the relic Dusty Tome; the source finishes the event after it | none detected | none-detected | ends event |
| DENSE_VEGETATION | INITIAL.options.TRUDGE_ON | TrudgeOn | none detected | Remove a card from your deck; Lose 11 HP; event is marked lethal at 11 current HP | lethal-possible | ends event |
| DENSE_VEGETATION | INITIAL.options.REST | Rest | Heal as if you had rested | none detected | none-detected | next page |
| DENSE_VEGETATION | REST.options.FIGHT | Fight | none detected | none detected | none-detected | unknown (handler not resolvable) |
| DOLL_ROOM | INITIAL.options.RANDOM | ChooseRandom | none detected | none detected | none-detected | unknown (handler not resolvable) |
| DOLL_ROOM | INITIAL.options.TAKE_SOME_TIME | TakeSomeTime | none detected | Lose ? HP; event is marked lethal at 5 current HP | lethal-possible | next page |
| DOLL_ROOM | INITIAL.options.EXAMINE | Examine | none detected | Lose ? HP; event is marked lethal at 15 current HP | lethal-possible | next page |
| DOLL_ROOM | unknown | Func | none detected | none detected | none-detected | unknown (handler not resolvable) |
| DOORS_OF_LIGHT_AND_DARK | INITIAL.options.LIGHT | Light | Upgrade a card | none detected | none-detected | ends event |
| DOORS_OF_LIGHT_AND_DARK | INITIAL.options.DARK | Dark | none detected | Remove a card from your deck | costly | ends event |
| DROWNING_BEACON | INITIAL.options.BOTTLE | BottleOption | Offer extra rewards | none detected | none-detected | ends event |
| DROWNING_BEACON | INITIAL.options.CLIMB | ClimbOption | Obtain the relic Fresnel Lens | Lose 13 Max HP | harmful | ends event |
| ENDLESS_CONVEYOR | INITIAL.options.OBSERVE_CHEF | ObserveChef | Upgrade a card | none detected | none-detected | ends event |
| ENDLESS_CONVEYOR | GRAB_SOMETHING_OFF_THE_BELT.options.LEAVE | Leave | none detected | none detected | none-detected | ends event |
| ENDLESS_CONVEYOR | unknown | GrabSomethingOffTheBelt | none detected | Lose 35 Gold | costly | next page |
| ENDLESS_CONVEYOR | ALL.options.LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| FIELD_OF_MAN_SIZED_HOLES | INITIAL.options.RESIST | Resist | none detected | Remove a card from your deck; Add curses to your deck | harmful | ends event |
| FIELD_OF_MAN_SIZED_HOLES | INITIAL.options.ENTER_YOUR_HOLE | EnterYourHole | Enchant a card in your deck; Enchant a card with Perfect Fit | none detected | none-detected | ends event |
| GRAVE_OF_THE_FORGOTTEN | INITIAL.options.CONFRONT_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| GRAVE_OF_THE_FORGOTTEN | INITIAL.options.CONFRONT | Confront | Enchant a card in your deck; Enchant a card with Souls Power | Add the curse Decay to your deck | harmful | ends event |
| GRAVE_OF_THE_FORGOTTEN | INITIAL.options.ACCEPT | Accept | Obtain the relic Forgotten Soul | none detected | none-detected | ends event |
| HUNGRY_FOR_MUSHROOMS | INITIAL.options.BIG_MUSHROOM | RelicOption<BigMushroom> | Obtain the relic Big Mushroom; the source finishes the event after it | none detected | none-detected | ends event |
| HUNGRY_FOR_MUSHROOMS | INITIAL.options.FRAGRANT_MUSHROOM | RelicOption<FragrantMushroom> | Obtain the relic Fragrant Mushroom; the source finishes the event after it | none detected | none-detected | ends event |
| INFESTED_AUTOMATON | INITIAL.options.STUDY | Study | Offer a card reward; Put a card into your deck | none detected | none-detected | ends event |
| INFESTED_AUTOMATON | INITIAL.options.TOUCH_CORE | TouchCore | Offer a card reward; Put a card into your deck | none detected | none-detected | ends event |
| JUNGLE_MAZE_ADVENTURE | INITIAL.options.SOLO_QUEST | DontNeedHelp | Gain 150 Gold | Lose 18 HP; event is marked lethal at 18 current HP | lethal-possible | ends event |
| JUNGLE_MAZE_ADVENTURE | INITIAL.options.JOIN_FORCES | SafetyInNumbers | Gain 50 Gold | none detected | none-detected | ends event |
| LOST_WISP | INITIAL.options.CLAIM | onChosen | none detected | none detected | none-detected | unknown (handler not resolvable) |
| LOST_WISP | INITIAL.options.SEARCH | Search | Gain 60 Gold | none detected | none-detected | ends event |
| LUMINOUS_CHOIR | INITIAL.options.REACH_INTO_THE_FLESH | ReachIntoTheFlesh | none detected | Remove a card from your deck; Add the curse Spore Mind to your deck | harmful | ends event |
| LUMINOUS_CHOIR | INITIAL.options.OFFER_TRIBUTE | OfferTribute | Obtain a random relic; Obtain a relic | Lose 149 Gold | costly | ends event |
| LUMINOUS_CHOIR | INITIAL.options.OFFER_TRIBUTE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| MORPHIC_GROVE | INITIAL.options.GROUP | Group | Transform a card in your deck; Transform a card into a random card | Lose 100 Gold | costly | ends event |
| MORPHIC_GROVE | INITIAL.options.LONER | Loner | Gain 5 Max HP | none detected | none-detected | ends event |
| NEOW | unknown | (inline) | none detected | none detected | none-detected | ends event |
| NEOW | INITIAL.options.ARCANE_SCROLL | RelicOption<ArcaneScroll> | Obtain the relic Arcane Scroll; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.BOOMING_CONCH | RelicOption<BoomingConch> | Obtain the relic Booming Conch; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.POMANDER | RelicOption<Pomander> | Obtain the relic Pomander; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.GOLDEN_PEARL | RelicOption<GoldenPearl> | Obtain the relic Golden Pearl; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.LEAD_PAPERWEIGHT | RelicOption<LeadPaperweight> | Obtain the relic Lead Paperweight; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.NEW_LEAF | RelicOption<NewLeaf> | Obtain the relic New Leaf; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.NEOWS_TORMENT | RelicOption<NeowsTorment> | Obtain the relic Neows Torment; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.PRECISE_SCISSORS | RelicOption<PreciseScissors> | Obtain the relic Precise Scissors; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.LOST_COFFER | RelicOption<LostCoffer> | Obtain the relic Lost Coffer; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.NUTRITIOUS_OYSTER | RelicOption<NutritiousOyster> | Obtain the relic Nutritious Oyster; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.STONE_HUMIDIFIER | RelicOption<StoneHumidifier> | Obtain the relic Stone Humidifier; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.MASSIVE_SCROLL | RelicOption<MassiveScroll> | Obtain the relic Massive Scroll; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.LAVA_ROCK | RelicOption<LavaRock> | Obtain the relic Lava Rock; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.SMALL_CAPSULE | RelicOption<SmallCapsule> | Obtain the relic Small Capsule; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.SILVER_CRUCIBLE | RelicOption<SilverCrucible> | Obtain the relic Silver Crucible; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.CURSED_PEARL | RelicOption<CursedPearl> | Obtain the relic Cursed Pearl; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.LARGE_CAPSULE | RelicOption<LargeCapsule> | Obtain the relic Large Capsule; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.LEAFY_POULTICE | RelicOption<LeafyPoultice> | Obtain the relic Leafy Poultice; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.PRECARIOUS_SHEARS | RelicOption<PrecariousShears> | Obtain the relic Precarious Shears; the source finishes the event after it | none detected | none-detected | ends event |
| NEOW | INITIAL.options.SCROLL_BOXES | RelicOption<ScrollBoxes> | Obtain the relic Scroll Boxes; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.BLESSED_ANTLER | RelicOption<BlessedAntler> | Obtain the relic Blessed Antler; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.BRILLIANT_SCARF | RelicOption<BrilliantScarf> | Obtain the relic Brilliant Scarf; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.DELICATE_FROND | RelicOption<DelicateFrond> | Obtain the relic Delicate Frond; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.DIAMOND_DIADEM | RelicOption<DiamondDiadem> | Obtain the relic Diamond Diadem; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.FUR_COAT | RelicOption<FurCoat> | Obtain the relic Fur Coat; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.GLITTER | RelicOption<Glitter> | Obtain the relic Glitter; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.JEWELRY_BOX | RelicOption<JewelryBox> | Obtain the relic Jewelry Box; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.LOOMING_FRUIT | RelicOption<LoomingFruit> | Obtain the relic Looming Fruit; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.SIGNET_RING | RelicOption<SignetRing> | Obtain the relic Signet Ring; the source finishes the event after it | none detected | none-detected | ends event |
| NONUPEIPE | INITIAL.options.BEAUTIFUL_BRACELET | RelicOption<BeautifulBracelet> | Obtain the relic Beautiful Bracelet; the source finishes the event after it | none detected | none-detected | ends event |
| OROBAS | INITIAL.options.OPTION_POOL_3_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| OROBAS | INITIAL.options.ELECTRIC_SHRYMP | RelicOption<ElectricShrymp> | Obtain the relic Electric Shrymp; the source finishes the event after it | none detected | none-detected | ends event |
| OROBAS | INITIAL.options.GLASS_EYE | RelicOption<GlassEye> | Obtain the relic Glass Eye; the source finishes the event after it | none detected | none-detected | ends event |
| OROBAS | INITIAL.options.SAND_CASTLE | RelicOption<SandCastle> | Obtain the relic Sand Castle; the source finishes the event after it | none detected | none-detected | ends event |
| OROBAS | INITIAL.options.ALCHEMICAL_COFFER | RelicOption<AlchemicalCoffer> | Obtain the relic Alchemical Coffer; the source finishes the event after it | none detected | none-detected | ends event |
| OROBAS | INITIAL.options.DRIFTWOOD | RelicOption<Driftwood> | Obtain the relic Driftwood; the source finishes the event after it | none detected | none-detected | ends event |
| OROBAS | INITIAL.options.RADIANT_PEARL | RelicOption<RadiantPearl> | Obtain the relic Radiant Pearl; the source finishes the event after it | none detected | none-detected | ends event |
| OROBAS | INITIAL.options.PRISMATIC_GEM | RelicOption<PrismaticGem> | Obtain the relic Prismatic Gem; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_CLAW | RelicOption<PaelsClaw> | Obtain the relic Paels Claw; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_TOOTH | RelicOption<PaelsTooth> | Obtain the relic Paels Tooth; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_GROWTH | RelicOption<PaelsGrowth> | Obtain the relic Paels Growth; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_LEGION | RelicOption<PaelsLegion> | Obtain the relic Paels Legion; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_FLESH | RelicOption<PaelsFlesh> | Obtain the relic Paels Flesh; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_HORN | RelicOption<PaelsHorn> | Obtain the relic Paels Horn; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_TEARS | RelicOption<PaelsTears> | Obtain the relic Paels Tears; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_WING | RelicOption<PaelsWing> | Obtain the relic Paels Wing; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_EYE | RelicOption<PaelsEye> | Obtain the relic Paels Eye; the source finishes the event after it | none detected | none-detected | ends event |
| PAEL | INITIAL.options.PAELS_BLOOD | RelicOption<PaelsBlood> | Obtain the relic Paels Blood; the source finishes the event after it | none detected | none-detected | ends event |
| POTION_COURIER | INITIAL.options.GRAB_POTIONS | GrabPotions | Offer extra rewards | none detected | none-detected | ends event |
| POTION_COURIER | INITIAL.options.RANSACK | Ransack | Offer extra rewards | none detected | none-detected | ends event |
| PUNCH_OFF | INITIAL.options.NAB | Nab | Offer extra rewards | Add the curse Injury to your deck | harmful | ends event |
| PUNCH_OFF | INITIAL.options.I_CAN_TAKE_THEM | TakeThem | none detected | none detected | none-detected | next page |
| PUNCH_OFF | I_CAN_TAKE_THEM.options.FIGHT | Fight | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RANWID_THE_ELDER | unknown | (inline) | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RANWID_THE_ELDER | INITIAL.options.POTION_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| RANWID_THE_ELDER | INITIAL.options.GOLD | GiveGold | Obtain a random relic; Obtain a relic | Lose 100 Gold | costly | ends event |
| RANWID_THE_ELDER | INITIAL.options.RELIC_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| REFLECTIONS | INITIAL.options.TOUCH_A_MIRROR | TouchAMirror | Upgrade a card | Downgrade a card | costly | ends event |
| REFLECTIONS | INITIAL.options.SHATTER | Shatter | Put a card into your deck | Add the curse Bad Luck to your deck | harmful | ends event |
| RELIC_TRADER | INITIAL.options.TOP | Top | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RELIC_TRADER | INITIAL.options.MIDDLE | Middle | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RELIC_TRADER | INITIAL.options.BOTTOM | Bottom | none detected | none detected | none-detected | unknown (handler not resolvable) |
| RELIC_TRADER | PROCEED | Done | none detected | none detected | none-detected | ends event |
| ROOM_FULL_OF_CHEESE | INITIAL.options.GORGE | Gorge | Offer a card reward; Choose a card from an offered set; Put a card into your deck | none detected | none-detected | ends event |
| ROOM_FULL_OF_CHEESE | INITIAL.options.SEARCH | Search | Obtain the relic Chosen Cheese | Lose 14 HP; event is marked lethal at 14 current HP | lethal-possible | ends event |
| ROUND_TEA_PARTY | INITIAL.options.ENJOY_TEA | EnjoyTea | Obtain the relic Royal Poison; Heal ? HP | none detected | none-detected | ends event |
| ROUND_TEA_PARTY | INITIAL.options.PICK_FIGHT | PickFight | none detected | none detected | none-detected | next page |
| ROUND_TEA_PARTY | PICK_FIGHT.options.CONTINUE_FIGHT | ContinueFight | Obtain a relic | Lose 11 HP | harmful | ends event |
| SAPPHIRE_SEED | INITIAL.options.EAT | Eat | Heal 9 HP; Upgrade a card in your deck; Upgrade a card | none detected | none-detected | ends event |
| SAPPHIRE_SEED | INITIAL.options.PLANT | Plant | Enchant a card in your deck; Enchant a card with Sown | none detected | none-detected | ends event |
| SELF_HELP_BOOK | INITIAL.options.READ_THE_BACK | ReadTheBack | none detected | none detected | none-detected | unknown (handler not resolvable) |
| SELF_HELP_BOOK | INITIAL.options.READ_THE_BACK_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| SELF_HELP_BOOK | INITIAL.options.READ_PASSAGE | ReadPassage | none detected | none detected | none-detected | unknown (handler not resolvable) |
| SELF_HELP_BOOK | INITIAL.options.READ_PASSAGE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| SELF_HELP_BOOK | INITIAL.options.READ_ENTIRE_BOOK | ReadEntireBook | none detected | none detected | none-detected | unknown (handler not resolvable) |
| SELF_HELP_BOOK | INITIAL.options.READ_ENTIRE_BOOK_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| SELF_HELP_BOOK | INITIAL.options.NO_OPTIONS | SkipBook | none detected | none detected | none-detected | ends event |
| SLIPPERY_BRIDGE | INITIAL.options.OVERCOME | Overcome | none detected | Remove a card from your deck | costly | ends event |
| SLIPPERY_BRIDGE | INITIAL.options.HOLD_ON_0 | HoldOn | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| SLIPPERY_BRIDGE | unknown | HoldOn | none detected | Lose ? HP; event is marked lethal at ? current HP | lethal-possible | next page |
| SPIRALING_WHIRLPOOL | INITIAL.options.OBSERVE | ObserveTheSpiral | Enchant a card in your deck; Enchant a card with Spiral | none detected | none-detected | ends event |
| SPIRALING_WHIRLPOOL | INITIAL.options.DRINK | Drink | Heal 0 HP | none detected | none-detected | ends event |
| SPIRIT_GRAFTER | INITIAL.options.LET_IT_IN | StepInside | Heal 25 HP; Put a card into your deck | none detected | none-detected | ends event |
| SPIRIT_GRAFTER | INITIAL.options.REJECTION | StickArmIn | none detected | Remove a card from your deck; Lose 9 HP; event is marked lethal at 9 current HP | lethal-possible | ends event |
| STONE_OF_ALL_TIME | INITIAL.options.LIFT | Lift | Gain 10 Max HP | Discard a potion | harmful | ends event |
| STONE_OF_ALL_TIME | INITIAL.options.LIFT_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| STONE_OF_ALL_TIME | INITIAL.options.PUSH_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| STONE_OF_ALL_TIME | INITIAL.options.PUSH | Push | Enchant a card in your deck; Enchant a card | Lose 6 HP; event is marked lethal at 6 current HP | lethal-possible | ends event |
| SUNKEN_STATUE | INITIAL.options.GRAB_SWORD | GrabSword | Obtain the relic Sword Of Stone | none detected | none-detected | ends event |
| SUNKEN_STATUE | INITIAL.options.DIVE_INTO_WATER | DiveIntoWater | Gain 111 Gold | Lose 7 HP; event is marked lethal at 7 current HP | lethal-possible | ends event |
| SUNKEN_TREASURY | INITIAL.options.FIRST_CHEST | FirstChest | Gain 60 Gold | none detected | none-detected | ends event |
| SUNKEN_TREASURY | INITIAL.options.SECOND_CHEST | SecondChest | Gain 333 Gold | Add the curse Greed to your deck | harmful | ends event |
| SYMBIOTE | INITIAL.options.APPROACH_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| SYMBIOTE | INITIAL.options.APPROACH | Approach | Enchant a card in your deck; Enchant a card with Corrupted | none detected | none-detected | ends event |
| SYMBIOTE | INITIAL.options.KILL_WITH_FIRE | KillWithFire | Transform a card in your deck; Transform a card into a random card | none detected | none-detected | ends event |
| TABLET_OF_TRUTH | INITIAL.options.DECIPHER_1 | Decipher | none detected | none detected | lethal-possible | ends event |
| TABLET_OF_TRUTH | INITIAL.options.SMASH | Smash | Heal 20 HP | none detected | none-detected | ends event |
| TABLET_OF_TRUTH | DECIPHER_*.options.DECIPHER | Decipher | none detected | none detected | lethal-possible | repeats this option (pages) |
| TABLET_OF_TRUTH | DECIPHER.options.GIVE_UP | GiveUp | none detected | none detected | none-detected | ends event |
| TANX | INITIAL.options.CLAWS | RelicOption<Claws> | Obtain the relic Claws; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.CROSSBOW | RelicOption<Crossbow> | Obtain the relic Crossbow; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.IRON_CLUB | RelicOption<IronClub> | Obtain the relic Iron Club; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.MEAT_CLEAVER | RelicOption<MeatCleaver> | Obtain the relic Meat Cleaver; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.SAI | RelicOption<Sai> | Obtain the relic Sai; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.SPIKED_GAUNTLETS | RelicOption<SpikedGauntlets> | Obtain the relic Spiked Gauntlets; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.TANXS_WHISTLE | RelicOption<TanxsWhistle> | Obtain the relic Tanxs Whistle; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.THROWING_AXE | RelicOption<ThrowingAxe> | Obtain the relic Throwing Axe; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.WAR_HAMMER | RelicOption<WarHammer> | Obtain the relic War Hammer; the source finishes the event after it | none detected | none-detected | ends event |
| TANX | INITIAL.options.TRI_BOOMERANG | RelicOption<TriBoomerang> | Obtain the relic Tri Boomerang; the source finishes the event after it | none detected | none-detected | ends event |
| TEA_MASTER | INITIAL.options.BONE_TEA | BoneTea | Obtain the relic Bone Tea | Lose 50 Gold | costly | ends event |
| TEA_MASTER | INITIAL.options.BONE_TEA_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| TEA_MASTER | INITIAL.options.EMBER_TEA | EmberTea | Obtain the relic Ember Tea | Lose 150 Gold | costly | ends event |
| TEA_MASTER | INITIAL.options.EMBER_TEA_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| TEA_MASTER | INITIAL.options.TEA_OF_DISCOURTESY | TeaOfDiscourtesy | Obtain the relic Tea Of Discourtesy | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.NUTRITIOUS_SOUP | RelicOption<NutritiousSoup> | Obtain the relic Nutritious Soup; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.VERY_HOT_COCOA | RelicOption<VeryHotCocoa> | Obtain the relic Very Hot Cocoa; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.YUMMY_COOKIE | RelicOption<YummyCookie> | Obtain the relic Yummy Cookie; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.BIIIG_HUG | RelicOption<BiiigHug> | Obtain the relic Biiig Hug; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.STORYBOOK | RelicOption<Storybook> | Obtain the relic Storybook; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.SEAL_OF_GOLD | RelicOption<SealOfGold> | Obtain the relic Seal Of Gold; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.TOASTY_MITTENS | RelicOption<ToastyMittens> | Obtain the relic Toasty Mittens; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.GOLDEN_COMPASS | RelicOption<GoldenCompass> | Obtain the relic Golden Compass; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.PUMPKIN_CANDLE | RelicOption<PumpkinCandle> | Obtain the relic Pumpkin Candle; the source finishes the event after it | none detected | none-detected | ends event |
| TEZCATARA | INITIAL.options.TOY_BOX | RelicOption<ToyBox> | Obtain the relic Toy Box; the source finishes the event after it | none detected | none-detected | ends event |
| THE_ARCHITECT | unknown | (inline) | none detected | none detected | none-detected | unknown (handler not resolvable) |
| THE_ARCHITECT | PROCEED | WinRun | none detected | none detected | none-detected | unknown (handler not resolvable) |
| THE_FUTURE_OF_POTIONS | unknown | (inline) | none detected | none detected | none-detected | unknown (handler not resolvable) |
| THE_LANTERN_KEY | INITIAL.options.RETURN_THE_KEY | ReturnTheKey | Gain 100 Gold | none detected | none-detected | repeats this option (pages) |
| THE_LANTERN_KEY | INITIAL.options.KEEP_THE_KEY | KeepTheKey | none detected | none detected | none-detected | next page |
| THE_LANTERN_KEY | KEEP_THE_KEY.options.FIGHT | Fight | none detected | none detected | none-detected | unknown (handler not resolvable) |
| THE_LEGENDS_WERE_TRUE | INITIAL.options.NAB_THE_MAP | NabTheMap | Put a card into your deck | none detected | none-detected | ends event |
| THE_LEGENDS_WERE_TRUE | INITIAL.options.SLOWLY_FIND_AN_EXIT | SlowlyFindAnExit | Offer extra rewards | Lose 8 HP; event is marked lethal at 8 current HP | lethal-possible | ends event |
| THIS_OR_THAT | INITIAL.options.PLAIN | Plain | Gain 0 Gold | Lose 6 HP; event is marked lethal at 6 current HP | lethal-possible | ends event |
| THIS_OR_THAT | INITIAL.options.ORNATE | Ornate | Obtain a random relic; Obtain a relic | Add the curse Clumsy to your deck | harmful | ends event |
| TINKER_TIME | INITIAL.options.CHOOSE_CARD_TYPE | ChooseCardType | none detected | none detected | none-detected | next page |
| TINKER_TIME | CHOOSE_CARD_TYPE.options.ATTACK | Attack | none detected | none detected | none-detected | next page |
| TINKER_TIME | CHOOSE_CARD_TYPE.options.SKILL | Skill | none detected | none detected | none-detected | next page |
| TINKER_TIME | CHOOSE_CARD_TYPE.options.POWER | Power | none detected | none detected | none-detected | next page |
| TINKER_TIME | unknown | (inline) | Put a card into your deck | none detected | none-detected | ends event |
| TRASH_HEAP | INITIAL.options.DIVE_IN | DiveIn | Obtain a relic | Lose 8 HP; event is marked lethal at 8 current HP | lethal-possible | ends event |
| TRASH_HEAP | INITIAL.options.GRAB | Grab | Gain 100 Gold; Put a card into your deck | none detected | none-detected | ends event |
| TRIAL | INITIAL.options.ACCEPT | Accept | none detected | none detected | none-detected | next page |
| TRIAL | INITIAL.options.REJECT | Reject | none detected | none detected | none-detected | next page |
| TRIAL | MERCHANT.options.GUILTY | MerchantGuilty | Obtain a relic | Add the curse Regret to your deck | harmful | unknown (handler not resolvable) |
| TRIAL | MERCHANT.options.INNOCENT | MerchantInnocent | Upgrade a card in your deck; Upgrade a card | Add the curse Shame to your deck | harmful | unknown (handler not resolvable) |
| TRIAL | NOBLE.options.GUILTY | NobleGuilty | Heal 10 HP | none detected | none-detected | unknown (handler not resolvable) |
| TRIAL | NOBLE.options.INNOCENT | NobleInnocent | Gain 300 Gold | Add the curse Regret to your deck | harmful | unknown (handler not resolvable) |
| TRIAL | NONDESCRIPT.options.GUILTY | NondescriptGuilty | Offer extra rewards | Add the curse Doubt to your deck | harmful | unknown (handler not resolvable) |
| TRIAL | NONDESCRIPT.options.INNOCENT | NondescriptInnocent | Transform a card in your deck; Transform a card into a random card | Add the curse Doubt to your deck | harmful | unknown (handler not resolvable) |
| TRIAL | REJECT.options.ACCEPT | Accept | none detected | none detected | none-detected | next page |
| TRIAL | REJECT.options.DOUBLE_DOWN | DoubleDown | none detected | event is marked lethal at 9999 current HP | lethal-possible | unknown (handler not resolvable) |
| UNREST_SITE | INITIAL.options.REST | Rest | Heal 0 HP | Add curses to your deck | harmful | ends event |
| UNREST_SITE | INITIAL.options.KILL | Kill | Obtain a random relic; Obtain a relic | Lose 8 Max HP | harmful | ends event |
| VAKUU | INITIAL.options.BLOOD_SOAKED_ROSE | RelicOption<BloodSoakedRose> | Obtain the relic Blood Soaked Rose; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.WHISPERING_EARRING | RelicOption<WhisperingEarring> | Obtain the relic Whispering Earring; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.FIDDLE | RelicOption<Fiddle> | Obtain the relic Fiddle; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.PRESERVED_FOG | RelicOption<PreservedFog> | Obtain the relic Preserved Fog; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.SERE_TALON | RelicOption<SereTalon> | Obtain the relic Sere Talon; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.DISTINGUISHED_CAPE | RelicOption<DistinguishedCape> | Obtain the relic Distinguished Cape; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.CHOICES_PARADOX | RelicOption<ChoicesParadox> | Obtain the relic Choices Paradox; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.MUSIC_BOX | RelicOption<MusicBox> | Obtain the relic Music Box; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.LORDS_PARASOL | RelicOption<LordsParasol> | Obtain the relic Lords Parasol; the source finishes the event after it | none detected | none-detected | ends event |
| VAKUU | INITIAL.options.JEWELED_MASK | RelicOption<JeweledMask> | Obtain the relic Jeweled Mask; the source finishes the event after it | none detected | none-detected | ends event |
| WAR_HISTORIAN_REPY | INITIAL.options.UNLOCK_CAGE | UnlockCage | Obtain the relic History Course; complete Quest | Remove a card from your deck | costly | ends event |
| WAR_HISTORIAN_REPY | INITIAL.options.UNLOCK_CHEST | UnlockChest | Offer extra rewards; complete Quest | Remove a card from your deck | costly | ends event |
| WATERLOGGED_SCRIPTORIUM | INITIAL.options.BLOODY_INK | BloodyInk | Gain 6 Max HP | none detected | none-detected | ends event |
| WATERLOGGED_SCRIPTORIUM | INITIAL.options.TENTACLE_QUILL | TentacleQuill | Enchant a card in your deck; Enchant a card with Steady | Lose 65 Gold | costly | ends event |
| WATERLOGGED_SCRIPTORIUM | INITIAL.options.TENTACLE_QUILL_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WATERLOGGED_SCRIPTORIUM | INITIAL.options.PRICKLY_SPONGE | PricklySponge | Enchant a card in your deck; Enchant a card with Steady | Lose 155 Gold | costly | ends event |
| WATERLOGGED_SCRIPTORIUM | INITIAL.options.PRICKLY_SPONGE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WELCOME_TO_WONGOS | INITIAL.options.BARGAIN_BIN | BuyBargainBin | Obtain a random relic; Obtain a relic | Lose 100 Gold | costly | ends event |
| WELCOME_TO_WONGOS | INITIAL.options.BARGAIN_BIN_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WELCOME_TO_WONGOS | INITIAL.options.FEATURED_ITEM | BuyFeaturedItem | Obtain a relic | Lose 200 Gold | costly | ends event |
| WELCOME_TO_WONGOS | INITIAL.options.FEATURED_ITEM_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WELCOME_TO_WONGOS | INITIAL.options.MYSTERY_BOX | BuyMysteryBox | Obtain the relic Wongos Mystery Ticket | Lose 300 Gold | costly | ends event |
| WELCOME_TO_WONGOS | INITIAL.options.MYSTERY_BOX_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WELCOME_TO_WONGOS | INITIAL.options.LEAVE | Leave | none detected | Downgrade a card | costly | ends event |
| WELLSPRING | INITIAL.options.BOTTLE | Bottle | Offer extra rewards | none detected | none-detected | ends event |
| WELLSPRING | INITIAL.options.BATHE | Bathe | none detected | Remove a card from your deck | costly | ends event |
| WHISPERING_HOLLOW | INITIAL.options.GOLD | Gold | Offer extra rewards | Lose 50 Gold | costly | ends event |
| WHISPERING_HOLLOW | INITIAL.options.HUG | Hug | Transform a card in your deck; Transform a card into a random card | Lose 9 HP; event is marked lethal at 9 current HP | lethal-possible | ends event |
| WOOD_CARVINGS | INITIAL.options.SNAKE_LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
| WOOD_CARVINGS | INITIAL.options.SNAKE | Snake | Enchant a card in your deck; Enchant a card with Slither | none detected | none-detected | ends event |
| WOOD_CARVINGS | INITIAL.options.BIRD | Bird | Choose a card in your deck; Transform a card into Peck | none detected | none-detected | ends event |
| WOOD_CARVINGS | INITIAL.options.TORUS | Torus | Choose a card in your deck; Transform a card into Toric Toughness | none detected | none-detected | ends event |
| ZEN_WEAVER | INITIAL.options.BREATHING_TECHNIQUES | BreathingTechniques | Put a card into your deck | Lose 50 Gold | costly | ends event |
| ZEN_WEAVER | INITIAL.options.EMOTIONAL_AWARENESS | EmotionalAwareness | none detected | none detected | none-detected | ends event |
| ZEN_WEAVER | INITIAL.options.ARACHNID_ACUPUNCTURE | ArachnidAcupuncture | none detected | none detected | none-detected | ends event |
| ZEN_WEAVER | INITIAL.options.LOCKED | none | option is locked (no handler) | n/a | locked | cannot be chosen |
