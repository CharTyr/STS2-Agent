# Card Behavior Index

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-09-20 21:16:43 +08:00

Behavior-oriented summaries extracted from card source. Vars, OnPlay, and OnUpgrade stay close to the code for tool-friendly lookup; Effect is the same readable summary used by cards.md, with ? marking an amount that is only computed at play time.

| Name | Vars | OnPlay | OnUpgrade | Owner | Effect |
| --- | --- | --- | --- | --- | --- |
| Abrasive | PowerVar<ThornsPower>(4m), PowerVar<DexterityPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DexterityPower>, PowerCmd.Apply<ThornsPower> | UpgradeValueBy(2m) | Silent | Gain 1 Dexterity; Gain 4 Thorns; keywords: Sly |
| Accelerant | DynamicVar("Accelerant", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<AccelerantPower> | UpgradeValueBy(1m) | Silent | Gain 1 Accelerant |
| Accuracy | PowerVar<AccuracyPower>(4m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<AccuracyPower> | UpgradeValueBy(2m) | Silent | Gain 4 Accuracy |
| Acrobatics | CardsVar(3) | CardPileCmd.Draw, CardSelectCmd.FromHandForDiscard, CardCmd.Discard | UpgradeValueBy(1m) | Silent | Draw 3 cards; Discard 1 card from your hand; Discard a card |
| AdaptiveStrike | DamageVar(18m, ValueProp.Move) | DamageCmd.Attack, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(5m) | Defect | Deal 18 damage; Add a generated card to your discard pile |
| Adrenaline | EnergyVar(1), CardsVar(2) | VfxCmd.PlayFullScreenInCombat, PlayerCmd.GainEnergy, CardPileCmd.Draw | UpgradeValueBy(1m) | Silent | Gain 1 Energy; Draw 2 cards; keywords: Exhaust |
| Afterimage | PowerVar<AfterimagePower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<AfterimagePower> | AddKeyword(Innate) | Silent | Gain 1 Afterimage |
| Afterlife | SummonVar(6m) | CreatureCmd.TriggerAnim, OstyCmd.Summon | UpgradeValueBy(3m) | Necrobinder | Summon 6; keywords: Exhaust |
| Aggression |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<AggressionPower> | AddKeyword(Innate) | Ironclad | Gain 1 Aggression |
| Alchemize |  | CreatureCmd.TriggerAnim, PotionCmd.TryToProcure |  | colorless | Obtain a random potion; keywords: Exhaust |
| Alignment | EnergyVar(2) | CreatureCmd.TriggerAnim, PlayerCmd.GainEnergy | UpgradeValueBy(1m) | Regent | Gain 2 Energy |
| AllForOne | DamageVar(10m, ValueProp.Move) | DamageCmd.Attack, CardPileCmd.Add | UpgradeValueBy(4m) | Defect | Deal 10 damage; Put a card into your hand |
| Anger | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(2m) | Ironclad | Deal 6 damage; Add a generated card to your discard pile |
| Anointed |  | CardPileCmd.Add | AddKeyword(Retain) | colorless | Put a card into your hand; keywords: Exhaust |
| Anticipate | PowerVar<DexterityPower>(3m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<AnticipatePower> | UpgradeValueBy(2m) | Silent | Gain 3 Anticipate |
| Apotheosis |  | CardCmd.Upgrade |  | event | Upgrade a card; keywords: Exhaust, Innate |
| Apparition | PowerVar<IntangiblePower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<IntangiblePower> |  | event | Gain 1 Intangible; keywords: Ethereal, Exhaust |
| Armaments | BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock, CardCmd.Upgrade, CardSelectCmd.FromHandForUpgrade |  | Ironclad | Gain 5 Block; if upgraded: Upgrade a card; Upgrade a card in your hand; Upgrade a card |
| Arsenal | PowerVar<ArsenalPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ArsenalPower> | UpgradeValueBy(1m) | Regent | Gain 1 Arsenal |
| AscendersBane |  |  |  | curse | no OnPlay effect in source; keywords: Eternal, Unplayable, Ethereal |
| AshenStrike | CalculationBaseVar(6m), ExtraDamageVar(3m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Ironclad | Deal ? damage |
| Assassinate | DamageVar(10m, ValueProp.Move), PowerVar<VulnerablePower>(1m) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(3m), UpgradeValueBy(1m) | Silent | Deal 10 damage; Apply 1 Vulnerable to the target; keywords: Innate, Exhaust |
| AstralPulse | DamageVar(14m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | Regent | Deal 14 damage to ALL enemies |
| Automation | EnergyVar(1) | PowerCmd.Apply<AutomationPower> |  | colorless | Gain 1 Automation |
| Backflip | BlockVar(5m, ValueProp.Move), CardsVar(2) | CreatureCmd.GainBlock, CardPileCmd.Draw | UpgradeValueBy(3m) | Silent | Gain 5 Block; Draw 2 cards |
| Backstab | DamageVar(11m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | Silent | Deal 11 damage; keywords: Exhaust, Innate |
| BadLuck | HpLossVar(13m) |  |  | curse | no OnPlay effect in source; keywords: Eternal, Unplayable |
| BallLightning | DamageVar(7m, ValueProp.Move) | DamageCmd.Attack, OrbCmd.Channel<LightningOrb> | UpgradeValueBy(3m) | Defect | Deal 7 damage; Channel a Lightning orb |
| BansheesCry | DamageVar(33m, ValueProp.Move), EnergyVar(2) | DamageCmd.Attack | UpgradeValueBy(6m) | Necrobinder | Deal 33 damage to ALL enemies |
| Barrage | DamageVar(5m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(2m) | Defect | Deal 5 damage ? times |
| Barricade |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<BarricadePower> |  | Ironclad | Gain 1 Barricade |
| Bash | DamageVar(8m, ValueProp.Move), PowerVar<VulnerablePower>(2m) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Ironclad | Deal 8 damage; Apply 2 Vulnerable to the target |
| BattleTrance | CardsVar(3) | CardPileCmd.Draw, PowerCmd.Apply<NoDrawPower> | UpgradeValueBy(1m) | Ironclad | Draw 3 cards; Gain 1 No Draw |
| BeaconOfHope |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<BeaconOfHopePower> | AddKeyword(Innate) | colorless | Gain 1 Beacon Of Hope |
| BeamCell | DamageVar(3m, ValueProp.Move), PowerVar<VulnerablePower>(1m) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(1m) | Defect | Deal 3 damage; Apply 1 Vulnerable to the target |
| BeatDown | CardsVar(3) | CardCmd.AutoPlay | UpgradeValueBy(1m) | colorless | Auto-play a card |
| BeatIntoShape | DamageVar(5m, ValueProp.Move), CalculationBaseVar(5m), CalculationExtraVar(5m), CalculatedVar("CalculatedForge") | DamageCmd.Attack, ForgeCmd.Forge | UpgradeValueBy(2m) | Regent | Deal 5 damage; Forge ? |
| Beckon | HpLossVar(6m) |  |  | status | no OnPlay effect in source |
| Begone | DamageVar(4m, ValueProp.Move) | DamageCmd.Attack, CardSelectCmd.FromHand, CardCmd.Upgrade, CardCmd.Transform | UpgradeValueBy(1m) | Regent | Deal 4 damage; Choose a card in your hand; if upgraded: Upgrade a card; Transform a card |
| BelieveInYou | EnergyVar(3) | PlayerCmd.GainEnergy | UpgradeValueBy(1m) | colorless | Gain 3 Energy |
| BiasedCognition | PowerVar<FocusPower>(4m), PowerVar<BiasedCognitionPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<FocusPower>, PowerCmd.Apply<BiasedCognitionPower> | UpgradeValueBy(1m) | Defect | Gain 4 Focus; Gain 1 Biased Cognition |
| BigBang | CardsVar(1), EnergyVar(1), StarsVar(1), ForgeVar(5) | CreatureCmd.TriggerAnim, CardPileCmd.Draw, PlayerCmd.GainStars, PlayerCmd.GainEnergy, ForgeCmd.Forge | AddKeyword(Innate) | Regent | Draw 1 card; Gain 1 Stars; Gain 1 Energy; Forge 5; keywords: Exhaust |
| BlackHole | PowerVar<BlackHolePower>(3m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<BlackHolePower> | UpgradeValueBy(1m) | Regent | Gain 3 Black Hole |
| BladeDance | CardsVar(3) | CreatureCmd.TriggerAnim | UpgradeValueBy(1m) | Silent | Add 3 Shivs to your hand; keywords: Exhaust |
| BladeOfInk | PowerVar<StrengthPower>(2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<BladeOfInkPower> | UpgradeValueBy(1m) | Silent | Gain 2 Blade Of Ink |
| BlightStrike | DamageVar(8m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<DoomPower> | UpgradeValueBy(2m) | Necrobinder | Deal 8 damage; Apply ? Doom to the target |
| Bloodletting | HpLossVar(3m), EnergyVar(2) | CreatureCmd.TriggerAnim, VfxCmd.PlayOnCreatureCenter, CreatureCmd.Damage, PlayerCmd.GainEnergy | UpgradeValueBy(1m) | Ironclad | Lose 3 HP; Gain 2 Energy |
| BloodWall | HpLossVar(2m), BlockVar(16m, ValueProp.Move) | VfxCmd.PlayOnCreatureCenter, CreatureCmd.Damage, CreatureCmd.TriggerAnim, SfxCmd.Play, VfxCmd.PlayOnCreature, CreatureCmd.GainBlock | UpgradeValueBy(4m) | Ironclad | Lose 2 HP; Gain 16 Block |
| Bludgeon | DamageVar(32m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(10m) | Ironclad | Deal 32 damage |
| Blur | BlockVar(5m, ValueProp.Move), DynamicVar("Blur", 1m) | CreatureCmd.GainBlock, PowerCmd.Apply<BlurPower> | UpgradeValueBy(3m) | Silent | Gain 5 Block; Gain 1 Blur |
| Bodyguard | SummonVar(5m) | CreatureCmd.TriggerAnim, OstyCmd.Summon | UpgradeValueBy(2m) | Necrobinder | Summon 5 |
| BodySlam | CalculationBaseVar(0m), ExtraDamageVar(1m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack |  | Ironclad | Deal ? damage |
| Bolas | DamageVar(3m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | colorless | Deal 3 damage |
| Bombardment | DamageVar(18m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(6m) | Regent | Deal 18 damage; keywords: Exhaust |
| BoneShards | OstyDamageVar(9m, ValueProp.Move), BlockVar(9m, ValueProp.Move) | DamageCmd.Attack, CreatureCmd.GainBlock, CreatureCmd.Kill | UpgradeValueBy(3m) | Necrobinder | Your Osty deals 9 damage to ALL enemies; Gain 9 Block; Kill your own Osty |
| BoostAway | BlockVar(6m, ValueProp.Move) | CreatureCmd.GainBlock, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(3m) | Defect | Gain 6 Block; Add a generated card to your discard pile |
| BootSequence | BlockVar(10m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Defect | Gain 10 Block; keywords: Innate, Exhaust |
| BorrowedTime | PowerVar<DoomPower>(3m), EnergyVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DoomPower>, PlayerCmd.GainEnergy | UpgradeValueBy(1m) | Necrobinder | Gain 3 Doom; Gain 1 Energy |
| BouncingFlask | PowerVar<PoisonPower>(3m), RepeatVar(3) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PoisonPower> | UpgradeValueBy(1m) | Silent | Apply 3 Poison to the target |
| Brand | HpLossVar(1m), PowerVar<StrengthPower>(1m) | CreatureCmd.TriggerAnim, VfxCmd.PlayOnCreatureCenter, CreatureCmd.Damage, CardSelectCmd.FromHand, CardCmd.Exhaust, SfxCmd.Play, PowerCmd.Apply<StrengthPower> | UpgradeValueBy(1m) | Ironclad | Lose 1 HP; Choose a card in your hand; Exhaust a card; Gain 1 Strength |
| Break | DamageVar(20m, ValueProp.Move), PowerVar<VulnerablePower>(5m) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(5m), UpgradeValueBy(2m) | Ironclad | Deal 20 damage; Apply 5 Vulnerable to the target |
| Breakthrough | DamageVar(9m, ValueProp.Move), HpLossVar(1m) | VfxCmd.PlayOnCreatureCenter, CreatureCmd.Damage, DamageCmd.Attack | UpgradeValueBy(4m) | Ironclad | Lose 1 HP; Deal 9 damage to ALL enemies |
| BrightestFlame | MaxHpVar(1m), EnergyVar(2), CardsVar(2) | PlayerCmd.GainEnergy, CardPileCmd.Draw, CreatureCmd.LoseMaxHp | UpgradeValueBy(1m) | event | Gain 2 Energy; Draw 2 cards; Lose 1 Max HP |
| BubbleBubble | PowerVar<PoisonPower>(9m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PoisonPower> | UpgradeValueBy(3m) | Silent | Apply 9 Poison to the target |
| Buffer | PowerVar<BufferPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<BufferPower> | UpgradeValueBy(1m) | Defect | Gain 1 Buffer |
| BulkUp | DynamicVar("OrbSlots", 1m), PowerVar<StrengthPower>(2m), PowerVar<DexterityPower>(2m) | CreatureCmd.TriggerAnim, OrbCmd.RemoveSlots, PowerCmd.Apply<StrengthPower>, PowerCmd.Apply<DexterityPower> | UpgradeValueBy(1m) | Defect | Remove 1 orb slot(s); Gain 2 Strength; Gain 2 Dexterity |
| BulletTime |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<NoDrawPower> |  | Silent | Gain 1 No Draw |
| Bully | CalculationBaseVar(4m), ExtraDamageVar(2m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Ironclad | Deal ? damage |
| Bulwark | BlockVar(13m, ValueProp.Move), ForgeVar(10) | CreatureCmd.GainBlock, CreatureCmd.TriggerAnim, ForgeCmd.Forge | UpgradeValueBy(3m) | Regent | Gain 13 Block; Forge 10 |
| BundleOfJoy | CardsVar(3) | CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(1m) | Regent | get Distinct For Combat; Add a generated card to your hand; keywords: Exhaust |
| Burn | DamageVar(2m, ValueProp.Unpowered \\| ValueProp.Move) |  |  | status | no OnPlay effect in source; keywords: Unplayable |
| BurningPact | CardsVar(2) | CardSelectCmd.FromHand, CardCmd.Exhaust, CreatureCmd.TriggerAnim, CardPileCmd.Draw | UpgradeValueBy(1m) | Ironclad | Choose a card in your hand; Exhaust a card; Draw 2 cards |
| Burst | DynamicVar("Skills", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<BurstPower> | UpgradeValueBy(1m) | Silent | Gain 1 Burst |
| Bury | DamageVar(52m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(11m) | Necrobinder | Deal 52 damage |
| ByrdonisEgg |  |  |  | quest | no OnPlay effect in source; keywords: Unplayable |
| ByrdSwoop | DamageVar(14m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | event | Deal 14 damage |
| Calamity |  | PowerCmd.Apply<CalamityPower> |  | colorless | Gain 1 Calamity |
| Calcify | PowerVar<CalcifyPower>(4m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CalcifyPower> | UpgradeValueBy(2m) | Necrobinder | Gain 4 Calcify |
| CalculatedGamble |  | CardCmd.DiscardAndDraw | AddKeyword(Retain) | Silent | Discard your hand and draw that many cards; keywords: Exhaust |
| CallOfTheVoid | CardsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CallOfTheVoidPower> | AddKeyword(Innate) | Necrobinder | Gain 1 Call Of The Void |
| Caltrops | PowerVar<ThornsPower>(3m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ThornsPower> | UpgradeValueBy(2m) | event | Gain 3 Thorns |
| Capacitor | RepeatVar(2) | CreatureCmd.TriggerAnim, OrbCmd.AddSlots | UpgradeValueBy(1m) | Defect | Add 2 orb slot(s) |
| CaptureSpirit | DamageVar(3m, ValueProp.Unblockable \\| ValueProp.Unpowered \\| ValueProp.Move), CardsVar(3) | CreatureCmd.TriggerAnim, CreatureCmd.Damage, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardsToCombat | UpgradeValueBy(1m) | Necrobinder | Deal 3 damage to the target; Add a generated card to your draw pile |
| Cascade |  | CardPileCmd.AutoPlayFromDrawPile |  | Ironclad | Auto-play a card from your draw pile |
| Catastrophe | CardsVar(2) | CardCmd.AutoPlay | UpgradeValueBy(1m) | colorless | Auto-play a card |
| CelestialMight | DamageVar(6m, ValueProp.Move), RepeatVar(3) | DamageCmd.Attack | UpgradeValueBy(2m) | Regent | Deal 6 damage 3 times |
| Chaos | RepeatVar(1) | CreatureCmd.TriggerAnim, OrbCmd.Channel | UpgradeValueBy(1m) | Defect | Channel an orb |
| Charge | CardsVar(2) | CreatureCmd.TriggerAnim, CardSelectCmd.FromSimpleGrid, CardCmd.TransformTo<MinionStrike>, CardCmd.Upgrade |  | Regent | Choose a card from a pile; Transform a card into Minion Strike; Upgrade a card |
| ChargeBattery | BlockVar(7m, ValueProp.Move), EnergyVar(1) | CreatureCmd.GainBlock, PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(3m) | Defect | Gain 7 Block; Gain 1 Energy Next Turn |
| ChildOfTheStars | DynamicVar("BlockForStars", 2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ChildOfTheStarsPower> | UpgradeValueBy(1m) | Regent | Gain 2 Child Of The Stars |
| Chill |  | CreatureCmd.TriggerAnim, OrbCmd.Channel<FrostOrb> |  | Defect | Channel a Frost orb; keywords: Exhaust |
| Cinder | DamageVar(17m, ValueProp.Move), DynamicVar("CardsToExhaust", 1m) | DamageCmd.Attack, CardPileCmd.ShuffleIfNecessary, CardCmd.Exhaust | UpgradeValueBy(5m) | Ironclad | Deal 17 damage; Shuffle your draw pile; Exhaust a card |
| Clash | DamageVar(14m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | event | Deal 14 damage |
| Claw | DamageVar(3m, ValueProp.Move), DynamicVar("Increase", 2m) | DamageCmd.Attack | UpgradeValueBy(1m) | Defect | Deal 3 damage |
| Cleanse | SummonVar(3m) | CreatureCmd.TriggerAnim, OstyCmd.Summon, CardSelectCmd.FromSimpleGrid, CardCmd.Exhaust | UpgradeValueBy(2m) | Necrobinder | Summon 3; Choose a card from a pile; Exhaust a card |
| CloakAndDagger | BlockVar(6m, ValueProp.Move), CardsVar(1) | CreatureCmd.GainBlock | UpgradeValueBy(1m) | Silent | Gain 6 Block; Add 1 Shiv to your hand |
| CloakOfStars | BlockVar(7m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Regent | Gain 7 Block |
| Clumsy |  |  |  | curse | no OnPlay effect in source; keywords: Unplayable, Ethereal |
| ColdSnap | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack, OrbCmd.Channel<FrostOrb> | UpgradeValueBy(3m) | Defect | Deal 6 damage; Channel a Frost orb |
| CollisionCourse | DamageVar(9m, ValueProp.Move) | DamageCmd.Attack, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(3m) | Regent | Deal 9 damage; Add a generated card to your hand |
| Colossus | BlockVar(5m, ValueProp.Move), DynamicVar("Colossus", 1m) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<ColossusPower> | UpgradeValueBy(3m) | Ironclad | Gain 5 Block; Gain 1 Colossus |
| Comet | DamageVar(33m, ValueProp.Move), PowerVar<VulnerablePower>(3m), PowerVar<WeakPower>(3m) | CreatureCmd.TriggerAnim, DamageCmd.Attack, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(11m) | Regent | Deal 33 damage; Apply 3 Weak to the target; Apply 3 Vulnerable to the target |
| Compact | BlockVar(6m, ValueProp.Move) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, CardCmd.Upgrade, CardCmd.Transform | UpgradeValueBy(1m) | Defect | Gain 6 Block; if upgraded: Upgrade a card; Transform a card |
| CompileDriver | DamageVar(7m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedCards") | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(3m) | Defect | Deal 7 damage; Draw ? cards |
| Conflagration | CalculationBaseVar(8m), ExtraDamageVar(2m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Ironclad | Deal ? damage to ALL enemies |
| Conqueror | ForgeVar(3) | CreatureCmd.TriggerAnim, ForgeCmd.Forge, PowerCmd.Apply<ConquerorPower> | UpgradeValueBy(2m) | Regent | Forge 3; Apply 1 Conqueror to the target |
| ConsumingShadow | RepeatVar(2), PowerVar<ConsumingShadowPower>(1m) | CreatureCmd.TriggerAnim, OrbCmd.Channel<DarkOrb>, PowerCmd.Apply<ConsumingShadowPower> | UpgradeValueBy(1m) | Defect | Channel a Dark orb; Gain 1 Consuming Shadow |
| Convergence | EnergyVar(1), StarsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<RetainHandPower>, PowerCmd.Apply<EnergyNextTurnPower>, PowerCmd.Apply<StarNextTurnPower> | UpgradeValueBy(1m) | Regent | Gain 1 Retain Hand; Gain 1 Energy Next Turn; Gain 1 Star Next Turn |
| Coolant | PowerVar<CoolantPower>(2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CoolantPower> | UpgradeValueBy(1m) | Defect | Gain 2 Coolant |
| Coolheaded | CardsVar(1) | CreatureCmd.TriggerAnim, OrbCmd.Channel<FrostOrb>, CardPileCmd.Draw | UpgradeValueBy(1m) | Defect | Channel a Frost orb; Draw 1 card |
| Coordinate | PowerVar<StrengthPower>(5m) | PowerCmd.Apply<CoordinatePower> | UpgradeValueBy(3m) | colorless | Apply 5 Coordinate to the target |
| CorrosiveWave | DynamicVar("CorrosiveWave", 3m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CorrosiveWavePower> | UpgradeValueBy(1m) | Silent | Gain 3 Corrosive Wave |
| Corruption | DynamicVar("Power", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CorruptionPower> |  | Ironclad | Gain 1 Corruption |
| CosmicIndifference | BlockVar(6m, ValueProp.Move) | CreatureCmd.GainBlock, CardSelectCmd.FromSimpleGrid, CardPileCmd.Add | UpgradeValueBy(3m) | Regent | Gain 6 Block; Choose a card from a pile; Put a card into your draw pile |
| Countdown | PowerVar<CountdownPower>(6m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CountdownPower> | UpgradeValueBy(3m) | Necrobinder | Gain 6 Countdown |
| CrashLanding | DamageVar(21m, ValueProp.Move) | DamageCmd.Attack, CardPileCmd.AddGeneratedCardsToCombat | UpgradeValueBy(5m) | Regent | Deal 21 damage to ALL enemies; Add a generated card to your hand |
| CreativeAi | DynamicVar("CreativeAi", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CreativeAiPower> |  | Defect | Gain 1 Creative AI |
| CrescentSpear | CalculationBaseVar(6m), ExtraDamageVar(2m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Regent | Deal ? damage |
| CrimsonMantle | PowerVar<CrimsonMantlePower>(8m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CrimsonMantlePower> | UpgradeValueBy(2m) | Ironclad | Gain 8 Crimson Mantle |
| Cruelty | PowerVar<CrueltyPower>(25m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<CrueltyPower> | UpgradeValueBy(25m) | Ironclad | Gain 25 Cruelty |
| CrushUnder | DamageVar(7m, ValueProp.Move), DynamicVar("StrengthLoss", 1m) | CreatureCmd.TriggerAnim, DamageCmd.Attack, PowerCmd.Apply<CrushUnderPower> | UpgradeValueBy(1m) | Regent | Deal 7 damage to ALL enemies; Apply 1 Crush Under to the target |
| CurseOfTheBell |  |  |  | curse | no OnPlay effect in source; keywords: Eternal, Unplayable |
| DaggerSpray | DamageVar(4m, ValueProp.Move) | SfxCmd.Play, DamageCmd.Attack | UpgradeValueBy(2m) | Silent | Deal 4 damage 2 times to ALL enemies |
| DaggerThrow | DamageVar(9m, ValueProp.Move) | DamageCmd.Attack, CardPileCmd.Draw, CardSelectCmd.FromHandForDiscard, CardCmd.Discard | UpgradeValueBy(3m) | Silent | Deal 9 damage; Draw 1 card; Discard 1 card from your hand; Discard a card |
| DanseMacabre | PowerVar<DanseMacabrePower>(3m), EnergyVar(2) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DanseMacabrePower> | UpgradeValueBy(1m) | Necrobinder | Gain 3 Danse Macabre |
| DarkEmbrace |  | PowerCmd.Apply<DarkEmbracePower> |  | Ironclad | Gain 1 Dark Embrace |
| Darkness |  | CreatureCmd.TriggerAnim, OrbCmd.Channel<DarkOrb>, OrbCmd.Passive |  | Defect | Channel a Dark orb; Trigger an orb passive |
| DarkShackles | DynamicVar("StrengthLoss", 9m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DarkShacklesPower> | UpgradeValueBy(6m) | colorless | Apply 9 Dark Shackles to the target; keywords: Exhaust |
| Dash | DamageVar(10m, ValueProp.Move), BlockVar(10m, ValueProp.Move) | CreatureCmd.GainBlock, DamageCmd.Attack | UpgradeValueBy(3m) | Silent | Gain 10 Block; Deal 10 damage |
| Dazed |  |  |  | status | no OnPlay effect in source; keywords: Ethereal, Unplayable |
| DeadlyPoison | PowerVar<PoisonPower>(5m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PoisonPower> | UpgradeValueBy(2m) | Silent | Apply 5 Poison to the target |
| Deathbringer | PowerVar<DoomPower>(21m), PowerVar<WeakPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DoomPower>, PowerCmd.Apply<WeakPower> | UpgradeValueBy(5m) | Necrobinder | Apply 21 Doom to the target; Apply 1 Weak to the target |
| DeathMarch | CalculationBaseVar(8m), ExtraDamageVar(3m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Necrobinder | Deal ? damage |
| DeathsDoor | BlockVar(6m, ValueProp.Move), RepeatVar(2) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock | UpgradeValueBy(1m) | Necrobinder | Gain 6 Block |
| Debilitate | DamageVar(7m, ValueProp.Move), PowerVar<DebilitatePower>(3m) | DamageCmd.Attack, PowerCmd.Apply<DebilitatePower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Necrobinder | Deal 7 damage; Apply 3 Debilitate to the target |
| Debris |  |  |  | status | no gameplay effect detected in OnPlay; keywords: Exhaust |
| Debt | GoldVar(10) |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| Decay | DamageVar(2m, ValueProp.Unpowered \\| ValueProp.Move) |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| DecisionsDecisions | CardsVar(3), RepeatVar(3) | CreatureCmd.TriggerAnim, CardPileCmd.Draw, CardSelectCmd.FromHand, CardCmd.AutoPlay | UpgradeValueBy(2m) | Regent | Draw 3 cards; Choose a card in your hand; Auto-play a card; keywords: Exhaust |
| DefendDefect | BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Defect | Gain 5 Block |
| DefendIronclad | BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Ironclad | Gain 5 Block |
| DefendNecrobinder | BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Necrobinder | Gain 5 Block |
| DefendRegent | BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Regent | Gain 5 Block |
| DefendSilent | BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Silent | Gain 5 Block |
| Defile | DamageVar(13m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | Necrobinder | Deal 13 damage; keywords: Ethereal |
| Deflect | BlockVar(4m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Silent | Gain 4 Block |
| Defragment | PowerVar<FocusPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<FocusPower> | UpgradeValueBy(1m) | Defect | Gain 1 Focus |
| Defy | BlockVar(6m, ValueProp.Move), PowerVar<WeakPower>(1m) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<WeakPower> | UpgradeValueBy(1m) | Necrobinder | Gain 6 Block; Apply 1 Weak to the target; keywords: Ethereal |
| Delay | BlockVar(11m, ValueProp.Move), EnergyVar(1) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Necrobinder | Gain 11 Block; Gain 1 Energy Next Turn |
| Demesne | EnergyVar(1), CardsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DemesnePower> |  | Necrobinder | Gain 1 Demesne; keywords: Ethereal |
| DemonForm | PowerVar<StrengthPower>(2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DemonFormPower> | UpgradeValueBy(1m) | Ironclad | Gain 2 Demon Form |
| DemonicShield | CalculationBaseVar(0m), HpLossVar(1m), CalculationExtraVar(1m), CalculatedBlockVar(ValueProp.Move) | VfxCmd.PlayOnCreatureCenter, CreatureCmd.Damage, CreatureCmd.GainBlock |  | Ironclad | Lose 1 HP; Give the target ? Block; keywords: Exhaust |
| DeprecatedCard |  |  |  | token | no OnPlay effect in source; keywords: Unplayable |
| Devastate | DamageVar(30m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(10m) | Regent | Deal 30 damage |
| DevourLife | PowerVar<DevourLifePower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DevourLifePower> | UpgradeValueBy(1m) | Necrobinder | Gain 1 Devour Life |
| Dirge | SummonVar(3m) | CreatureCmd.TriggerAnim, OstyCmd.Summon, CardCmd.Upgrade, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardsToCombat | UpgradeValueBy(1m) | Necrobinder | Summon 3; if upgraded: Upgrade a card; Add a generated card to your draw pile |
| Discovery |  | CardSelectCmd.FromChooseACardScreen, CardPileCmd.AddGeneratedCardToCombat |  | colorless | get Distinct For Combat; Choose a card from an offered set; Add a generated card to your hand; keywords: Exhaust |
| Disintegration | PowerVar<DisintegrationPower>(6m) |  |  | token | no OnPlay effect in source |
| Dismantle | DamageVar(8m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(2m) | Ironclad | Deal 8 damage ? times |
| Distraction |  | CardPileCmd.AddGeneratedCardToCombat |  | event | get Distinct For Combat; Add a generated card to your hand; keywords: Exhaust |
| DodgeAndRoll | BlockVar(4m, ValueProp.Move) | CreatureCmd.GainBlock, PowerCmd.Apply<BlockNextTurnPower> | UpgradeValueBy(2m) | Silent | Gain 4 Block; Gain ? Block Next Turn |
| Dominate | DynamicVar("StrengthPerVulnerable", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<StrengthPower> |  | Ironclad | Gain ? Strength; keywords: Exhaust |
| DoubleEnergy |  | PlayerCmd.GainEnergy |  | Defect | Gain ? Energy; keywords: Exhaust |
| Doubt | PowerVar<WeakPower>(1m) |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| DrainPower | DamageVar(10m, ValueProp.Move), CardsVar(2) | DamageCmd.Attack, CardCmd.Upgrade, CardCmd.Preview | UpgradeValueBy(2m), UpgradeValueBy(1m) | Necrobinder | Deal 10 damage; Upgrade a card |
| DramaticEntrance | DamageVar(11m, ValueProp.Move) | CreatureCmd.TriggerAnim, VfxCmd.PlayFullScreenInCombat, DamageCmd.Attack | UpgradeValueBy(4m) | colorless | Deal 11 damage to ALL enemies; keywords: Exhaust, Innate |
| Dredge | CardsVar(3) | CreatureCmd.TriggerAnim, CardPileCmd.Add, CardSelectCmd.FromSimpleGrid | AddKeyword(Retain) | Necrobinder | Put a card into your hand; keywords: Exhaust |
| DrumOfBattle | CardsVar(2), PowerVar<DrumOfBattlePower>(1m) | CardPileCmd.Draw, PowerCmd.Apply<DrumOfBattlePower> | UpgradeValueBy(1m) | Ironclad | Draw 2 cards; Gain 1 Drum Of Battle |
| Dualcast |  | CreatureCmd.TriggerAnim, OrbCmd.EvokeNext |  | Defect | Evoke your next orb |
| DualWield | CardsVar(1) | CardSelectCmd.FromHand, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(1m) | event | Choose a card in your hand; Add a generated card to your hand |
| DyingStar | DamageVar(9m, ValueProp.Move), DynamicVar("StrengthLoss", 9m) | CreatureCmd.TriggerAnim, DamageCmd.Attack, PowerCmd.Apply<DyingStarPower>, VfxCmd.PlayOnCreature | UpgradeValueBy(2m) | Regent | Deal 9 damage to ALL enemies; Apply 9 Dying Star to the target; keywords: Ethereal |
| EchoForm | DynamicVar("EchoForm", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<EchoFormPower> |  | Defect | Gain 1 Echo Form; keywords: Ethereal |
| EchoingSlash | DamageVar(10m, ValueProp.Move) | CreatureCmd.Damage | UpgradeValueBy(3m) | Silent | Deal 10 damage to the target |
| Eidolon |  | CreatureCmd.TriggerAnim, CardCmd.Exhaust, PowerCmd.Apply<IntangiblePower> |  | Necrobinder | Exhaust a card; Gain 1 Intangible |
| EndOfDays | PowerVar<DoomPower>(29m) | CreatureCmd.TriggerAnim, VfxCmd.GetSideCenterFloor, PowerCmd.Apply<DoomPower> | UpgradeValueBy(8m) | Necrobinder | Apply 29 Doom to the target |
| EnergySurge | EnergyVar(2) | PlayerCmd.GainEnergy | UpgradeValueBy(1m) | Defect | Gain 2 Energy; keywords: Exhaust |
| EnfeeblingTouch | DynamicVar("StrengthLoss", 8m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<EnfeeblingTouchPower> | UpgradeValueBy(3m) | Necrobinder | Apply 8 Enfeebling Touch to the target; keywords: Ethereal |
| Enlightenment |  |  |  | event | no gameplay effect detected in OnPlay; keywords: Exhaust |
| Enthralled |  |  |  | curse | no OnPlay effect in source; keywords: Eternal |
| Entrench |  | CreatureCmd.GainBlock |  | event | Gain ? Block |
| Entropy | CardsVar(1) | PowerCmd.Apply<EntropyPower> | AddKeyword(Innate) | colorless | Gain 1 Entropy |
| Envenom | PowerVar<EnvenomPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<EnvenomPower> | UpgradeValueBy(1m) | Silent | Gain 1 Envenom |
| Equilibrium | BlockVar(13m, ValueProp.Move), DynamicVar("Equilibrium", 1m) | CreatureCmd.GainBlock, PowerCmd.Apply<RetainHandPower> | UpgradeValueBy(3m) | colorless | Gain 13 Block; Gain 1 Retain Hand |
| Eradicate | DamageVar(11m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Necrobinder | Deal 11 damage ? times; keywords: Retain |
| EscapePlan | BlockVar(3m, ValueProp.Move) | CardPileCmd.Draw, CreatureCmd.GainBlock | UpgradeValueBy(2m) | Silent | Draw 1 card; Gain 3 Block |
| EternalArmor | PowerVar<PlatingPower>(7m) | PowerCmd.Apply<PlatingPower> | UpgradeValueBy(2m) | colorless | Gain 7 Plating |
| EvilEye | BlockVar(8m, ValueProp.Move) | CreatureCmd.TriggerAnim, VfxCmd.PlayOnCreatureCenter, CreatureCmd.GainBlock | UpgradeValueBy(3m) | Ironclad | Gain 8 Block |
| ExpectAFight | EnergyVar(0), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedEnergy") | CreatureCmd.TriggerAnim, PlayerCmd.GainEnergy |  | Ironclad | Gain ? Energy |
| Expertise | CardsVar(6) | CardPileCmd.Draw | UpgradeValueBy(1m) | Silent | Draw ? cards |
| Expose | DynamicVar("Power", 2m) | CreatureCmd.TriggerAnim, VfxCmd.PlayOnCreatureCenter, CreatureCmd.LoseBlock, PowerCmd.Remove<ArtifactPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(1m) | Silent | Lose all Block; Remove Artifact from the target; Apply 2 Vulnerable to the target; keywords: Exhaust |
| Exterminate | DamageVar(3m, ValueProp.Move), RepeatVar(4) | DamageCmd.Attack | UpgradeValueBy(1m) | event | Deal 3 damage 4 times to ALL enemies |
| FallingStar | DamageVar(7m, ValueProp.Move), PowerVar<VulnerablePower>(1m), PowerVar<WeakPower>(1m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(4m) | Regent | Deal 7 damage; Apply 1 Weak to the target; Apply 1 Vulnerable to the target |
| FanOfKnives | CardsVar("Shivs", 4) | PowerCmd.Apply<FanOfKnivesPower> | UpgradeValueBy(1m) | Silent | Gain 1 Fan Of Knives; Add 4 Shivs to your hand |
| Fasten | DynamicVar("ExtraBlock", 5m) | PowerCmd.Apply<FastenPower> | UpgradeValueBy(2m) | colorless | Gain 5 Fasten |
| Fear | DamageVar(7m, ValueProp.Move), PowerVar<VulnerablePower>(1m) | DamageCmd.Attack, CreatureCmd.TriggerAnim, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(1m) | Necrobinder | Deal 7 damage; Apply 1 Vulnerable to the target; keywords: Ethereal |
| Feed | DamageVar(10m, ValueProp.Move), MaxHpVar(3m) | DamageCmd.Attack, CreatureCmd.GainMaxHp | UpgradeValueBy(2m), UpgradeValueBy(1m) | Ironclad | Deal 10 damage; Gain 3 Max HP; keywords: Exhaust |
| FeedingFrenzy | PowerVar<StrengthPower>(5m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<FeedingFrenzyPower> | UpgradeValueBy(2m) | event | Gain 5 Feeding Frenzy |
| FeelNoPain | DynamicVar("Power", 3m) | PowerCmd.Apply<FeelNoPainPower> | UpgradeValueBy(1m) | Ironclad | Gain 3 Feel No Pain |
| Feral | PowerVar<FeralPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<FeralPower> |  | Defect | Gain 1 Feral |
| Fetch | OstyDamageVar(3m, ValueProp.Move), CardsVar(1) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(3m) | Necrobinder | Your Osty deals 3 damage; Draw 1 card |
| FiendFire | DamageVar(7m, ValueProp.Move) | CardCmd.Exhaust, DamageCmd.Attack, SfxCmd.Play | UpgradeValueBy(3m) | Ironclad | Exhaust a card; Deal 7 damage ? times; keywords: Exhaust |
| FightMe | DamageVar(5m, ValueProp.Move), RepeatVar(2), PowerVar<StrengthPower>(2m), DynamicVar("EnemyStrength", 1m) | DamageCmd.Attack, PowerCmd.Apply<StrengthPower> | UpgradeValueBy(1m) | Ironclad | Deal 5 damage 2 times; Gain 2 Strength; Apply 1 Strength to the target |
| FightThrough | BlockVar(13m, ValueProp.Move) | CreatureCmd.GainBlock, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(4m) | Defect | Gain 13 Block; Add a generated card to your discard pile |
| Finesse | BlockVar(4m, ValueProp.Move), CardsVar(1) | CreatureCmd.GainBlock, CardPileCmd.Draw | UpgradeValueBy(3m) | colorless | Gain 4 Block; Draw 1 card |
| Finisher | DamageVar(6m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(2m) | Silent | Deal 6 damage ? times |
| Fisticuffs | DamageVar(7m, ValueProp.Move) | DamageCmd.Attack, CreatureCmd.GainBlock | UpgradeValueBy(2m) | colorless | Deal 7 damage; Gain ? Block |
| FlakCannon | DamageVar(8m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | CardCmd.Exhaust, DamageCmd.Attack | UpgradeValueBy(3m) | Defect | Exhaust a card; Deal 8 damage ? times to a random enemy |
| FlameBarrier | BlockVar(12m, ValueProp.Move), DynamicVar("DamageBack", 4m) | CreatureCmd.GainBlock, PowerCmd.Apply<FlameBarrierPower> | UpgradeValueBy(4m), UpgradeValueBy(2m) | Ironclad | Gain 12 Block; Gain 4 Flame Barrier |
| Flanking |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<FlankingPower> |  | Silent | Apply 2 Flanking to the target |
| FlashOfSteel | DamageVar(5m, ValueProp.Move), CardsVar(1) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(3m) | colorless | Deal 5 damage; Draw 1 card |
| Flatten | OstyDamageVar(12m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | Necrobinder | Your Osty deals 12 damage |
| Flechettes | DamageVar(5m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(2m) | Silent | Deal 5 damage ? times |
| FlickFlack | DamageVar(7m, ValueProp.Move) | CreatureCmd.TriggerAnim, DamageCmd.Attack | UpgradeValueBy(2m) | Silent | Deal 7 damage to ALL enemies; keywords: Sly |
| FocusedStrike | DamageVar(9m, ValueProp.Move), PowerVar<FocusPower>(1m) | DamageCmd.Attack, PowerCmd.Apply<FocusedStrikePower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Defect | Deal 9 damage; Gain 1 Focused Strike |
| FollowThrough | DamageVar(6m, ValueProp.Move), PowerVar<WeakPower>(1m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Silent | Deal 6 damage to ALL enemies; Apply 1 Weak to the target |
| Folly |  |  |  | curse | no OnPlay effect in source; keywords: Unplayable, Eternal, Innate |
| Footwork | PowerVar<DexterityPower>(2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DexterityPower> | UpgradeValueBy(1m) | Silent | Gain 2 Dexterity |
| ForbiddenGrimoire |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<ForbiddenGrimoirePower> |  | Necrobinder | Gain 1 Forbidden Grimoire; keywords: Eternal |
| ForegoneConclusion | CardsVar(2) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ForegoneConclusionPower> | UpgradeValueBy(1m) | Regent | Gain 2 Foregone Conclusion |
| ForgottenRitual | EnergyVar(3) | CreatureCmd.TriggerAnim, PlayerCmd.GainEnergy | UpgradeValueBy(1m) | Ironclad | Gain 3 Energy |
| FranticEscape |  | PowerCmd.ModifyAmount |  | status | Increase a power already on the target |
| Friendship | PowerVar<StrengthPower>(2m), EnergyVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<StrengthPower>, PowerCmd.Apply<FriendshipPower> | UpgradeValueBy(-1m) | Necrobinder | Gain -2 Strength; Gain 1 Friendship |
| Ftl | DamageVar(5m, ValueProp.Move), IntVar("PlayMax", 3m), CardsVar(1) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(1m) | Defect | Deal 5 damage; Draw 1 card |
| Fuel | EnergyVar(1), CardsVar(1) | PlayerCmd.GainEnergy, CardPileCmd.Draw | UpgradeValueBy(1m) | token | Gain 1 Energy; Draw 1 card; keywords: Exhaust |
| Furnace | ForgeVar(4) | CreatureCmd.TriggerAnim, PowerCmd.Apply<FurnacePower> | UpgradeValueBy(2m) | Regent | Gain 4 Furnace |
| Fusion |  | CreatureCmd.TriggerAnim, OrbCmd.Channel<PlasmaOrb> |  | Defect | Channel a Plasma orb |
| GammaBlast | DamageVar(13m, ValueProp.Move), PowerVar<VulnerablePower>(2m), PowerVar<WeakPower>(2m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(5m) | Regent | Deal 13 damage; Apply 2 Weak to the target; Apply 2 Vulnerable to the target |
| GangUp | CalculationBaseVar(5m), ExtraDamageVar(5m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(2m) | colorless | Deal ? damage |
| GatherLight | BlockVar(7m, ValueProp.Move), StarsVar(1) | CreatureCmd.GainBlock, PlayerCmd.GainStars | UpgradeValueBy(3m) | Regent | Gain 7 Block; Gain 1 Stars |
| Genesis | DynamicVar("StarsPerTurn", 2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<GenesisPower> | UpgradeValueBy(1m) | Regent | Gain 2 Genesis |
| GeneticAlgorithm | BlockVar(CurrentBlock, ValueProp.Move), IntVar("Increase", 3m) | CreatureCmd.GainBlock | UpgradeValueBy(1m) | Defect | Gain ? Block; keywords: Exhaust |
| GiantRock | DamageVar(16m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | token | Deal 16 damage |
| Glacier | BlockVar(6m, ValueProp.Move) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, OrbCmd.Channel<FrostOrb> | UpgradeValueBy(3m) | Defect | Gain 6 Block; Channel a Frost orb |
| Glasswork | BlockVar(5m, ValueProp.Move) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, OrbCmd.Channel<GlassOrb> | UpgradeValueBy(3m) | Defect | Gain 5 Block; Channel a Glass orb |
| Glimmer | CardsVar(3), DynamicVar("PutBack", 1m) | CardPileCmd.Draw, CardSelectCmd.FromHand, CardPileCmd.Add | UpgradeValueBy(1m) | Regent | Draw 3 cards; Choose a card in your hand; Put a card into your draw pile |
| GlimpseBeyond | CardsVar(3) | CardPileCmd.AddGeneratedCardsToCombat, CardCmd.PreviewCardPileAdd | UpgradeValueBy(1m) | Necrobinder | Add a generated card to your draw pile; keywords: Exhaust |
| Glitterstream | BlockVar(11m, ValueProp.Move), BlockVar("BlockNextTurn", 4m, ValueProp.Move) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<BlockNextTurnPower> | UpgradeValueBy(2m) | Regent | Gain 11 Block; Gain ? Block Next Turn |
| Glow | StarsVar(1), CardsVar(2) | CreatureCmd.TriggerAnim, PlayerCmd.GainStars, CardPileCmd.Draw | UpgradeValueBy(1m) | Regent | Gain 1 Stars; Draw 2 cards |
| GoForTheEyes | DamageVar(3m, ValueProp.Move), PowerVar<WeakPower>(1m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower> | UpgradeValueBy(1m) | Defect | Deal 3 damage; Apply 1 Weak to the target |
| GoldAxe | CalculationBaseVar(0m), ExtraDamageVar(1m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | AddKeyword(Retain) | colorless | Deal ? damage |
| GrandFinale | DamageVar(50m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(10m) | Silent | Deal 50 damage to ALL enemies |
| Grapple | DamageVar(7m, ValueProp.Move), PowerVar<GrapplePower>(5m) | DamageCmd.Attack, PowerCmd.Apply<GrapplePower> | UpgradeValueBy(2m) | Ironclad | Deal 7 damage; Apply 5 Grapple to the target |
| Graveblast | DamageVar(4m, ValueProp.Move) | DamageCmd.Attack, CardSelectCmd.FromSimpleGrid, CardPileCmd.Add | UpgradeValueBy(2m) | Necrobinder | Deal 4 damage; Choose a card from a pile; Put a card into your hand; keywords: Exhaust |
| GraveWarden | BlockVar(8m, ValueProp.Move), CardsVar(1) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, CardCmd.Upgrade, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardsToCombat | UpgradeValueBy(2m) | Necrobinder | Gain 8 Block; if upgraded: Upgrade a card; Add a generated card to your draw pile |
| Greed |  |  |  | curse | no OnPlay effect in source; keywords: Eternal, Unplayable |
| Guards |  | CreatureCmd.TriggerAnim, CardSelectCmd.FromHand, CardCmd.Upgrade, CardCmd.Transform |  | Regent | Choose a card in your hand; if upgraded: Upgrade a card; Transform a card; keywords: Exhaust |
| GuidingStar | DamageVar(12m, ValueProp.Move), CardsVar(2) | CreatureCmd.TriggerAnim, SfxCmd.Play, DamageCmd.Attack, PowerCmd.Apply<DrawCardsNextTurnPower> | UpgradeValueBy(1m) | Regent | Deal 12 damage; Gain 2 Draw Cards Next Turn |
| Guilty | DynamicVar("Combats", 5m) |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| GunkUp | DamageVar(4m, ValueProp.Move), RepeatVar(3) | DamageCmd.Attack, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(1m) | Defect | Deal 4 damage 3 times; Add a generated card to your discard pile |
| Hailstorm | PowerVar<HailstormPower>(6m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<HailstormPower> | UpgradeValueBy(2m) | Defect | Gain 6 Hailstorm |
| HammerTime |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<HammerTimePower> |  | Regent | Gain 1 Hammer Time |
| HandOfGreed | DamageVar(20m, ValueProp.Move), DynamicVar("Gold", 20m) | DamageCmd.Attack, VfxCmd.PlayVfx, PlayerCmd.GainGold | UpgradeValueBy(5m) | colorless | Deal 20 damage; Gain 20 Gold |
| HandTrick | BlockVar(7m, ValueProp.Move) | CreatureCmd.GainBlock, CardSelectCmd.FromHand, CardCmd.ApplySingleTurnSly | UpgradeValueBy(3m) | Silent | Gain 7 Block; Choose a card in your hand; apply Single Turn Sly |
| Hang | DamageVar(10m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<HangPower> | UpgradeValueBy(3m) | Necrobinder | Deal 10 damage; Apply ? Hang to the target |
| Haunt | HpLossVar(6m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<HauntPower> | UpgradeValueBy(2m) | Necrobinder | Gain 6 Haunt |
| Havoc |  | CardPileCmd.AutoPlayFromDrawPile |  | Ironclad | Auto-play a card from your draw pile |
| Haze | PowerVar<PoisonPower>(4m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PoisonPower> | UpgradeValueBy(2m) | Silent | Apply 4 Poison to the target; keywords: Sly |
| Headbutt | DamageVar(9m, ValueProp.Move) | DamageCmd.Attack, CardSelectCmd.FromSimpleGrid, CardPileCmd.Add | UpgradeValueBy(3m) | Ironclad | Deal 9 damage; Choose a card from a pile; Put a card into your draw pile |
| HeavenlyDrill | DamageVar(8m, ValueProp.Move), EnergyVar(4) | DamageCmd.Attack | UpgradeValueBy(2m) | Regent | Deal 8 damage X (energy spent) times |
| Hegemony | DamageVar(15m, ValueProp.Move), EnergyVar(2) | DamageCmd.Attack, PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(3m), UpgradeValueBy(1m) | Regent | Deal 15 damage; Gain 2 Energy Next Turn |
| HeirloomHammer | DamageVar(17m, ValueProp.Move), RepeatVar(1) | DamageCmd.Attack, CardSelectCmd.FromHand, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(5m) | Regent | Deal 17 damage; Choose a card in your hand; Add a generated card to your hand |
| HelixDrill | DamageVar(3m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(2m) | Defect | Deal 3 damage ? times |
| HelloWorld |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<HelloWorldPower> | AddKeyword(Innate) | event | Gain 1 Hello World |
| Hellraiser |  | PowerCmd.Apply<HellraiserPower> |  | Ironclad | Gain 1 Hellraiser |
| Hemokinesis | HpLossVar(2m), DamageVar(14m, ValueProp.Move) | CreatureCmd.Damage, DamageCmd.Attack | UpgradeValueBy(5m) | Ironclad | Lose 2 HP; Deal 14 damage |
| HiddenCache | StarsVar(1), PowerVar<StarNextTurnPower>(3m) | CreatureCmd.TriggerAnim, PlayerCmd.GainStars, PowerCmd.Apply<StarNextTurnPower> | UpgradeValueBy(1m) | Regent | Gain 1 Stars; Gain 3 Star Next Turn |
| HiddenDaggers | CardsVar(2), DynamicVar("Shivs", 2m) | CardCmd.Discard, CardSelectCmd.FromHandForDiscard, CardCmd.Upgrade |  | Silent | Discard a card; Add 2 Shivs to your hand; Upgrade a card |
| HiddenGem | IntVar("Replay", 2m) | CreatureCmd.TriggerAnim, CardCmd.Preview | UpgradeValueBy(1m) | colorless | no gameplay effect detected in OnPlay |
| HighFive | OstyDamageVar(11m, ValueProp.Move), PowerVar<VulnerablePower>(2m) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Necrobinder | Your Osty deals 11 damage to ALL enemies; Apply 2 Vulnerable to the target |
| Hologram | BlockVar(3m, ValueProp.Move) | CreatureCmd.GainBlock, CardSelectCmd.FromSimpleGrid, CardPileCmd.Add | UpgradeValueBy(2m) | Defect | Gain 3 Block; Choose a card from a pile; Put a card into your hand; keywords: Exhaust |
| Hotfix | PowerVar<FocusPower>(2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<HotfixPower> | UpgradeValueBy(1m) | Defect | Gain 2 Hotfix |
| HowlFromBeyond | DamageVar(16m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(5m) | Ironclad | Deal 16 damage to ALL enemies |
| HuddleUp | CardsVar(2) | CardPileCmd.Draw | UpgradeValueBy(1m) | colorless | Draw 2 cards |
| Hyperbeam | DamageVar(26m, ValueProp.Move), PowerVar<FocusPower>(3m) | DamageCmd.Attack, PowerCmd.Apply<FocusPower> | UpgradeValueBy(8m) | Defect | Deal 26 damage to ALL enemies; Gain -3 Focus |
| IAmInvincible | BlockVar(9m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Regent | Gain 9 Block |
| IceLance | DamageVar(19m, ValueProp.Move), RepeatVar(3) | DamageCmd.Attack, OrbCmd.Channel<FrostOrb> | UpgradeValueBy(5m) | Defect | Deal 19 damage; Channel a Frost orb |
| Ignition |  | CreatureCmd.TriggerAnim, OrbCmd.Channel<PlasmaOrb> |  | Defect | Channel a Plasma orb; keywords: Exhaust |
| Impatience | CardsVar(2) | CardPileCmd.Draw | UpgradeValueBy(1m) | colorless | Draw 2 cards |
| Impervious | BlockVar(30m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(10m) | Ironclad | Gain 30 Block; keywords: Exhaust |
| Infection | DamageVar(3m, ValueProp.Unpowered \\| ValueProp.Move) |  |  | status | no OnPlay effect in source; keywords: Unplayable |
| InfernalBlade |  | CardPileCmd.AddGeneratedCardToCombat |  | Ironclad | get Distinct For Combat; Add a generated card to your hand; keywords: Exhaust |
| Inferno | PowerVar<InfernoPower>(6m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<InfernoPower> | UpgradeValueBy(3m) | Ironclad | Gain 6 Inferno |
| InfiniteBlades |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<InfiniteBladesPower> | AddKeyword(Innate) | Silent | Gain 1 Infinite Blades |
| Inflame | PowerVar<StrengthPower>(2m) | PowerCmd.Apply<StrengthPower> | UpgradeValueBy(1m) | Ironclad | Gain 2 Strength |
| Injury |  |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| Intercept | BlockVar(9m, ValueProp.Move) | CreatureCmd.GainBlock, PowerCmd.Apply<CoveredPower> | UpgradeValueBy(4m) | colorless | Gain 9 Block; Apply 1 Covered to the target |
| Invoke | SummonVar(2m), EnergyVar(2) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SummonNextTurnPower>, PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(1m) | Necrobinder | Gain 2 Summon Next Turn; Gain 2 Energy Next Turn |
| IronWave | DamageVar(5m, ValueProp.Move), BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock, DamageCmd.Attack | UpgradeValueBy(2m) | Ironclad | Gain 5 Block; Deal 5 damage |
| Iteration | PowerVar<IterationPower>(2m) | PowerCmd.Apply<IterationPower> | UpgradeValueBy(1m) | Defect | Gain 2 Iteration |
| JackOfAllTrades | CardsVar(1) | CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(1m) | colorless | get Distinct For Combat; Add a generated card to your hand; keywords: Exhaust |
| Jackpot | DamageVar(25m, ValueProp.Move), CardsVar(3) | DamageCmd.Attack, CardCmd.Upgrade, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(5m) | colorless | Deal 25 damage; get For Combat; if upgraded: Upgrade a card; Add a generated card to your hand |
| Juggernaut | PowerVar<JuggernautPower>(5m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<JuggernautPower> | UpgradeValueBy(2m) | Ironclad | Gain 5 Juggernaut |
| Juggling |  | PowerCmd.Apply<JugglingPower> | AddKeyword(Innate) | Ironclad | Gain 1 Juggling |
| KinglyKick | DamageVar(24m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(6m) | Regent | Deal 24 damage |
| KinglyPunch | DamageVar(8m, ValueProp.Move), DynamicVar("Increase", 3m) | DamageCmd.Attack | UpgradeValueBy(2m) | Regent | Deal 8 damage |
| KnifeTrap | CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedShivs") | CardCmd.Upgrade, CardCmd.AutoPlay |  | Silent | if upgraded: Upgrade a card; Auto-play a card |
| Knockdown | DamageVar(10m, ValueProp.Move), PowerVar<KnockdownPower>(2m) | DamageCmd.Attack, PowerCmd.Apply<KnockdownPower> | UpgradeValueBy(4m), UpgradeValueBy(1m) | colorless | Deal 10 damage; Apply 2 Knockdown to the target |
| KnockoutBlow | DamageVar(30m, ValueProp.Move), StarsVar(5) | DamageCmd.Attack, PlayerCmd.GainStars | UpgradeValueBy(8m) | Regent | Deal 30 damage; Gain 5 Stars |
| KnowThyPlace | PowerVar<WeakPower>(1m), PowerVar<VulnerablePower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> |  | Regent | Apply 1 Weak to the target; Apply 1 Vulnerable to the target; keywords: Exhaust |
| LanternKey |  |  |  | quest | no OnPlay effect in source; keywords: Unplayable |
| Largesse |  | CreatureCmd.TriggerAnim, CardCmd.Upgrade, CardPileCmd.AddGeneratedCardToCombat |  | Regent | get Distinct For Combat; if upgraded: Upgrade a card; Add a generated card to your hand |
| LeadingStrike | CardsVar("Shivs", 1), DamageVar(7m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Silent | Deal 7 damage; Add 1 Shiv to your hand |
| Leap | BlockVar(9m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Defect | Gain 9 Block |
| LegionOfBone | SummonVar(6m) | CreatureCmd.TriggerAnim, OstyCmd.Summon | UpgradeValueBy(2m) | Necrobinder | Summon 6; keywords: Exhaust |
| LegSweep | BlockVar(11m, ValueProp.Move), PowerVar<WeakPower>(2m) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<WeakPower> | UpgradeValueBy(3m), UpgradeValueBy(1m) | Silent | Gain 11 Block; Apply 2 Weak to the target |
| Lethality | PowerVar<LethalityPower>(50m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<LethalityPower> | UpgradeValueBy(25m) | Necrobinder | Gain 50 Lethality; keywords: Ethereal |
| Lift | BlockVar(11m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(5m) | colorless | Give the target 11 Block |
| LightningRod | BlockVar(4m, ValueProp.Move), PowerVar<LightningRodPower>(2m) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<LightningRodPower> | UpgradeValueBy(3m) | Defect | Gain 4 Block; Gain 2 Lightning Rod |
| Loop | DynamicVar("Loop", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<LoopPower> | UpgradeValueBy(1m) | Defect | Gain 1 Loop |
| Luminesce | EnergyVar(2) | CreatureCmd.TriggerAnim, PlayerCmd.GainEnergy | UpgradeValueBy(1m) | token | Gain 2 Energy; keywords: Exhaust, Retain |
| LunarBlast | DamageVar(4m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(1m) | Regent | Deal 4 damage ? times |
| MachineLearning | CardsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<MachineLearningPower> | AddKeyword(Innate) | Defect | Gain 1 Machine Learning |
| MadScience | DamageVar(12m, ValueProp.Move), BlockVar(8m, ValueProp.Move), PowerVar<WeakPower>("SappingWeak", 2m), PowerVar<VulnerablePower>("SappingVulnerable", 2m), DynamicVar("ViolenceHits", 3m), PowerVar<StranglePower>("ChokingDamage", 6m), EnergyVar("EnergizedEnergy", 2), CardsVar("WisdomCards", 3), PowerVar<StrengthPower>("ExpertiseStrength", 2m), PowerVar<DexterityPower>("ExpertiseDexterity", 2m), DynamicVar("CuriousReduction", 1m) |  | AddKeyword(Innate) | event | no gameplay effect detected in OnPlay |
| MakeItSo | DamageVar(6m, ValueProp.Move), CardsVar(3) | DamageCmd.Attack | UpgradeValueBy(3m) | Regent | Deal 6 damage |
| Malaise |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<StrengthPower>, PowerCmd.Apply<WeakPower> |  | Silent | Apply -X (energy spent) Strength to the target; Apply X (energy spent) Weak to the target; keywords: Exhaust |
| Mangle | DamageVar(15m, ValueProp.Move), DynamicVar("StrengthLoss", 10m) | DamageCmd.Attack, PowerCmd.Apply<ManglePower> | UpgradeValueBy(5m) | Ironclad | Deal 15 damage; Apply 10 Mangle to the target |
| ManifestAuthority | BlockVar(7m, ValueProp.Move) | CreatureCmd.GainBlock, CardCmd.Upgrade, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(1m) | Regent | Gain 7 Block; get Distinct For Combat; if upgraded: Upgrade a card; Add a generated card to your hand |
| MasterOfStrategy | CardsVar(3) | CardPileCmd.Draw | UpgradeValueBy(1m) | colorless | Draw 3 cards; keywords: Exhaust |
| MasterPlanner |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<MasterPlannerPower> |  | Silent | Gain 1 Master Planner |
| Maul | DamageVar(5m, ValueProp.Move), DynamicVar("Increase", 1m) | DamageCmd.Attack | UpgradeValueBy(1m) | event | Deal 5 damage 2 times |
| Mayhem |  | PowerCmd.Apply<MayhemPower> |  | colorless | Gain 1 Mayhem |
| Melancholy | BlockVar(13m, ValueProp.Move), EnergyVar(1) | CreatureCmd.GainBlock | UpgradeValueBy(4m) | Necrobinder | Gain 13 Block |
| MementoMori | CalculationBaseVar(8m), ExtraDamageVar(4m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(2m), UpgradeValueBy(1m) | Silent | Deal ? damage |
| Metamorphosis | CardsVar(3) | CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(2m) | event | get For Combat; Add a generated card to your draw pile; keywords: Exhaust |
| MeteorShower | DamageVar(14m, ValueProp.Move), PowerVar<VulnerablePower>(2m), PowerVar<WeakPower>(2m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(7m) | Regent | Deal 14 damage to ALL enemies; Apply 2 Weak to the target; Apply 2 Vulnerable to the target |
| MeteorStrike | DamageVar(24m, ValueProp.Move) | DamageCmd.Attack, OrbCmd.Channel<PlasmaOrb> | UpgradeValueBy(6m) | Defect | Deal 24 damage; Channel a Plasma orb |
| Mimic | CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedBlockVar(ValueProp.Move) | CreatureCmd.GainBlock |  | colorless | Gain ? Block; keywords: Exhaust |
| MindBlast | CalculationBaseVar(0m), ExtraDamageVar(1m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack |  | colorless | Deal ? damage; keywords: Innate |
| MindRot | PowerVar<MindRotPower>(1m) |  |  | token | no OnPlay effect in source |
| MinionDiveBomb | DamageVar(13m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | token | Deal 13 damage; keywords: Exhaust |
| MinionSacrifice | BlockVar(9m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | token | Gain 9 Block; keywords: Exhaust |
| MinionStrike | DamageVar(7m, ValueProp.Move), CardsVar(1) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(3m) | token | Deal 7 damage; Draw 1 card; keywords: Exhaust |
| Mirage | CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedBlockVar(ValueProp.Move) | CreatureCmd.GainBlock |  | Silent | Gain ? Block; keywords: Exhaust |
| Misery | DamageVar(7m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.ModifyAmount, PowerCmd.Apply | UpgradeValueBy(2m), AddKeyword(Retain) | Necrobinder | Deal 7 damage; Increase a power already on the target; Apply a power to the target |
| Modded | RepeatVar(1), CardsVar(1) | CreatureCmd.TriggerAnim, OrbCmd.AddSlots, CardPileCmd.Draw | UpgradeValueBy(1m) | Defect | Add 1 orb slot(s); Draw 1 card |
| MoltenFist | DamageVar(10m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(4m) | Ironclad | Deal 10 damage; Apply ? Vulnerable to the target; keywords: Exhaust |
| MomentumStrike | DamageVar(10m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Defect | Deal 10 damage |
| MonarchsGaze | DynamicVar("StrengthLoss", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<MonarchsGazePower> |  | Regent | Gain 1 Monarchs Gaze |
| Monologue | DynamicVar("Power", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<MonologuePower> | AddKeyword(Retain) | Regent | Gain 1 Monologue |
| MultiCast |  | CreatureCmd.TriggerAnim, OrbCmd.EvokeNext |  | Defect | Evoke your next orb |
| Murder | CalculationBaseVar(1m), ExtraDamageVar(1m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack |  | Silent | Deal ? damage |
| NecroMastery | SummonVar(5m) | CreatureCmd.TriggerAnim, OstyCmd.Summon, PowerCmd.Apply<NecroMasteryPower> | UpgradeValueBy(3m) | Necrobinder | Summon 5; Gain 1 Necro Mastery |
| NegativePulse | BlockVar(5m, ValueProp.Move), PowerVar<DoomPower>(7m) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<DoomPower> | UpgradeValueBy(1m), UpgradeValueBy(4m) | Necrobinder | Gain 5 Block; Apply 7 Doom to the target |
| NeowsFury | DamageVar(10m, ValueProp.Move), CardsVar(2) | DamageCmd.Attack, CardPileCmd.Add | UpgradeValueBy(4m) | event | Deal 10 damage; Put a card into your hand; keywords: Exhaust |
| Neurosurge | PowerVar<NeurosurgePower>(3m), EnergyVar(3), CardsVar(2) | CreatureCmd.TriggerAnim, PlayerCmd.GainEnergy, CardPileCmd.Draw, PowerCmd.Apply<NeurosurgePower> | UpgradeValueBy(1m) | Necrobinder | Gain 3 Energy; Draw 2 cards; Gain 3 Neurosurge |
| Neutralize | DamageVar(3m, ValueProp.Move), PowerVar<WeakPower>(1m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower> | UpgradeValueBy(1m) | Silent | Deal 3 damage; Apply 1 Weak to the target |
| NeutronAegis | PowerVar<PlatingPower>(8m) | PowerCmd.Apply<PlatingPower> | UpgradeValueBy(3m) | Regent | Gain 8 Plating |
| Nightmare |  | CreatureCmd.TriggerAnim, CardSelectCmd.FromHand, PowerCmd.Apply<NightmarePower> |  | Silent | Choose a card in your hand; Gain 3 Nightmare; keywords: Exhaust |
| NoEscape | DynamicVar("DoomThreshold", 10m), CalculationBaseVar(10m), CalculationExtraVar(5m), CalculatedVar("CalculatedDoom") | PowerCmd.Apply<DoomPower> | UpgradeValueBy(5m) | Necrobinder | Apply ? Doom to the target |
| Normality | CalculationBaseVar(3m), CalculationExtraVar(-1m), CalculatedVar("CalculatedCards") |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| Nostalgia |  | PowerCmd.Apply<NostalgiaPower> |  | colorless | Gain 1 Nostalgia |
| NoxiousFumes | DynamicVar("PoisonPerTurn", 2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<NoxiousFumesPower> | UpgradeValueBy(1m) | Silent | Gain 2 Noxious Fumes |
| Null | DamageVar(10m, ValueProp.Move), PowerVar<WeakPower>(2m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower>, OrbCmd.Channel<DarkOrb> | UpgradeValueBy(3m), UpgradeValueBy(1m) | Defect | Deal 10 damage; Apply 2 Weak to the target; Channel a Dark orb |
| Oblivion | PowerVar<DoomPower>(3m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<OblivionPower> | UpgradeValueBy(1m) | Necrobinder | Apply 3 Oblivion to the target |
| Offering | HpLossVar(6m), EnergyVar(2), CardsVar(3) | CreatureCmd.Damage, PlayerCmd.GainEnergy, CardPileCmd.Draw | UpgradeValueBy(2m) | Ironclad | Lose 6 HP; Gain 2 Energy; Draw 3 cards; keywords: Exhaust |
| Omnislice | DamageVar(8m, ValueProp.Move) | CreatureCmd.Damage | UpgradeValueBy(3m) | colorless | Deal 8 damage to the target; Deal ? damage to the target |
| OneTwoPunch | DynamicVar("Attacks", 1m) | PowerCmd.Apply<OneTwoPunchPower> | UpgradeValueBy(1m) | Ironclad | Gain 1 One Two Punch |
| Orbit | EnergyVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<OrbitPower> |  | Regent | Gain 1 Orbit |
| Outbreak | PowerVar<OutbreakPower>(11m), RepeatVar(3) | PowerCmd.Apply<OutbreakPower> | UpgradeValueBy(4m) | Silent | Gain 11 Outbreak |
| Outmaneuver | EnergyVar(2) | PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(1m) | event | Gain 2 Energy Next Turn |
| Overclock | CardsVar(2) | CreatureCmd.TriggerAnim, CardPileCmd.Draw, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(1m) | Defect | Draw 2 cards; Add a generated card to your discard pile |
| PactsEnd | DamageVar(17m, ValueProp.Move), CardsVar(3) | DamageCmd.Attack | UpgradeValueBy(6m) | Ironclad | Deal 17 damage to ALL enemies |
| Pagestorm | CardsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PagestormPower> |  | Necrobinder | Gain 1 Pagestorm |
| PaleBlueDot | CardsVar(1), DynamicVar("CardPlay", 5m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PaleBlueDotPower> | UpgradeValueBy(1m) | Regent | Gain 1 Pale Blue Dot |
| Panache | DynamicVar("PanacheDamage", 10m) | PowerCmd.Apply<PanachePower> | UpgradeValueBy(4m) | colorless | Gain 10 Panache |
| PanicButton | BlockVar(30m, ValueProp.Move), DynamicVar("Turns", 2m) | CreatureCmd.GainBlock, PowerCmd.Apply<NoBlockPower> | UpgradeValueBy(10m) | colorless | Gain 30 Block; Gain 2 No Block; keywords: Exhaust |
| Parry | PowerVar<ParryPower>(6m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ParryPower> | UpgradeValueBy(3m) | Regent | Gain 6 Parry |
| Parse | CardsVar(3) | CardPileCmd.Draw | UpgradeValueBy(1m) | Necrobinder | Draw 3 cards; keywords: Ethereal |
| ParticleWall | BlockVar(9m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Regent | Gain 9 Block |
| Patter | BlockVar(8m, ValueProp.Move), PowerVar<VigorPower>(2m) | CreatureCmd.GainBlock, CreatureCmd.TriggerAnim, PowerCmd.Apply<VigorPower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Regent | Gain 8 Block; Gain 2 Vigor |
| Peck | DamageVar(2m, ValueProp.Move), RepeatVar(3) | DamageCmd.Attack | UpgradeValueBy(1m) | event | Deal 2 damage 3 times |
| PerfectedStrike | CalculationBaseVar(6m), ExtraDamageVar(2m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Ironclad | Deal ? damage |
| PhantomBlades | PowerVar<PhantomBladesPower>(9m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PhantomBladesPower> | UpgradeValueBy(3m) | Silent | Gain 9 Phantom Blades |
| PhotonCut | DamageVar(10m, ValueProp.Move), CardsVar(1), DynamicVar("PutBack", 1m) | DamageCmd.Attack, CardPileCmd.Draw, CardPileCmd.Add, CardSelectCmd.FromHand | UpgradeValueBy(3m), UpgradeValueBy(1m) | Regent | Deal 10 damage; Draw 1 card; Put a card into your draw pile |
| PiercingWail | DynamicVar("StrengthLoss", 6m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PiercingWailPower> | UpgradeValueBy(2m) | Silent | Apply 6 Piercing Wail to the target; keywords: Exhaust |
| Pillage | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(3m) | Ironclad | Deal 6 damage; Draw 1 card |
| PillarOfCreation | BlockVar(3m, ValueProp.Unpowered) | PowerCmd.Apply<PillarOfCreationPower> | UpgradeValueBy(1m) | Regent | Gain 3 Pillar Of Creation |
| Pinpoint | DamageVar(17m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(5m) | Silent | Deal 17 damage |
| PoisonedStab | DamageVar(6m, ValueProp.Move), PowerVar<PoisonPower>(3m) | DamageCmd.Attack, PowerCmd.Apply<PoisonPower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Silent | Deal 6 damage; Apply 3 Poison to the target |
| Poke | OstyDamageVar(6m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Necrobinder | Your Osty deals 6 damage |
| PommelStrike | DamageVar(9m, ValueProp.Move), CardsVar(1) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(1m) | Ironclad | Deal 9 damage; Draw 1 card |
| PoorSleep |  |  |  | curse | no OnPlay effect in source; keywords: Unplayable, Retain |
| Pounce | DamageVar(12m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<FreeSkillPower> | UpgradeValueBy(6m) | Silent | Deal 12 damage; Gain 1 Free Skill |
| PreciseCut | CalculationBaseVar(13m), ExtraDamageVar(2m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Silent | Deal ? damage |
| Predator | DamageVar(15m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<DrawCardsNextTurnPower> | UpgradeValueBy(5m) | Silent | Deal 15 damage; Gain 2 Draw Cards Next Turn |
| Prepared | CardsVar(1) | CardPileCmd.Draw, CardCmd.Discard, CardSelectCmd.FromHandForDiscard | UpgradeValueBy(1m) | Silent | Draw 1 card; Discard a card |
| PrepTime | PowerVar<PrepTimePower>(4m) | PowerCmd.Apply<PrepTimePower> | UpgradeValueBy(2m) | colorless | Gain 4 Prep Time |
| PrimalForce |  | CreatureCmd.TriggerAnim, CardCmd.Upgrade, CardCmd.Transform |  | Ironclad | if upgraded: Upgrade a card; Transform a card |
| Production | EnergyVar(2) | PlayerCmd.GainEnergy |  | colorless | Gain 2 Energy; keywords: Exhaust |
| Prolong |  | PowerCmd.Apply<BlockNextTurnPower> |  | colorless | Apply ? Block Next Turn to the target; keywords: Exhaust |
| Prophesize | CardsVar(6) | CreatureCmd.TriggerAnim, CardPileCmd.Draw | UpgradeValueBy(3m) | Regent | Draw 6 cards |
| Protector | CalculationBaseVar(10m), ExtraDamageVar(1m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(5m) | Necrobinder | Deal ? damage |
| Prowess | PowerVar<StrengthPower>(1m), PowerVar<DexterityPower>(1m) | PowerCmd.Apply<StrengthPower>, PowerCmd.Apply<DexterityPower> | UpgradeValueBy(1m) | colorless | Gain 1 Strength; Gain 1 Dexterity |
| PullAggro | SummonVar(4m), BlockVar(7m, ValueProp.Move) | CreatureCmd.TriggerAnim, OstyCmd.Summon, CreatureCmd.GainBlock | UpgradeValueBy(1m), UpgradeValueBy(2m) | Necrobinder | Summon 4; Gain 7 Block |
| PullFromBelow | DamageVar(5m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(2m) | Necrobinder | Deal 5 damage ? times |
| Purity | CardsVar(3) | CardSelectCmd.FromHand, CardCmd.Exhaust | UpgradeValueBy(2m) | colorless | Choose a card in your hand; Exhaust a card; keywords: Retain, Exhaust |
| Putrefy | DynamicVar("Power", 2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(1m) | Necrobinder | Apply 2 Weak to the target; Apply 2 Vulnerable to the target; keywords: Exhaust |
| Pyre | EnergyVar(1) | PowerCmd.Apply<PyrePower> | UpgradeValueBy(1m) | Ironclad | Gain 1 Pyre |
| Quadcast | RepeatVar(4) | CreatureCmd.TriggerAnim, OrbCmd.EvokeNext |  | Defect | Evoke your next orb |
| Quasar |  | CardCmd.Upgrade, CardSelectCmd.FromChooseACardScreen, CardPileCmd.AddGeneratedCardToCombat |  | Regent | get Distinct For Combat; if upgraded: Upgrade a card; Choose a card from an offered set; Add a generated card to your hand |
| Radiate | DamageVar(3m, ValueProp.Move), StarsVar(1), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(1m) | Regent | Deal 3 damage ? times to ALL enemies |
| Rage | DynamicVar("Power", 3m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<RagePower> | UpgradeValueBy(2m) | Ironclad | Gain 3 Rage |
| Rainbow |  | CreatureCmd.TriggerAnim, OrbCmd.Channel<LightningOrb>, OrbCmd.Channel<FrostOrb>, OrbCmd.Channel<DarkOrb> |  | Defect | Channel a Lightning orb; Channel a Frost orb; Channel a Dark orb; keywords: Exhaust |
| Rally | BlockVar(12m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(5m) | colorless | Give 12 Block |
| Rampage | DamageVar(9m, ValueProp.Move), DynamicVar("Increase", 5m) | DamageCmd.Attack | UpgradeValueBy(4m) | Ironclad | Deal 9 damage |
| Rattle | OstyDamageVar(7m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(2m) | Necrobinder | Your Osty deals 7 damage ? times |
| Reanimate | SummonVar(20m) | CreatureCmd.TriggerAnim, OstyCmd.Summon | UpgradeValueBy(5m) | Necrobinder | Summon 20; keywords: Exhaust |
| Reap | DamageVar(27m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(6m) | Necrobinder | Deal 27 damage; keywords: Retain |
| ReaperForm |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<ReaperFormPower> | AddKeyword(Retain) | Necrobinder | Gain 1 Reaper Form |
| Reave | DamageVar(9m, ValueProp.Move), CardsVar(1) | DamageCmd.Attack, CardCmd.Upgrade, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardsToCombat | UpgradeValueBy(2m) | Necrobinder | Deal 9 damage; if upgraded: Upgrade a card; Add a generated card to your draw pile |
| Reboot | CardsVar(4) | CreatureCmd.TriggerAnim, CardPileCmd.Add, CardPileCmd.Shuffle, CardPileCmd.Draw | UpgradeValueBy(2m) | Defect | Put a card into your draw pile; Shuffle your draw pile; Draw 4 cards; keywords: Exhaust |
| Rebound | DamageVar(9m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<ReboundPower> | UpgradeValueBy(3m) | event | Deal 9 damage; Gain 1 Rebound |
| RefineBlade | ForgeVar(6), EnergyVar(1) | CreatureCmd.TriggerAnim, ForgeCmd.Forge, PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(4m) | Regent | Forge 6; Gain 1 Energy Next Turn |
| Reflect | BlockVar(17m, ValueProp.Move) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<ReflectPower> | UpgradeValueBy(4m) | Regent | Gain 17 Block; Gain 1 Reflect |
| Reflex | CardsVar(2) | CreatureCmd.TriggerAnim, CardPileCmd.Draw | UpgradeValueBy(1m) | Silent | Draw 2 cards; keywords: Sly |
| Refract | RepeatVar(2), DamageVar(9m, ValueProp.Move) | DamageCmd.Attack, OrbCmd.Channel<GlassOrb> | UpgradeValueBy(3m) | Defect | Deal 9 damage 2 times; Channel a Glass orb |
| Regret |  |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| Relax | BlockVar(15m, ValueProp.Move), CardsVar(2), EnergyVar(2) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<DrawCardsNextTurnPower>, PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | event | Gain 15 Block; Gain 2 Draw Cards Next Turn; Gain 2 Energy Next Turn; keywords: Exhaust |
| Rend | CalculationBaseVar(15m), ExtraDamageVar(5m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | colorless | Deal ? damage |
| Resonance | PowerVar<StrengthPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<StrengthPower> | UpgradeValueBy(1m) | Regent | Gain 1 Strength; Apply -1 Strength to the target |
| Restlessness | CardsVar(2), EnergyVar(2) | CardPileCmd.Draw, PlayerCmd.GainEnergy | UpgradeValueBy(1m) | colorless | Draw 1 card; Gain 2 Energy; keywords: Retain |
| Ricochet | DamageVar(3m, ValueProp.Move), RepeatVar(4) | DamageCmd.Attack | UpgradeValueBy(1m) | Silent | Deal 3 damage 4 times to a random enemy; keywords: Sly |
| RightHandHand | OstyDamageVar(4m, ValueProp.Move), EnergyVar(2) | DamageCmd.Attack | UpgradeValueBy(2m) | Necrobinder | Your Osty deals 4 damage |
| RipAndTear | DamageVar(7m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(2m) | event | Deal 7 damage 2 times to a random enemy |
| RocketPunch | DamageVar(13m, ValueProp.Move), CardsVar(1) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(1m) | Defect | Deal 13 damage; Draw 1 card |
| RollingBoulder | PowerVar<RollingBoulderPower>(5m), DynamicVar("IncrementAmount", 5m) | PowerCmd.Apply<RollingBoulderPower> | UpgradeValueBy(5m) | colorless | Gain 5 Rolling Boulder |
| RoyalGamble | StarsVar(9) | CreatureCmd.TriggerAnim, PlayerCmd.GainStars | AddKeyword(Retain) | Regent | Gain 9 Stars; keywords: Exhaust |
| Royalties | GoldVar(30) | CreatureCmd.TriggerAnim, PowerCmd.Apply<RoyaltiesPower> | UpgradeValueBy(5m) | Regent | Gain 30 Royalties |
| Rupture | PowerVar<StrengthPower>(1m) | PowerCmd.Apply<RupturePower> | UpgradeValueBy(1m) | Ironclad | Gain 1 Rupture |
| Sacrifice |  | CreatureCmd.TriggerAnim, CreatureCmd.Kill, CreatureCmd.GainBlock |  | Necrobinder | Kill your own Osty; Gain ? Block; keywords: Retain |
| Salvo | DamageVar(12m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<RetainHandPower> | UpgradeValueBy(4m) | colorless | Deal 12 damage; Gain 1 Retain Hand |
| Scavenge | EnergyVar(2) | CardSelectCmd.FromHand, CardCmd.Exhaust, PowerCmd.Apply<EnergyNextTurnPower> | UpgradeValueBy(1m) | Defect | Choose a card in your hand; Exhaust a card; Gain 2 Energy Next Turn |
| Scourge | PowerVar<DoomPower>(13m), CardsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<DoomPower>, CardPileCmd.Draw | UpgradeValueBy(3m), UpgradeValueBy(1m) | Necrobinder | Apply 13 Doom to the target; Draw 1 card |
| Scrape | DamageVar(7m, ValueProp.Move), CardsVar(4) | DamageCmd.Attack, CardPileCmd.Draw, CardCmd.Discard | UpgradeValueBy(3m), UpgradeValueBy(1m) | Defect | Deal 7 damage; Draw 4 cards; Discard a card |
| Scrawl |  | CardPileCmd.Draw | AddKeyword(Retain) | colorless | Draw ? cards; keywords: Exhaust |
| SculptingStrike | DamageVar(8m, ValueProp.Move) | DamageCmd.Attack, CardSelectCmd.FromHand, CardCmd.ApplyKeyword | UpgradeValueBy(3m) | Necrobinder | Deal 8 damage; Choose a card in your hand; Give a card Ethereal |
| Seance | CardsVar(1) | CreatureCmd.TriggerAnim, CardSelectCmd.FromSimpleGrid, CardCmd.TransformTo<Soul>, CardCmd.Upgrade |  | Necrobinder | Choose a card from a pile; Transform a card into Soul; Upgrade a card; keywords: Ethereal |
| SecondWind | BlockVar(5m, ValueProp.Move) | CreatureCmd.TriggerAnim, CardCmd.Exhaust, CreatureCmd.GainBlock | UpgradeValueBy(2m) | Ironclad | Exhaust a card; Gain 5 Block |
| SecretTechnique |  | CardSelectCmd.FromSimpleGrid, CardPileCmd.Add |  | colorless | Choose a card from a pile; Put a card into your hand; keywords: Exhaust |
| SecretWeapon |  | CardSelectCmd.FromSimpleGrid, CardPileCmd.Add |  | colorless | Choose a card from a pile; Put a card into your hand; keywords: Exhaust |
| SeekerStrike | DamageVar(6m, ValueProp.Move), CardsVar(3) | DamageCmd.Attack, CardSelectCmd.FromSimpleGrid, CardPileCmd.Add | UpgradeValueBy(3m) | colorless | Deal 6 damage; Choose a card from a pile; Put a card into your hand |
| SeekingEdge | ForgeVar(7) | CreatureCmd.TriggerAnim, ForgeCmd.Forge, PowerCmd.Apply<SeekingEdgePower> | UpgradeValueBy(4m) | Regent | Forge 7; Gain 1 Seeking Edge |
| SentryMode | PowerVar<SentryModePower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SentryModePower> |  | Necrobinder | Gain 1 Sentry Mode |
| SerpentForm | PowerVar<SerpentFormPower>(4m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SerpentFormPower> | UpgradeValueBy(1m) | Silent | Gain 4 Serpent Form |
| SetupStrike | DamageVar(7m, ValueProp.Move), PowerVar<StrengthPower>(2m) | DamageCmd.Attack, PowerCmd.Apply<SetupStrikePower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Ironclad | Deal 7 damage; Gain 2 Setup Strike |
| SevenStars | DamageVar(7m, ValueProp.Move), RepeatVar(7) | DamageCmd.Attack |  | Regent | Deal 7 damage 7 times to ALL enemies |
| Severance | DamageVar(13m, ValueProp.Move) | DamageCmd.Attack, CardPileCmd.AddGeneratedCardToCombat, CardCmd.PreviewCardPileAdd | UpgradeValueBy(5m) | Necrobinder | Deal 13 damage; Add a generated card to your draw pile; Add a generated card to your discard pile; Add a generated card to your hand |
| Shadowmeld | DynamicVar("Power", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ShadowmeldPower> |  | Silent | Gain 1 Shadowmeld |
| ShadowShield | BlockVar(11m, ValueProp.Move) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, OrbCmd.Channel<DarkOrb> | UpgradeValueBy(4m) | Defect | Gain 11 Block; Channel a Dark orb |
| ShadowStep | CardsVar(3) | CardCmd.Discard, PowerCmd.Apply<ShadowStepPower> |  | Silent | Discard a card; Gain 1 Shadow Step |
| Shame | DynamicVar("Frail", 1m) |  |  | curse | no OnPlay effect in source; keywords: Unplayable |
| SharedFate | DynamicVar("EnemyStrengthLoss", 2m), DynamicVar("PlayerStrengthLoss", 2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<StrengthPower> | UpgradeValueBy(1m) | Necrobinder | Gain -2 Strength; Apply -2 Strength to the target; keywords: Exhaust |
| Shatter | DamageVar(11m, ValueProp.Move) | DamageCmd.Attack, OrbCmd.EvokeNext | UpgradeValueBy(4m) | Defect | Deal 11 damage to ALL enemies; Evoke your next orb |
| ShiningStrike | DamageVar(8m, ValueProp.Move), StarsVar(2) | DamageCmd.Attack, PlayerCmd.GainStars, CardPileCmd.Add | UpgradeValueBy(3m) | Regent | Deal 8 damage; Gain 2 Stars; Put a card into your draw pile |
| Shiv | DamageVar(4m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("FanOfKnivesAmount") | DamageCmd.Attack | UpgradeValueBy(2m) | token | Deal 4 damage; keywords: Exhaust |
| Shockwave | DynamicVar("Power", 3m) | CreatureCmd.TriggerAnim, VfxCmd.PlayOnCreatureCenter, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(2m) | colorless | Apply 3 Weak to the target; Apply 3 Vulnerable to the target; keywords: Exhaust |
| Shroud | BlockVar(2m, ValueProp.Unpowered) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ShroudPower> | UpgradeValueBy(1m) | Necrobinder | Gain 2 Shroud |
| ShrugItOff | BlockVar(8m, ValueProp.Move), CardsVar(1) | CreatureCmd.GainBlock, CardPileCmd.Draw | UpgradeValueBy(3m) | Ironclad | Gain 8 Block; Draw 1 card |
| SicEm | OstyDamageVar(5m, ValueProp.Move), PowerVar<SicEmPower>(2m) | DamageCmd.Attack, PowerCmd.Apply<SicEmPower> | UpgradeValueBy(1m) | Necrobinder | Your Osty deals 5 damage; Apply 2 Sic Em to the target |
| SignalBoost | PowerVar<SignalBoostPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SignalBoostPower> |  | Defect | Gain 1 Signal Boost; keywords: Exhaust |
| Skewer | DamageVar(7m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Silent | Deal 7 damage ? times |
| Skim | CardsVar(3) | CardPileCmd.Draw | UpgradeValueBy(1m) | Defect | Draw 3 cards |
| SleightOfFlesh | PowerVar<SleightOfFleshPower>(9m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SleightOfFleshPower> | UpgradeValueBy(4m) | Necrobinder | Gain 9 Sleight Of Flesh |
| Slice | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Silent | Deal 6 damage |
| Slimed | CardsVar(1) | CardPileCmd.Draw |  | status | Draw 1 card; keywords: Exhaust |
| Sloth | PowerVar<SlothPower>(3m) |  |  | token | no OnPlay effect in source |
| Smokestack | PowerVar<SmokestackPower>(5m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SmokestackPower> | UpgradeValueBy(2m) | Defect | Gain 5 Smokestack |
| Snakebite | PowerVar<PoisonPower>(7m) | CreatureCmd.TriggerAnim, VfxCmd.PlayOnCreatureCenter, PowerCmd.Apply<PoisonPower> | UpgradeValueBy(3m) | Silent | Apply 7 Poison to the target; keywords: Retain |
| Snap | OstyDamageVar(7m, ValueProp.Move) | DamageCmd.Attack, CardSelectCmd.FromHand, CardCmd.ApplyKeyword | UpgradeValueBy(3m) | Necrobinder | Your Osty deals 7 damage; Choose a card in your hand; Give a card Retain |
| Sneaky | PowerVar<SneakyPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SneakyPower> | UpgradeValueBy(1m) | Silent | Gain 1 Sneaky; keywords: Sly |
| SolarStrike | DamageVar(8m, ValueProp.Move), StarsVar(1) | DamageCmd.Attack, PlayerCmd.GainStars | UpgradeValueBy(1m) | Regent | Deal 8 damage; Gain 1 Stars |
| Soot |  |  |  | status | no OnPlay effect in source; keywords: Unplayable |
| Soul | CardsVar(2) | CardPileCmd.Draw | UpgradeValueBy(1m) | token | Draw 2 cards; keywords: Exhaust |
| SoulStorm | CalculationBaseVar(9m), ExtraDamageVar(2m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Necrobinder | Deal ? damage |
| SovereignBlade | DamageVar(10m, ValueProp.Move), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("SeekingEdgeAmount"), RepeatVar(1) | DamageCmd.Attack |  | token | Deal 10 damage; keywords: Retain |
| Sow | DamageVar(8m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Necrobinder | Deal 8 damage to ALL enemies; keywords: Retain |
| SpectrumShift | CardsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SpectrumShiftPower> |  | Regent | Gain 1 Spectrum Shift |
| Speedster | PowerVar<SpeedsterPower>(2m) | PowerCmd.Apply<SpeedsterPower> | UpgradeValueBy(1m) | Silent | Gain 2 Speedster |
| Spinner | PowerVar<SpinnerPower>(1m) | CreatureCmd.TriggerAnim, OrbCmd.Channel<GlassOrb>, PowerCmd.Apply<SpinnerPower> |  | Defect | if upgraded: Channel a Glass orb; Gain 1 Spinner |
| SpiritOfAsh | DynamicVar("BlockOnExhaust", 4m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SpiritOfAshPower> | UpgradeValueBy(1m) | Necrobinder | Gain 4 Spirit Of Ash |
| Spite | DamageVar(6m, ValueProp.Move), CardsVar(1) | DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(3m) | Ironclad | Deal 6 damage; Draw 1 card |
| Splash |  | CardCmd.Upgrade, CardSelectCmd.FromChooseACardScreen, CardPileCmd.AddGeneratedCardToCombat |  | colorless | get Distinct For Combat; if upgraded: Upgrade a card; Choose a card from an offered set; Add a generated card to your hand |
| SpoilsMap | GoldVar(600) |  |  | quest | no OnPlay effect in source; keywords: Unplayable |
| SpoilsOfBattle | ForgeVar(10) | ForgeCmd.Forge | UpgradeValueBy(5m) | Regent | Forge 10 |
| SporeMind |  |  |  | curse | no OnPlay effect in source; keywords: Exhaust |
| Spur | SummonVar(3m), HealVar(5m) | CreatureCmd.TriggerAnim, OstyCmd.Summon, CreatureCmd.Heal | UpgradeValueBy(2m) | Necrobinder | Summon 3; Heal 5 HP; keywords: Retain |
| Squash | DamageVar(10m, ValueProp.Move), PowerVar<VulnerablePower>(2m) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | event | Deal 10 damage; Apply 2 Vulnerable to the target |
| Squeeze | CalculationBaseVar(25m), ExtraDamageVar(5m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(5m), UpgradeValueBy(1m) | Necrobinder | Deal ? damage |
| Stack | CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedBlockVar(ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | event | Gain ? Block |
| Stampede | DynamicVar("Power", 1m) | PowerCmd.Apply<StampedePower> |  | Ironclad | Gain 1 Stampede |
| Stardust | DamageVar(5m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(2m) | Regent | Deal 5 damage ? times to a random enemy |
| Stoke |  | CreatureCmd.TriggerAnim, CardCmd.Exhaust, CardPileCmd.Draw |  | Ironclad | Exhaust a card; Draw ? cards; keywords: Exhaust |
| Stomp | DamageVar(12m, ValueProp.Move) | CreatureCmd.TriggerAnim, DamageCmd.Attack | UpgradeValueBy(3m) | Ironclad | Deal 12 damage to ALL enemies |
| StoneArmor | PowerVar<PlatingPower>(4m) | PowerCmd.Apply<PlatingPower> | UpgradeValueBy(2m) | Ironclad | Gain 4 Plating |
| Storm | PowerVar<StormPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<StormPower> | UpgradeValueBy(1m) | Defect | Gain 1 Storm |
| StormOfSteel |  | CardCmd.Discard, CardCmd.Upgrade |  | Silent | Discard a card; Add ? Shivs to your hand; Upgrade a card |
| Strangle | DamageVar(8m, ValueProp.Move), PowerVar<StranglePower>(2m) | DamageCmd.Attack, PowerCmd.Apply<StranglePower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Silent | Deal 8 damage; Apply 2 Strangle to the target |
| Stratagem |  | PowerCmd.Apply<StratagemPower> |  | colorless | Gain 1 Stratagem |
| StrikeDefect | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Defect | Deal 6 damage |
| StrikeIronclad | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Ironclad | Deal 6 damage |
| StrikeNecrobinder | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Necrobinder | Deal 6 damage |
| StrikeRegent | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Regent | Deal 6 damage |
| StrikeSilent | DamageVar(6m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Silent | Deal 6 damage |
| Subroutine |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<SubroutinePower> |  | Defect | Gain 1 Subroutine |
| SuckerPunch | DamageVar(8m, ValueProp.Move), PowerVar<WeakPower>(1m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower> | UpgradeValueBy(2m), UpgradeValueBy(1m) | Silent | Deal 8 damage; Apply 1 Weak to the target |
| SummonForth | ForgeVar(8) | CreatureCmd.TriggerAnim, ForgeCmd.Forge, CardPileCmd.Add | UpgradeValueBy(3m) | Regent | Forge 8; Put a card into your hand |
| Sunder | DamageVar(24m, ValueProp.Move), EnergyVar(3) | DamageCmd.Attack, PlayerCmd.GainEnergy | UpgradeValueBy(8m) | Defect | Deal 24 damage; Gain 3 Energy |
| Supercritical | EnergyVar(4) | CreatureCmd.TriggerAnim, PlayerCmd.GainEnergy | UpgradeValueBy(2m) | Defect | Gain 4 Energy; keywords: Exhaust |
| Supermassive | CalculationBaseVar(5m), ExtraDamageVar(3m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(1m) | Regent | Deal ? damage |
| Suppress | DamageVar(11m, ValueProp.Move), PowerVar<WeakPower>(3m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower> | UpgradeValueBy(6m), UpgradeValueBy(2m) | Silent | Deal 11 damage; Apply 3 Weak to the target; keywords: Innate |
| Survivor | BlockVar(8m, ValueProp.Move) | CreatureCmd.GainBlock, CardSelectCmd.FromHandForDiscard, CardCmd.Discard | UpgradeValueBy(3m) | Silent | Gain 8 Block; Discard 1 card from your hand; Discard a card |
| SweepingBeam | DamageVar(6m, ValueProp.Move), CardsVar(1) | CreatureCmd.TriggerAnim, DamageCmd.Attack, CardPileCmd.Draw | UpgradeValueBy(3m) | Defect | Deal 6 damage to ALL enemies; Draw 1 card |
| SweepingGaze | OstyDamageVar(10m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(5m) | token | Your Osty deals 10 damage to a random enemy; keywords: Ethereal, Exhaust |
| SwordBoomerang | DamageVar(3m, ValueProp.Move), RepeatVar(3) | DamageCmd.Attack | UpgradeValueBy(1m) | Ironclad | Deal 3 damage 3 times to a random enemy |
| SwordSage | PowerVar<SwordSagePower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<SwordSagePower> |  | Regent | Gain 1 Sword Sage |
| Synchronize | CalculationBaseVar(0m), CalculationExtraVar(2m), CalculatedVar("CalculatedFocus") | CreatureCmd.TriggerAnim, PowerCmd.Apply<SynchronizePower> |  | Defect | Gain ? Synchronize; keywords: Exhaust |
| Synthesis | DamageVar(12m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<FreePowerPower> | UpgradeValueBy(6m) | Defect | Deal 12 damage; Gain 1 Free Power |
| Tactician | EnergyVar(1) | PlayerCmd.GainEnergy | UpgradeValueBy(1m) | Silent | Gain 1 Energy; keywords: Sly |
| TagTeam | DamageVar(11m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<TagTeamPower> | UpgradeValueBy(4m) | colorless | Deal 11 damage; Apply 1 Tag Team to the target |
| Tank |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<TankPower> |  | Ironclad | Gain 1 Tank |
| Taunt | BlockVar(7m, ValueProp.Move), PowerVar<VulnerablePower>(1m) | CreatureCmd.TriggerAnim, CreatureCmd.GainBlock, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(1m) | Ironclad | Gain 7 Block; Apply 1 Vulnerable to the target |
| TearAsunder | DamageVar(5m, ValueProp.Move), RepeatVar(1), CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedHits") | DamageCmd.Attack | UpgradeValueBy(2m) | Ironclad | Deal 5 damage ? times |
| Tempest |  | CreatureCmd.TriggerAnim, OrbCmd.Channel<LightningOrb> |  | Defect | Channel a Lightning orb |
| Terraforming | PowerVar<VigorPower>(6m) | PowerCmd.Apply<VigorPower> | UpgradeValueBy(2m) | Regent | Gain 6 Vigor |
| TeslaCoil | DamageVar(3m, ValueProp.Move) | DamageCmd.Attack, OrbCmd.Passive | UpgradeValueBy(3m) | Defect | Deal 3 damage; Trigger an orb passive |
| TheBomb | DynamicVar("Turns", 3m), DynamicVar("BombDamage", 40m) | PowerCmd.Apply<TheBombPower> | UpgradeValueBy(10m) | colorless | Gain 3 The Bomb |
| TheGambit | BlockVar(50m, ValueProp.Move) | CreatureCmd.GainBlock, PowerCmd.Apply<TheGambitPower> | UpgradeValueBy(25m) | colorless | Gain 50 Block; Gain 1 The Gambit |
| TheHunt | DamageVar(10m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<TheHuntPower> | UpgradeValueBy(5m) | Silent | Deal 10 damage; Gain 1 The Hunt; keywords: Exhaust |
| TheScythe | DamageVar(CurrentDamage, ValueProp.Move), IntVar("Increase", 3m) | DamageCmd.Attack | UpgradeValueBy(1m) | Necrobinder | Deal ? damage; keywords: Exhaust |
| TheSealedThrone |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<TheSealedThronePower> | AddKeyword(Innate) | Regent | Gain 1 The Sealed Throne |
| TheSmith | ForgeVar(30) | CreatureCmd.TriggerAnim, ForgeCmd.Forge | UpgradeValueBy(10m) | Regent | Forge 30 |
| ThinkingAhead | CardsVar(2) | CardPileCmd.Draw, CardSelectCmd.FromHand, CardPileCmd.Add |  | colorless | Draw 2 cards; Choose a card in your hand; Put a card into your draw pile; keywords: Exhaust |
| Thrash | DamageVar(4m, ValueProp.Move) | DamageCmd.Attack, CardCmd.Exhaust | UpgradeValueBy(2m) | Ironclad | Deal 4 damage 2 times; Exhaust a card |
| ThrummingHatchet | DamageVar(11m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | colorless | Deal 11 damage |
| Thunder | PowerVar<ThunderPower>(6m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ThunderPower> | UpgradeValueBy(2m) | Defect | Gain 6 Thunder |
| Thunderclap | DamageVar(4m, ValueProp.Move), PowerVar<VulnerablePower>(1m) | DamageCmd.Attack, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(3m) | Ironclad | Deal 4 damage to ALL enemies; Apply 1 Vulnerable to the target |
| TimesUp | CalculationBaseVar(0m), ExtraDamageVar(1m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | AddKeyword(Retain) | Necrobinder | Deal ? damage; keywords: Exhaust |
| ToolsOfTheTrade |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<ToolsOfTheTradePower> |  | Silent | Gain 1 Tools Of The Trade |
| ToricToughness | DynamicVar("Turns", 2m), BlockVar(5m, ValueProp.Move) | CreatureCmd.GainBlock, PowerCmd.Apply<ToricToughnessPower> | UpgradeValueBy(2m) | event | Gain 5 Block; Gain 2 Toric Toughness |
| Toxic | DamageVar(5m, ValueProp.Unpowered \\| ValueProp.Move) |  |  | status | no OnPlay effect in source; keywords: Exhaust |
| Tracking |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<TrackingPower> |  | Silent | Gain 1 Tracking; Gain 2 Tracking |
| Transfigure | EnergyVar(1) | CardSelectCmd.FromHand |  | Necrobinder | Choose a card in your hand; keywords: Exhaust |
| TrashToTreasure |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<TrashToTreasurePower> | AddKeyword(Innate) | Defect | Gain 1 Trash To Treasure |
| Tremble | PowerVar<VulnerablePower>(2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(1m) | Ironclad | Apply 2 Vulnerable to the target |
| TrueGrit | BlockVar(7m, ValueProp.Move) | CreatureCmd.GainBlock, CardSelectCmd.FromHand, CardCmd.Exhaust | UpgradeValueBy(2m) | Ironclad | Gain 7 Block; if upgraded: Choose a card in your hand; if upgraded: Exhaust a card; Exhaust a card |
| Turbo | EnergyVar(2) | PlayerCmd.GainEnergy, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(1m) | Defect | Gain 2 Energy; Add a generated card to your discard pile |
| TwinStrike | DamageVar(5m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(2m) | Ironclad | Deal 5 damage 2 times |
| Tyranny |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<TyrannyPower> | AddKeyword(Innate) | Regent | Gain 1 Tyranny |
| UltimateDefend | BlockVar(11m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(4m) | colorless | Gain 11 Block |
| UltimateStrike | DamageVar(14m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(6m) | colorless | Deal 14 damage |
| Undeath | BlockVar(7m, ValueProp.Move) | CreatureCmd.GainBlock, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat | UpgradeValueBy(2m) | Necrobinder | Gain 7 Block; Add a generated card to your discard pile |
| Unleash | CalculationBaseVar(6m), ExtraDamageVar(1m), CalculatedDamageVar(ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(3m) | Necrobinder | Deal ? damage |
| Unmovable |  | CreatureCmd.TriggerAnim, PowerCmd.Apply<UnmovablePower> |  | Ironclad | Gain 1 Unmovable |
| Unrelenting | DamageVar(12m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<FreeAttackPower> | UpgradeValueBy(6m) | Ironclad | Deal 12 damage; Gain 1 Free Attack |
| Untouchable | BlockVar(9m, ValueProp.Move) | CreatureCmd.GainBlock | UpgradeValueBy(3m) | Silent | Gain 9 Block; keywords: Sly |
| UpMySleeve | CardsVar(3) | CreatureCmd.TriggerAnim | UpgradeValueBy(1m) | Silent | Add 3 Shivs to your hand |
| Uppercut | DamageVar(13m, ValueProp.Move), DynamicVar("Power", 1m) | DamageCmd.Attack, PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> | UpgradeValueBy(1m) | Ironclad | Deal 13 damage; Apply 1 Weak to the target; Apply 1 Vulnerable to the target |
| Uproar | DamageVar(5m, ValueProp.Move) | DamageCmd.Attack, CardCmd.AutoPlay | UpgradeValueBy(2m) | Defect | Deal 5 damage 2 times; Auto-play a card |
| Veilpiercer | DamageVar(10m, ValueProp.Move) | DamageCmd.Attack, PowerCmd.Apply<VeilpiercerPower> | UpgradeValueBy(3m) | Necrobinder | Deal 10 damage; Gain 1 Veilpiercer |
| Venerate | StarsVar(2) | CreatureCmd.TriggerAnim, PlayerCmd.GainStars | UpgradeValueBy(1m) | Regent | Gain 2 Stars |
| Vicious | CardsVar(1) | CreatureCmd.TriggerAnim, PowerCmd.Apply<ViciousPower> | UpgradeValueBy(1m) | Ironclad | Gain 1 Vicious |
| Void | EnergyVar(1) |  |  | status | no OnPlay effect in source; keywords: Unplayable, Ethereal |
| VoidForm | PowerVar<VoidFormPower>(2m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<VoidFormPower>, PlayerCmd.EndTurn | UpgradeValueBy(1m) | Regent | Gain 2 Void Form; End your turn |
| Volley | DamageVar(10m, ValueProp.Move) | DamageCmd.Attack | UpgradeValueBy(4m) | colorless | Deal 10 damage ? times to a random enemy |
| Voltaic | CalculationBaseVar(0m), CalculationExtraVar(1m), CalculatedVar("CalculatedChannels") | CreatureCmd.TriggerAnim, OrbCmd.Channel<LightningOrb> |  | Defect | Channel a Lightning orb; keywords: Exhaust |
| WasteAway | PowerVar<WasteAwayPower>(1m) |  |  | token | no OnPlay effect in source |
| WellLaidPlans | DynamicVar("RetainAmount", 1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<WellLaidPlansPower> | UpgradeValueBy(1m) | Silent | Gain 1 Well Laid Plans |
| Whirlwind | DamageVar(5m, ValueProp.Move) | SfxCmd.Play, DamageCmd.Attack | UpgradeValueBy(3m) | Ironclad | Deal 5 damage X (energy spent) times to ALL enemies |
| Whistle | DamageVar(33m, ValueProp.Move) | DamageCmd.Attack, CreatureCmd.Stun | UpgradeValueBy(11m) | event | Deal 33 damage; Stun the target; keywords: Exhaust |
| WhiteNoise |  | CreatureCmd.TriggerAnim, CardPileCmd.AddGeneratedCardToCombat |  | Defect | get Distinct For Combat; Add a generated card to your hand; keywords: Exhaust |
| Wish |  | CardSelectCmd.FromSimpleGrid, CardPileCmd.Add | AddKeyword(Retain) | event | Choose a card from a pile; Put a card into your hand; keywords: Exhaust |
| Wisp | EnergyVar(1) | PlayerCmd.GainEnergy | AddKeyword(Retain) | Necrobinder | Gain 1 Energy; keywords: Exhaust |
| Wound |  |  |  | status | no OnPlay effect in source; keywords: Unplayable |
| WraithForm | PowerVar<IntangiblePower>(2m), PowerVar<WraithFormPower>(1m) | CreatureCmd.TriggerAnim, PowerCmd.Apply<IntangiblePower>, PowerCmd.Apply<WraithFormPower> | UpgradeValueBy(1m) | Silent | Gain 2 Intangible; Gain 1 Wraith Form |
| Writhe |  |  |  | curse | no OnPlay effect in source; keywords: Innate, Unplayable |
| WroughtInWar | DamageVar(7m, ValueProp.Move), ForgeVar(5) | DamageCmd.Attack, ForgeCmd.Forge | UpgradeValueBy(2m) | Regent | Deal 7 damage; Forge 5 |
| Zap |  | CreatureCmd.TriggerAnim, OrbCmd.Channel<LightningOrb> |  | Defect | Channel a Lightning orb |
