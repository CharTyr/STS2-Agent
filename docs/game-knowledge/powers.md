# Power Index

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-09-21 22:31:54 +08:00

What a `power_id` plus a stack count actually does. `Type` is `PowerType` (Buff/Debuff) and `StackType` is `PowerStackType`: `Counter` means the stacks accumulate and the amount is meaningful, `Single` means the power is present or not, `None` means it does not stack at all, and two values separated by a slash mean the source picks between them at run time. `Hook` lists the combat hooks the class overrides, which is when it triggers; `Effect` says what each of those hooks does, prefixed with the hook name, with a hook inherited from a base class prefixed with that class instead. `Amount` inside an effect is the power's own stack count (`PowerModel.Amount`), not a fixed number, and `?` marks an amount that is only computed at run time. A power whose source declares no command in any hook shows `no hook effect detected in source` rather than an invented description.

| Name | Type | StackType | Hook | Effect |
| --- | --- | --- | --- | --- |
| AccelerantPower | Buff | Counter | passive | no hook effect detected in source |
| AccuracyPower | Buff | Counter | ModifyDamageAdditive | ModifyDamageAdditive: returns 0 or Amount |
| AdaptablePower | Buff | Single | AfterDeath, ShouldAllowHitting, ShouldStopCombatFromEnding, ShouldCreatureBeRemovedFromCombatAfterDeath, ShouldPowerBeRemovedAfterOwnerDeath | ShouldAllowHitting: returns true or !IsReviving; ShouldStopCombatFromEnding: returns true; ShouldCreatureBeRemovedFromCombatAfterDeath: returns true or false; ShouldPowerBeRemovedAfterOwnerDeath: returns false |
| AfterimagePower | Buff | Counter | BeforeCardPlayed, AfterCardPlayed | AfterCardPlayed: Give ? Block |
| AggressionPower | Buff | Counter | BeforeSideTurnStart | BeforeSideTurnStart: Put a card into your hand; BeforeSideTurnStart: Upgrade a card |
| AnticipatePower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryDexterityPower.BeforeApplied: Apply Amount Dexterity to the target; TemporaryDexterityPower.AfterPowerAmountChanged: Gain Amount Dexterity; TemporaryDexterityPower.AfterTurnEnd: Remove this power; TemporaryDexterityPower.AfterTurnEnd: Gain -Amount Dexterity |
| ArsenalPower | Buff | Counter | AfterCardPlayed | AfterCardPlayed: Gain Amount Strength |
| ArtifactPower | Buff | Counter | TryModifyPowerAmountReceived, AfterModifyingPowerAmountReceived | TryModifyPowerAmountReceived: returns false or true; AfterModifyingPowerAmountReceived: decrement |
| AsleepPower | Buff | Counter | AfterDamageReceived, BeforeTurnEndVeryEarly, AfterTurnEnd | AfterDamageReceived: Remove a power from the target; AfterDamageReceived: Stun the target; AfterDamageReceived: Remove this power; BeforeTurnEndVeryEarly: Remove a power from the target; AfterTurnEnd: decrement |
| AutomationPower | Buff | Counter | AfterCardDrawn | AfterCardDrawn: Gain Amount Energy |
| BackAttackLeftPower | Buff | Single | passive | no hook effect detected in source |
| BackAttackRightPower | Buff | Single | passive | no hook effect detected in source |
| BarricadePower | Buff | Single | AfterApplied, ShouldClearBlock | ShouldClearBlock: returns true or false |
| BattlewornDummyTimeLimitPower | Buff | Counter | AfterTurnEnd | AfterTurnEnd: decrement; AfterTurnEnd: escape |
| BeaconOfHopePower | Buff | Counter | AfterBlockGained | AfterBlockGained: Give ? Block |
| BiasedCognitionPower | Debuff | Counter | AfterSideTurnStart | AfterSideTurnStart: Gain -Amount Focus |
| BlackHolePower | Buff | Counter | AfterCardPlayed, AfterStarsGained | AfterCardPlayed: Deal Amount damage to the target; AfterStarsGained: Deal Amount damage to the target |
| BladeOfInkPower | Buff | Counter | AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Gain Amount Strength; AfterTurnEnd: Gain -0 Strength; AfterTurnEnd: Remove this power |
| BlockNextTurnPower | Buff | Counter | AfterBlockCleared | AfterBlockCleared: Give Amount Block; AfterBlockCleared: Remove this power |
| BlurPower | Buff | Counter | ShouldClearBlock, AfterPreventingBlockClear, AfterSideTurnStart | ShouldClearBlock: returns true or false; AfterSideTurnStart: decrement |
| BufferPower | Buff | Counter | ModifyHpLostAfterOstyLate, AfterModifyingHpLostAfterOsty | ModifyHpLostAfterOstyLate: returns amount or 0; AfterModifyingHpLostAfterOsty: decrement |
| BurrowedPower | Buff | Single | ShouldClearBlock, AfterBlockBroken, AfterRemoved | ShouldClearBlock: returns true or false; AfterBlockBroken: Stun the target; AfterBlockBroken: Remove Burrowed from the target; AfterRemoved: Lose all Block |
| BurstPower | Buff | Counter | ModifyCardPlayCount, AfterModifyingCardPlayCount, AfterTurnEnd | ModifyCardPlayCount: returns playCount or playCount + 1; AfterModifyingCardPlayCount: decrement; AfterTurnEnd: Remove this power |
| CalamityPower | Buff | Counter | BeforeCardPlayed, AfterCardPlayed | AfterCardPlayed: get For Combat; AfterCardPlayed: Add a generated card to your hand |
| CalcifyPower | Buff | Counter | ModifyDamageAdditive | ModifyDamageAdditive: returns 0 or Amount |
| CallOfTheVoidPower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: Give a card Ethereal; BeforeHandDraw: Add a generated card to your hand |
| ChainsOfBindingPower | Debuff | Counter | AfterCardDrawn, BeforeCardPlayed, ShouldPlay, BeforeTurnEnd | AfterCardDrawn: afflict And Preview Bound; ShouldPlay: returns true or !GetInternalData<Data>().boundCardPlayed; BeforeTurnEnd: clear Affliction |
| ChildOfTheStarsPower | Buff | Counter | AfterStarsSpent | AfterStarsSpent: Give ? Block |
| ClarityPower | Buff | Counter | ModifyHandDraw, AfterSideTurnStart | ModifyHandDraw: returns count or count + 1; AfterSideTurnStart: decrement |
| ColossusPower | Buff | Counter | ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or 0.5; AfterTurnEnd: tick Down Duration |
| ConfusedPower | Debuff | Single | AfterCardDrawn | AfterCardDrawn: returns TestEnergyCostOverride or base.Owner.Player.RunState.Rng.CombatEnergyCosts.NextInt(4) |
| ConquerorPower | Debuff | Counter | ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or 2; AfterTurnEnd: tick Down Duration |
| ConstrictPower | Debuff | Counter | AfterTurnEnd, AfterDeath | AfterTurnEnd: Deal Amount damage to the target; AfterDeath: Remove this power |
| ConsumingShadowPower | Buff | Counter | AfterTurnEnd | AfterTurnEnd: evoke Last |
| CoolantPower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Give ? Block |
| CoordinatePower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain -Amount Strength |
| CorrosiveWavePower | Buff | Counter | AfterCardDrawn, AfterTurnEnd | AfterCardDrawn: Apply Amount Poison to the target; AfterTurnEnd: Remove this power |
| CorruptionPower | Buff | Single | TryModifyEnergyCostInCombat | TryModifyEnergyCostInCombat: returns false or true |
| CountdownPower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Apply Amount Doom to the target |
| CoveredPower | Buff | Single | AfterApplied, AfterDeath, ModifyDamageMultiplicative, AfterTurnEnd | AfterApplied: Apply 1 Intercept to the target; AfterDeath: Remove this power; ModifyDamageMultiplicative: returns 1 or 0; AfterTurnEnd: Remove this power |
| CrabRagePower | Buff | Single | AfterDeath | AfterDeath: Gain 5 Strength; AfterDeath: Give 99 Block; AfterDeath: Remove this power |
| CreativeAiPower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: get Distinct For Combat; BeforeHandDraw: Add a generated card to your hand |
| CrimsonMantlePower | Buff | Counter | AfterPlayerTurnStart | AfterPlayerTurnStart: Deal ? damage to the target; AfterPlayerTurnStart: Give Amount Block |
| CrueltyPower | Buff | Counter | passive | no hook effect detected in source |
| CrushUnderPower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| CuriousPower | Buff | Counter | TryModifyEnergyCostInCombat | TryModifyEnergyCostInCombat: returns false or true |
| CurlUpPower | Buff | Counter | AfterDamageReceived, AfterCardPlayed | AfterCardPlayed: Give Amount Block; AfterCardPlayed: Remove this power |
| DampenPower | Debuff | None | AfterApplied, AfterDeath, AfterRemoved | AfterApplied: Downgrade a card; AfterApplied: Give a card Ethereal; AfterDeath: Remove this power; AfterRemoved: Upgrade a card |
| DanseMacabrePower | Buff | Counter | BeforeCardPlayed | BeforeCardPlayed: Give Amount Block |
| DarkEmbracePower | Buff | Counter | AfterCardExhausted, AfterTurnEnd | AfterCardExhausted: Draw Amount cards; AfterTurnEnd: Draw ? cards |
| DarkShacklesPower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| DebilitatePower | Debuff | Counter | AfterTurnEnd | AfterTurnEnd: decrement |
| DemesnePower | Buff | Counter | ModifyHandDraw, ModifyMaxEnergy | ModifyHandDraw: returns count or count + base.Amount; ModifyMaxEnergy: returns amount or amount + base.Amount |
| DemisePower | Debuff | Counter | AfterTurnEnd | AfterTurnEnd: Deal Amount damage to the target |
| DemonFormPower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Gain Amount Strength |
| DevourLifePower | Buff | Counter | AfterCardPlayed | AfterCardPlayed: Summon Amount |
| DexterityPower | Buff | Counter | ModifyBlockAdditive | ModifyBlockAdditive: returns 0 or Amount |
| DiamondDiademPower | Buff | Single | ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or 0.5; AfterTurnEnd: Remove this power |
| DieForYouPower | Buff | Single | ModifyUnblockedDamageTarget, ShouldAllowHitting, ShouldCreatureBeRemovedFromCombatAfterDeath, ShouldPowerBeRemovedAfterOwnerDeath | ModifyUnblockedDamageTarget: returns target or base.Owner; ShouldAllowHitting: returns creature.IsAlive; ShouldCreatureBeRemovedFromCombatAfterDeath: returns true or false; ShouldPowerBeRemovedAfterOwnerDeath: returns false |
| DisintegrationPower | Debuff | Counter | AfterTurnEndLate | AfterTurnEndLate: Deal Amount damage to the target |
| DoomPower | Debuff | Counter | BeforeTurnEnd | BeforeTurnEnd: returns base.Owner.CurrentHp <= base.Amount |
| DoorRevivalPower | Buff | Single | BeforeDeath, AfterDeath, ShouldAllowHitting, ShouldStopCombatFromEnding, ShouldCreatureBeRemovedFromCombatAfterDeath, ShouldPowerBeRemovedAfterOwnerDeath | AfterDeath: add; ShouldAllowHitting: returns true or !IsHalfDead; ShouldStopCombatFromEnding: returns false or door.Doormaker.IsAlive; ShouldCreatureBeRemovedFromCombatAfterDeath: returns false or true; ShouldPowerBeRemovedAfterOwnerDeath: returns false |
| DoubleDamagePower | Buff | Counter | ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or 2; AfterTurnEnd: tick Down Duration |
| DrawCardsNextTurnPower | Buff | Counter | ModifyHandDraw, AfterSideTurnStart | ModifyHandDraw: returns count or count + base.Amount; AfterSideTurnStart: Remove this power |
| DrumOfBattlePower | Buff | Counter | BeforeHandDrawLate | BeforeHandDrawLate: Shuffle your draw pile; BeforeHandDrawLate: Exhaust a card |
| DuplicationPower | Buff | Counter | ModifyCardPlayCount, AfterModifyingCardPlayCount, AfterTurnEnd | ModifyCardPlayCount: returns playCount or playCount + 1; AfterModifyingCardPlayCount: decrement; AfterTurnEnd: Remove this power |
| DyingStarPower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| EchoFormPower | Buff | Counter | ModifyCardPlayCount, AfterModifyingCardPlayCount | ModifyCardPlayCount: returns playCount or playCount + 1 |
| EnergyNextTurnPower | Buff | Counter | AfterEnergyReset | AfterEnergyReset: Gain Amount Energy; AfterEnergyReset: Remove this power |
| EnfeeblingTouchPower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| EnragePower | Buff | Counter | AfterCardPlayed | AfterCardPlayed: Gain Amount Strength |
| EntropyPower | Buff | Counter | AfterPlayerTurnStart | AfterPlayerTurnStart: Choose a card in your hand; AfterPlayerTurnStart: Transform a card into a random card |
| EnvenomPower | Buff | Counter | AfterDamageGiven | AfterDamageGiven: Apply Amount Poison to the target |
| EscapeArtistPower | Buff | Counter | AfterTurnEnd | AfterTurnEnd: decrement |
| FanOfKnivesPower | Buff | Single | passive | no hook effect detected in source |
| FastenPower | Buff | Counter | ModifyBlockAdditive, AfterModifyingBlockAmount | ModifyBlockAdditive: returns 0 or Amount |
| FeedingFrenzyPower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain -Amount Strength |
| FeelNoPainPower | Buff | Counter | AfterCardExhausted | AfterCardExhausted: Give Amount Block |
| FeralPower | Buff | Counter | AfterApplied, AfterModifyingCardPlayResultPileOrPosition, AfterSideTurnStart | no hook effect detected in source |
| FlameBarrierPower | Buff | Counter | AfterDamageReceived, AfterTurnEnd | AfterDamageReceived: Deal Amount damage to the target; AfterTurnEnd: Remove this power |
| FlankingPower | Debuff | Counter | AfterApplied, ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or Amount; AfterTurnEnd: Remove this power |
| FlexPotionPower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain -Amount Strength |
| FlutterPower | Buff | Counter | ModifyDamageMultiplicative, AfterDamageReceived | ModifyDamageMultiplicative: returns 1 or 50 / 100; AfterDamageReceived: decrement; AfterDamageReceived: Stun the target |
| FocusedStrikePower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryFocusPower.BeforeApplied: Apply Amount Focus to the target; TemporaryFocusPower.AfterPowerAmountChanged: Gain Amount Focus; TemporaryFocusPower.AfterTurnEnd: Remove this power; TemporaryFocusPower.AfterTurnEnd: Gain -Amount Focus |
| FocusPower | Buff | Counter | ModifyOrbValue | ModifyOrbValue: returns value or Math.Max(value + base.Amount, 0) |
| ForbiddenGrimoirePower | Buff | Counter | AfterCombatEnd | no hook effect detected in source |
| ForegoneConclusionPower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: Shuffle your draw pile; BeforeHandDraw: Put a card into your hand; BeforeHandDraw: Remove this power |
| FrailPower | Debuff | Counter | ModifyBlockMultiplicative, AfterTurnEnd | ModifyBlockMultiplicative: returns 1 or 0.75; AfterTurnEnd: tick Down Duration |
| FreeAttackPower | Buff | Counter | TryModifyEnergyCostInCombat, BeforeCardPlayed | TryModifyEnergyCostInCombat: returns false or true; BeforeCardPlayed: decrement |
| FreePowerPower | Buff | Counter | TryModifyEnergyCostInCombat, BeforeCardPlayed | TryModifyEnergyCostInCombat: returns false or true; BeforeCardPlayed: decrement |
| FreeSkillPower | Buff | Counter | TryModifyEnergyCostInCombat, BeforeCardPlayed | TryModifyEnergyCostInCombat: returns false or true; BeforeCardPlayed: decrement |
| FriendshipPower | Buff | Counter | ModifyMaxEnergy | ModifyMaxEnergy: returns amount or amount + base.Amount |
| FurnacePower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Forge Amount |
| GalvanicPower | Buff | Counter | BeforeCombatStart, AfterCardEnteredCombat, AfterCardPlayed | BeforeCombatStart: afflict Galvanized; AfterCardEnteredCombat: afflict Galvanized; AfterCardPlayed: Lose Amount HP |
| GenesisPower | Buff | Counter | AfterEnergyReset | AfterEnergyReset: Gain Amount Stars |
| GigantificationPower | Buff | Counter | BeforeAttack, ModifyDamageMultiplicative, AfterAttack | ModifyDamageMultiplicative: returns 1 or 3; AfterAttack: decrement |
| GrapplePower | Debuff | Counter | AfterBlockGained, AfterTurnEnd | AfterBlockGained: Deal Amount damage to the target; AfterTurnEnd: Remove this power |
| GravityPower | Buff | Counter | BeforeCardPlayed, AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Deal ? damage to the target; AfterTurnEnd: Remove this power |
| GuardedPower | Buff | Single | AfterApplied, AfterDeath, ModifyDamageMultiplicative | AfterDeath: Remove this power; ModifyDamageMultiplicative: returns 1 or 0.5 |
| HailstormPower | Buff | Counter | BeforeTurnEnd | BeforeTurnEnd: Deal Amount damage to the target |
| HammerTimePower | Buff | Single | AfterForge | AfterForge: Forge ? |
| HangPower | Debuff | Counter | ModifyDamageMultiplicative | ModifyDamageMultiplicative: returns 1 or Amount |
| HardenedShellPower | Buff | Counter | ModifyHpLostBeforeOstyLate, AfterModifyingHpLostBeforeOsty, AfterDamageReceived, BeforeSideTurnStart | ModifyHpLostBeforeOstyLate: returns amount or Math.Min(amount, base.Amount - GetInternalData<Data>().damageReceivedThisTurn) |
| HardToKillPower | Buff | Counter | ModifyDamageCap, AfterModifyingDamageAmount | ModifyDamageCap: returns decimal.MaxValue or Amount |
| HatchPower | Buff | Counter | AfterTurnEnd | AfterTurnEnd: tick Down Duration |
| HauntPower | Buff | Counter | AfterCardPlayed | AfterCardPlayed: Deal Amount damage to the target |
| HeistPower | Buff | Counter | BeforeDeath | no hook effect detected in source |
| HelicalDartPower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryDexterityPower.BeforeApplied: Apply Amount Dexterity to the target; TemporaryDexterityPower.AfterPowerAmountChanged: Gain Amount Dexterity; TemporaryDexterityPower.AfterTurnEnd: Remove this power; TemporaryDexterityPower.AfterTurnEnd: Gain -Amount Dexterity |
| HelloWorldPower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: get Distinct For Combat; BeforeHandDraw: Add a generated card to your hand |
| HellraiserPower | Buff | Single | AfterCardDrawnEarly, BeforeAttack | AfterCardDrawnEarly: Auto-play a card |
| HexPower | Debuff | Single | AfterApplied, AfterCardEnteredCombat, AfterDeath, AfterRemoved | AfterDeath: Remove this power; AfterRemoved: remove Keyword; AfterRemoved: clear Affliction |
| HighVoltagePower | Buff | Counter | AfterTurnEnd | AfterTurnEnd: Gain Amount Strength |
| HotfixPower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryFocusPower.BeforeApplied: Apply Amount Focus to the target; TemporaryFocusPower.AfterPowerAmountChanged: Gain Amount Focus; TemporaryFocusPower.AfterTurnEnd: Remove this power; TemporaryFocusPower.AfterTurnEnd: Gain -Amount Focus |
| IllusionPower | Buff | Single | ShouldPowerBeRemovedOnDeath, AfterApplied, AfterDeath, ShouldAllowHitting, ShouldCreatureBeRemovedFromCombatAfterDeath, AfterCombatEnd | ShouldPowerBeRemovedOnDeath: returns power.Type == PowerType.Debuff; AfterApplied: Gain 1 Minion; ShouldAllowHitting: returns true or false; ShouldCreatureBeRemovedFromCombatAfterDeath: returns true or false |
| ImbalancedPower | Debuff | Single | AfterDamageGiven | AfterDamageGiven: Stun the target |
| ImprovementPower | Buff | Counter | AfterCombatEnd | AfterCombatEnd: Upgrade a card |
| InfernoPower | Buff | Counter | AfterPlayerTurnStart, AfterDamageReceived | AfterPlayerTurnStart: Deal ? damage to the target; AfterDamageReceived: Deal Amount damage to the target |
| InfestedPower | Buff | Single | AfterDeath, ShouldStopCombatFromEnding | AfterDeath: add; ShouldStopCombatFromEnding: returns true |
| InfiniteBladesPower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: Add Amount Shivs to your hand |
| IntangiblePower | Buff | Counter | ModifyHpLostAfterOsty, AfterModifyingHpLostAfterOsty, ModifyDamageCap, AfterModifyingDamageAmount, AfterTurnEnd | ModifyHpLostAfterOsty: returns amount or Math.Min(GetDamageCap(dealer), amount); ModifyDamageCap: returns decimal.MaxValue or GetDamageCap(dealer); AfterTurnEnd: tick Down Duration |
| InterceptPower | Buff | Single | ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or GetInternalData<Data>().coveredCreatures.Count + 1; AfterTurnEnd: Remove this power |
| IterationPower | Buff | Counter | AfterCardDrawn | AfterCardDrawn: Draw Amount cards |
| JuggernautPower | Buff | Counter | AfterBlockGained | AfterBlockGained: Deal Amount damage to the target |
| JugglingPower | Buff | Counter | AfterApplied, AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Add a generated card to your hand |
| KnockdownPower | Debuff | Counter | AfterApplied, ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or Amount; AfterTurnEnd: Remove this power |
| LeadershipPower | Buff | Counter | ModifyDamageAdditive | ModifyDamageAdditive: returns 0 or Amount |
| LethalityPower | Buff | Counter | ModifyDamageMultiplicative | ModifyDamageMultiplicative: returns 1 or 1 + base.Amount / 100 |
| LightningRodPower | Buff | Counter | AfterEnergyReset | AfterEnergyReset: Channel a Lightning orb; AfterEnergyReset: decrement |
| LoopPower | Buff | Counter | AfterPlayerTurnStart | AfterPlayerTurnStart: Trigger an orb passive |
| MachineLearningPower | Buff | Counter | ModifyHandDraw | ModifyHandDraw: returns count or count + base.Amount |
| MagicBombPower | Debuff | Counter | AfterTurnEnd, AfterDeath | AfterTurnEnd: Deal Amount damage to the target; AfterTurnEnd: Remove this power; AfterDeath: Remove this power |
| ManglePower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| MasterPlannerPower | Buff | Single | AfterCardPlayed | AfterCardPlayed: Give a card Sly |
| MayhemPower | Buff | Counter | BeforeHandDrawLate | BeforeHandDrawLate: Auto-play a card from your draw pile |
| MindRotPower | Debuff | Counter | ModifyHandDraw, AfterModifyingHandDraw | ModifyHandDraw: returns count or Math.Max(0, count - base.Amount) |
| MinionPower | Buff | Single | ShouldPowerBeRemovedAfterOwnerDeath, ShouldOwnerDeathTriggerFatal | ShouldPowerBeRemovedAfterOwnerDeath: returns false; ShouldOwnerDeathTriggerFatal: returns false |
| MonarchsGazePower | Buff | Counter | AfterDamageGiven | AfterDamageGiven: Apply Amount Monarchs Gaze Strength Down to the target |
| MonarchsGazeStrengthDownPower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| MonologuePower | Buff | Counter / None | BeforeCardPlayed, AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Gain ? Strength; AfterTurnEnd: Remove this power; AfterTurnEnd: Gain -0 Strength |
| NecroMasteryPower | Buff | Counter | AfterCurrentHpChanged | AfterCurrentHpChanged: Deal ? damage to the target |
| NemesisPower | Buff | Single | AfterTurnEnd | AfterTurnEnd: Gain 1 Intangible; AfterTurnEnd: Remove a power from the target |
| NeurosurgePower | Debuff | Counter | AfterSideTurnStart | AfterSideTurnStart: Gain Amount Doom |
| NightmarePower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: Add a generated card to your hand; BeforeHandDraw: Remove this power |
| NoBlockPower | Debuff | Counter | AfterTurnEnd, ModifyBlockMultiplicative | AfterTurnEnd: decrement; ModifyBlockMultiplicative: returns 1 or 0 |
| NoDrawPower | Debuff | Single | ShouldDraw, AfterTurnEnd | ShouldDraw: returns true or false; AfterTurnEnd: Remove this power |
| NostalgiaPower | Buff | Counter | AfterModifyingCardPlayResultPileOrPosition | no hook effect detected in source |
| NoxiousFumesPower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Apply Amount Poison to the target |
| OblivionPower | Debuff | Counter | BeforeCardPlayed, AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Gain ? Doom; AfterTurnEnd: Remove this power |
| OneTwoPunchPower | Buff | Counter | ModifyCardPlayCount, AfterModifyingCardPlayCount, AfterTurnEnd | ModifyCardPlayCount: returns playCount or playCount + 1; AfterModifyingCardPlayCount: decrement; AfterTurnEnd: Remove this power |
| OrbitPower | Buff | Counter | AfterEnergySpent | AfterEnergySpent: Gain ? Energy |
| OutbreakPower | Buff | Counter | AfterPowerAmountChanged | AfterPowerAmountChanged: Deal Amount damage to the target |
| PagestormPower | Buff | Counter | AfterCardDrawn | AfterCardDrawn: Draw Amount cards |
| PainfulStabsPower | Buff | Counter | ShouldPowerBeRemovedAfterOwnerDeath, ShouldCreatureBeRemovedFromCombatAfterDeath, AfterAttack | ShouldPowerBeRemovedAfterOwnerDeath: returns false; ShouldCreatureBeRemovedFromCombatAfterDeath: returns creature != base.Owner; AfterAttack: add To Combat And Preview Wound |
| PaleBlueDotPower | Buff | Counter | ModifyHandDraw, AfterModifyingHandDraw | ModifyHandDraw: returns count or count + base.Amount |
| PanachePower | Buff | Counter | AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Deal Amount damage to the target |
| PaperCutsPower | Buff | Counter | AfterDamageGiven | AfterDamageGiven: Lose Amount Max HP |
| ParryPower | Buff | Counter | passive | no hook effect detected in source |
| PersonalHivePower | Buff | Counter | AfterDamageReceived | AfterDamageReceived: Add a generated card to your draw pile |
| PhantomBladesPower | Buff | Counter | AfterCardEnteredCombat, AfterApplied, ModifyDamageAdditive | AfterCardEnteredCombat: Give a card Retain; AfterApplied: Give a card Retain; ModifyDamageAdditive: returns 0 or Amount |
| PiercingWailPower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| PillarOfCreationPower | Buff | Counter | AfterCardGeneratedForCombat | AfterCardGeneratedForCombat: Give Amount Block |
| PlatingPower | Buff | Counter | AfterApplied, BeforeSideTurnStart, BeforeTurnEndEarly, AfterTurnEnd | BeforeSideTurnStart: Give Amount Block; BeforeTurnEndEarly: Give Amount Block; AfterTurnEnd: Increase a power already on the target; AfterTurnEnd: decrement |
| PlowPower | Debuff | Counter | AfterDamageReceived | AfterDamageReceived: Remove Strength from the target; AfterDamageReceived: Stun the target; AfterDamageReceived: Remove this power |
| PoisonPower | Debuff | Counter | AfterSideTurnStart | AfterSideTurnStart: Deal Amount damage to the target; AfterSideTurnStart: decrement |
| PossessSpeedPower | Buff | Single | AfterPowerAmountChanged, AfterDeath | AfterDeath: Apply ? Dexterity to the target |
| PossessStrengthPower | Buff | Single | AfterPowerAmountChanged, AfterDeath | AfterDeath: Apply ? Strength to the target |
| PrepTimePower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Gain Amount Vigor |
| PyrePower | Buff | Counter | ModifyMaxEnergy | ModifyMaxEnergy: returns amount or amount + base.Amount |
| RadiancePower | Buff | Counter | AfterEnergyReset | AfterEnergyReset: Gain 1 Energy; AfterEnergyReset: decrement |
| RagePower | Buff | Counter | AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Give Amount Block; AfterTurnEnd: Remove this power |
| RampartPower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Give Amount Block |
| RavenousPower | Buff | Counter | AfterDeath | AfterDeath: Stun the target; AfterDeath: Gain Amount Strength |
| ReaperFormPower | Buff | Counter | AfterDamageGiven | AfterDamageGiven: Apply ? Doom to the target |
| ReattachPower | Buff | Single | AfterDeath, ShouldAllowHitting, ShouldCreatureBeRemovedFromCombatAfterDeath, ShouldPowerBeRemovedAfterOwnerDeath, ShouldOwnerDeathTriggerFatal | AfterDeath: returns GetOtherSegments().All((Creature s) => s.IsDead); ShouldAllowHitting: returns true or false; ShouldCreatureBeRemovedFromCombatAfterDeath: returns true or false; ShouldPowerBeRemovedAfterOwnerDeath: returns false; ShouldOwnerDeathTriggerFatal: returns AreAllOtherSegmentsDead() or GetOtherSegments().All((Creature s) => s.IsDead) |
| ReboundPower | Buff | Counter | AfterModifyingCardPlayResultPileOrPosition, AfterTurnEnd | AfterModifyingCardPlayResultPileOrPosition: decrement; AfterTurnEnd: Remove this power |
| ReflectPower | Buff | Counter | AfterDamageReceived, AfterSideTurnStart | AfterDamageReceived: Deal ? damage to the target; AfterSideTurnStart: decrement |
| RegenPower | Buff | Counter | AfterTurnEnd | AfterTurnEnd: Heal Amount HP; AfterTurnEnd: decrement |
| ReptileTrinketPower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain -Amount Strength |
| RetainHandPower | Buff | Counter | ShouldFlush, AfterTurnEnd | ShouldFlush: returns true or false; AfterTurnEnd: decrement |
| RingingPower | Debuff | Single | AfterApplied, AfterCardEnteredCombat, AfterTurnEnd, AfterRemoved, ShouldPlay | AfterApplied: afflict Ringing; AfterCardEnteredCombat: afflict Ringing; AfterTurnEnd: Remove this power; AfterRemoved: clear Affliction; ShouldPlay: returns true |
| RitualPower | Buff | Counter | AfterApplied, AfterTurnEnd | AfterTurnEnd: Gain Amount Strength |
| RollingBoulderPower | Buff | Counter | AfterPlayerTurnStart | no hook effect detected in source |
| RoyaltiesPower | Buff | Counter | AfterCombatEnd | no hook effect detected in source |
| RupturePower | Buff | Counter | BeforeCardPlayed, AfterDamageReceived, AfterCardPlayed | AfterDamageReceived: Gain Amount Strength; AfterCardPlayed: Gain ? Strength |
| SandpitPower | Buff | Counter | AfterApplied, AfterSideTurnStart, AfterPowerAmountChanged, AfterRemoved, AfterCreatureAddedToCombat, AfterOstyRevived, BeforeTurnEnd | AfterSideTurnStart: decrement; AfterRemoved: Kill the target |
| SeekingEdgePower | Buff | Single | passive | no hook effect detected in source |
| SelfFormingClayPower | Buff | Counter | AfterBlockCleared | AfterBlockCleared: Give Amount Block; AfterBlockCleared: Remove this power |
| SentryModePower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: Add a generated card to your hand |
| SerpentFormPower | Buff | Counter | BeforeCardPlayed, AfterCardPlayed | AfterCardPlayed: Deal ? damage to the target |
| SetupStrikePower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain -Amount Strength |
| ShacklingPotionPower | Debuff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryStrengthPower.BeforeApplied: Apply -Amount Strength to the target; TemporaryStrengthPower.AfterPowerAmountChanged: Gain -Amount Strength; TemporaryStrengthPower.AfterTurnEnd: Remove this power; TemporaryStrengthPower.AfterTurnEnd: Gain Amount Strength |
| ShadowmeldPower | Buff | Counter | ModifyBlockMultiplicative, AfterTurnEnd | ModifyBlockMultiplicative: returns 1 or Math.Pow(2.0, base.Amount); AfterTurnEnd: Remove this power |
| ShadowStepPower | Buff | Counter | AfterSideTurnStart | AfterSideTurnStart: Gain Amount Double Damage; AfterSideTurnStart: Remove this power |
| ShriekPower | Debuff | Counter | AfterDamageReceived | AfterDamageReceived: Stun the target; AfterDamageReceived: Remove this power |
| ShrinkPower | Debuff | Counter / Single | AfterApplied, AfterRemoved, AfterTurnEnd, AfterDeath, ModifyDamageMultiplicative | AfterTurnEnd: decrement; AfterDeath: Remove this power; ModifyDamageMultiplicative: returns 1 or (100 - 30) / 100 |
| ShroudPower | Buff | Counter | AfterPowerAmountChanged | AfterPowerAmountChanged: Give Amount Block |
| SicEmPower | Debuff | Counter | AfterDamageGiven, AfterTurnEnd | AfterDamageGiven: Summon Amount; AfterTurnEnd: Remove this power |
| SignalBoostPower | Buff | Counter | ModifyCardPlayCount, AfterModifyingCardPlayCount | ModifyCardPlayCount: returns playCount or playCount + 1; AfterModifyingCardPlayCount: decrement |
| SkittishPower | Buff | Counter | AfterAttack, AfterTurnEnd | AfterAttack: Give Amount Block |
| SleightOfFleshPower | Buff | Counter | AfterPowerAmountChanged | AfterPowerAmountChanged: Deal Amount damage to the target |
| SlipperyPower | Buff | Counter | ModifyDamageCap, AfterDamageReceived | ModifyDamageCap: returns decimal.MaxValue or 1; AfterDamageReceived: decrement |
| SlothPower | Debuff | Counter | ShouldPlay, BeforeCardPlayed, BeforeSideTurnStart | ShouldPlay: returns true or _cardsPlayedThisTurn < base.Amount |
| SlowPower | Debuff | Counter | AfterCardPlayed, ModifyDamageMultiplicative, AfterModifyingDamageAmount, AfterSideTurnStart | ModifyDamageMultiplicative: returns 1 or 1 + 0.1 * 0 |
| SlumberPower | Buff | Counter | AfterDamageReceived, AfterRemoved, AfterTurnEnd | AfterDamageReceived: decrement; AfterDamageReceived: Stun the target; AfterTurnEnd: decrement |
| SmoggyPower | Debuff | Single | AfterCardPlayed, AfterCardEnteredCombat, AfterTurnEnd, ShouldPlay | AfterCardPlayed: afflict Smog; AfterCardEnteredCombat: afflict Smog; AfterTurnEnd: clear Affliction; ShouldPlay: returns true or !(card.Affliction is Smog) |
| SmokestackPower | Buff | Counter | AfterCardGeneratedForCombat | AfterCardGeneratedForCombat: Deal Amount damage to the target |
| SneakyPower | Buff | Counter | AfterCardPlayed | AfterCardPlayed: Give Amount Block |
| SoarPower | Buff | Single | ModifyDamageMultiplicative | ModifyDamageMultiplicative: returns 1 or 50 / 100 |
| SpectrumShiftPower | Buff | Counter | BeforeHandDraw | BeforeHandDraw: get Distinct For Combat; BeforeHandDraw: Add a generated card to your hand |
| SpeedPotionPower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryDexterityPower.BeforeApplied: Apply Amount Dexterity to the target; TemporaryDexterityPower.AfterPowerAmountChanged: Gain Amount Dexterity; TemporaryDexterityPower.AfterTurnEnd: Remove this power; TemporaryDexterityPower.AfterTurnEnd: Gain -Amount Dexterity |
| SpeedsterPower | Buff | Counter | AfterCardDrawn | AfterCardDrawn: Deal Amount damage to the target |
| SpinnerPower | Buff | Counter | AfterEnergyReset | AfterEnergyReset: Channel a Glass orb |
| SpiritOfAshPower | Buff | Counter | BeforeCardPlayed | BeforeCardPlayed: Give Amount Block |
| StampedePower | Buff | Counter | BeforeTurnEnd | BeforeTurnEnd: Auto-play a card |
| StarNextTurnPower | Buff | Counter | AfterEnergyReset | AfterEnergyReset: Gain Amount Stars; AfterEnergyReset: Remove this power |
| SteamEruptionPower | Buff | Counter | AfterDeath, ShouldStopCombatFromEnding, ShouldCreatureBeRemovedFromCombatAfterDeath, ShouldPowerBeRemovedAfterOwnerDeath | ShouldStopCombatFromEnding: returns true; ShouldCreatureBeRemovedFromCombatAfterDeath: returns true or false; ShouldPowerBeRemovedAfterOwnerDeath: returns false |
| StockPower | Buff | Counter | AfterDeath, ShouldStopCombatFromEnding | AfterDeath: add; ShouldStopCombatFromEnding: returns true |
| StormPower | Buff | Counter | BeforeCardPlayed, AfterCardPlayed | AfterCardPlayed: Channel a Lightning orb |
| StranglePower | Debuff | Counter | BeforeCardPlayed, AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Deal ? damage to the target; AfterTurnEnd: Remove this power |
| StratagemPower | Buff | Counter | AfterShuffle | AfterShuffle: Choose a card from a pile; AfterShuffle: Put a card into your hand |
| StrengthPower | Buff | Counter | ModifyDamageAdditive | ModifyDamageAdditive: returns 0 or Amount |
| SubroutinePower | Buff | Counter | BeforeCardPlayed, AfterCardPlayed | AfterCardPlayed: Gain 1 Energy |
| SuckPower | Buff | Counter | AfterAttack | AfterAttack: Gain ? Strength |
| SummonNextTurnPower | Buff | Counter | AfterPlayerTurnStart | AfterPlayerTurnStart: Summon Amount; AfterPlayerTurnStart: Remove this power |
| SurprisePower | Buff | Single | AfterDeath, ShouldStopCombatFromEnding | AfterDeath: add Sneaky Gremlin; AfterDeath: add Fat Gremlin; AfterDeath: Apply a power to the target; ShouldStopCombatFromEnding: returns true |
| SurroundedPower | Debuff | Single | ModifyDamageMultiplicative, BeforeCardPlayed, BeforePotionUsed, AfterDeath | ModifyDamageMultiplicative: returns 1 or 1.5 |
| SwipePower | Buff | Single | BeforeDeath | no hook effect detected in source |
| SwordSagePower | Buff | Counter | AfterPowerAmountChanged, AfterCardEnteredCombat, AfterRemoved, TryModifyEnergyCostInCombat | TryModifyEnergyCostInCombat: returns false or true |
| SynchronizePower | Buff | Counter | BeforeApplied, AfterPowerAmountChanged, AfterTurnEnd | TemporaryFocusPower.BeforeApplied: Apply Amount Focus to the target; TemporaryFocusPower.AfterPowerAmountChanged: Gain Amount Focus; TemporaryFocusPower.AfterTurnEnd: Remove this power; TemporaryFocusPower.AfterTurnEnd: Gain -Amount Focus |
| TagTeamPower | Debuff | Counter | AfterApplied, ModifyCardPlayCount, AfterModifyingCardPlayCount | ModifyCardPlayCount: returns playCount or playCount + base.Amount; AfterModifyingCardPlayCount: Remove this power |
| TangledPower | Debuff | Counter | AfterApplied, AfterCardEnteredCombat, AfterTurnEnd, AfterRemoved, TryModifyEnergyCostInCombat | AfterApplied: afflict Entangled; AfterCardEnteredCombat: afflict Entangled; AfterTurnEnd: Remove this power; AfterRemoved: clear Affliction; TryModifyEnergyCostInCombat: returns false or true |
| TankPower | Buff | Single | AfterApplied, ModifyDamageMultiplicative | AfterApplied: Apply Amount Guarded to the target; ModifyDamageMultiplicative: returns 1 or 2 |
| TenderPower | Debuff | Counter | AfterCardPlayed, AfterTurnEnd | AfterCardPlayed: Gain -1 Strength; AfterCardPlayed: Gain -1 Dexterity; AfterTurnEnd: Gain ? Strength; AfterTurnEnd: Gain ? Dexterity |
| TerritorialPower | Buff | Counter | AfterTurnEnd | AfterTurnEnd: Gain Amount Strength |
| TheBombPower | Buff | Counter | BeforeTurnEnd | BeforeTurnEnd: decrement; BeforeTurnEnd: Deal 40 damage to the target; BeforeTurnEnd: Remove this power |
| TheGambitPower | Debuff | Single | AfterDamageReceived | AfterDamageReceived: Remove this power; AfterDamageReceived: Kill the target |
| TheHuntPower | Buff | Counter | passive | no hook effect detected in source |
| TheSealedThronePower | Buff | Counter | BeforeCardPlayed | BeforeCardPlayed: Gain Amount Stars |
| ThieveryPower | Buff | Counter | passive | no hook effect detected in source |
| ThornsPower | Buff | Counter | BeforeDamageReceived | BeforeDamageReceived: Deal Amount damage to the target |
| ThunderPower | Buff | Counter | AfterOrbEvoked | AfterOrbEvoked: Deal Amount damage to the target |
| ToolsOfTheTradePower | Buff | Counter | ModifyHandDraw, AfterPlayerTurnStart | ModifyHandDraw: returns count or count + base.Amount; AfterPlayerTurnStart: Discard Amount cards from your hand; AfterPlayerTurnStart: Discard a card |
| ToricToughnessPower | Buff | Counter | AfterBlockCleared | AfterBlockCleared: Give 0 Block; AfterBlockCleared: decrement |
| TrackingPower | Buff | Counter | ModifyDamageMultiplicative | ModifyDamageMultiplicative: returns 1 or Amount |
| TrashToTreasurePower | Buff | Counter | AfterCardGeneratedForCombat | AfterCardGeneratedForCombat: Channel an orb |
| TyrannyPower | Buff | Counter | ModifyHandDraw, AfterPlayerTurnStart | ModifyHandDraw: returns count or count + base.Amount; AfterPlayerTurnStart: Choose a card in your hand; AfterPlayerTurnStart: Exhaust a card |
| UnmovablePower | Buff | Counter | ModifyBlockMultiplicative | ModifyBlockMultiplicative: returns 1 or 2 |
| VeilpiercerPower | Buff | Counter | TryModifyEnergyCostInCombat, BeforeCardPlayed | TryModifyEnergyCostInCombat: returns false or true; BeforeCardPlayed: decrement |
| ViciousPower | Buff | Counter | AfterPowerAmountChanged | AfterPowerAmountChanged: Draw Amount cards |
| VigorPower | Buff | Counter | BeforeAttack, ModifyDamageAdditive, AfterAttack | ModifyDamageAdditive: returns 0 or Amount; AfterAttack: Increase a power already on the target |
| VitalSparkPower | Buff | Counter | AfterDamageReceived, BeforeSideTurnStart | AfterDamageReceived: Gain 1 Energy |
| VoidFormPower | Buff | Counter | BeforePowerAmountChanged, BeforeApplied, TryModifyEnergyCostInCombat, TryModifyStarCost, AfterCardPlayed, BeforeSideTurnStart | TryModifyEnergyCostInCombat: returns false or true; TryModifyStarCost: returns false or true |
| VulnerablePower | Debuff | Counter | ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or 1.5; AfterTurnEnd: tick Down Duration |
| WasteAwayPower | Debuff | Counter | ModifyMaxEnergy | ModifyMaxEnergy: returns amount or amount - base.Amount |
| WeakPower | Debuff | Counter | ModifyDamageMultiplicative, AfterTurnEnd | ModifyDamageMultiplicative: returns 1 or 0.75; AfterTurnEnd: tick Down Duration |
| WellLaidPlansPower | Buff | Counter | BeforeFlushLate | BeforeFlushLate: Choose a card in your hand |
| WraithFormPower | Debuff | Counter | AfterSideTurnStart | AfterSideTurnStart: Gain -Amount Dexterity |
