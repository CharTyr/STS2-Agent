# Potion Behavior Index

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-09-21 22:31:54 +08:00

Behavior summaries extracted from potion source. Useful when adding potion support or planning item usage.

| Name | Vars | OnUse |
| --- | --- | --- |
| Ashwater |  | CardSelectCmd.FromHand, CardCmd.Exhaust |
| AttackPotion |  | CardSelectCmd.FromChooseACardScreen, CardPileCmd.AddGeneratedCardToCombat |
| BeetleJuice | DynamicVar("DamageDecrease", 30m), RepeatVar(4) | PowerCmd.Apply<ShrinkPower> |
| BlessingOfTheForge |  | CardCmd.Upgrade |
| BlockPotion | BlockVar(12m, ValueProp.Unpowered) | CreatureCmd.GainBlock |
| BloodPotion | DynamicVar("HealPercent", 20m) | CreatureCmd.Heal |
| BoneBrew | SummonVar(15m) | OstyCmd.Summon |
| BottledPotential | CardsVar(5) | CardPileCmd.Add, CardPileCmd.Shuffle, CardPileCmd.Draw |
| Clarity | PowerVar<ClarityPower>(3m), CardsVar(1) | CardPileCmd.Draw, PowerCmd.Apply<ClarityPower> |
| ColorlessPotion |  | CardSelectCmd.FromChooseACardScreen, CardPileCmd.AddGeneratedCardToCombat |
| CosmicConcoction | CardsVar(3) | CardCmd.Upgrade, CardPileCmd.AddGeneratedCardToCombat |
| CunningPotion | CardsVar(3) | CardCmd.Upgrade |
| CureAll | EnergyVar(1), CardsVar(2) | PlayerCmd.GainEnergy, CardPileCmd.Draw |
| DeprecatedPotion |  |  |
| DexterityPotion | PowerVar<DexterityPower>(2m) | PowerCmd.Apply<DexterityPower> |
| DistilledChaos | RepeatVar(3) | CardPileCmd.AutoPlayFromDrawPile |
| DropletOfPrecognition |  | CardSelectCmd.FromSimpleGrid, CardPileCmd.Add |
| Duplicator |  | PowerCmd.Apply<DuplicationPower> |
| EnergyPotion | EnergyVar(2) | PlayerCmd.GainEnergy |
| EntropicBrew |  | PotionCmd.TryToProcure |
| EssenceOfDarkness |  | OrbCmd.Channel<DarkOrb> |
| ExplosiveAmpoule | DamageVar(10m, ValueProp.Unpowered) | CreatureCmd.Damage |
| FairyInABottle |  | CreatureCmd.Heal |
| FirePotion | DamageVar(20m, ValueProp.Unpowered) | CreatureCmd.Damage |
| FlexPotion | PowerVar<StrengthPower>(5m) | PowerCmd.Apply<FlexPotionPower> |
| FocusPotion | PowerVar<FocusPower>(2m) | PowerCmd.Apply<FocusPower> |
| Fortifier |  | CreatureCmd.GainBlock |
| FoulPotion | DamageVar(12m, ValueProp.Unpowered), GoldVar(100) | CreatureCmd.Damage, PlayerCmd.GainGold |
| FruitJuice | MaxHpVar(5m) | CreatureCmd.GainMaxHp |
| FyshOil | PowerVar<StrengthPower>(1m), PowerVar<DexterityPower>(1m) | PowerCmd.Apply<StrengthPower>, PowerCmd.Apply<DexterityPower> |
| GamblersBrew |  | CardSelectCmd.FromHandForDiscard, CardCmd.DiscardAndDraw |
| GhostInAJar | PowerVar<IntangiblePower>(1m) | PowerCmd.Apply<IntangiblePower> |
| GigantificationPotion | PowerVar<GigantificationPower>(1m) | PowerCmd.Apply<GigantificationPower> |
| GlowwaterPotion | CardsVar(10) | CardCmd.Exhaust, CardPileCmd.Draw |
| HeartOfIron | PowerVar<PlatingPower>(7m) | PowerCmd.Apply<PlatingPower> |
| KingsCourage | ForgeVar(15) | ForgeCmd.Forge |
| LiquidBronze | PowerVar<ThornsPower>(3m) | PowerCmd.Apply<ThornsPower> |
| LiquidMemories |  | CardSelectCmd.FromSimpleGrid, CardPileCmd.Add |
| LuckyTonic | PowerVar<BufferPower>(1m) | PowerCmd.Apply<BufferPower> |
| MazalethsGift | PowerVar<RitualPower>(1m) | PowerCmd.Apply<RitualPower> |
| OrobicAcid |  | CardPileCmd.AddGeneratedCardsToCombat |
| PoisonPotion | PowerVar<PoisonPower>(6m) | PowerCmd.Apply<PoisonPower> |
| PotionOfBinding | PowerVar<VulnerablePower>(1m), PowerVar<WeakPower>(1m) | PowerCmd.Apply<WeakPower>, PowerCmd.Apply<VulnerablePower> |
| PotionOfCapacity | RepeatVar(2) | OrbCmd.AddSlots |
| PotionOfDoom | PowerVar<DoomPower>(33m) | PowerCmd.Apply<DoomPower> |
| PotionShapedRock | DamageVar(15m, ValueProp.Unpowered) | CreatureCmd.Damage |
| PotOfGhouls | CardsVar(2) |  |
| PowderedDemise | DynamicVar("Demise", 9m) | PowerCmd.Apply<DemisePower> |
| PowerPotion |  | CardSelectCmd.FromChooseACardScreen, CardPileCmd.AddGeneratedCardToCombat |
| RadiantTincture | EnergyVar(1), PowerVar<RadiancePower>(3m) | PlayerCmd.GainEnergy, PowerCmd.Apply<RadiancePower> |
| RegenPotion | PowerVar<RegenPower>(5m) | PowerCmd.Apply<RegenPower> |
| ShacklingPotion | PowerVar<StrengthPower>(7m) | PowerCmd.Apply<ShacklingPotionPower> |
| ShipInABottle | BlockVar(10m, ValueProp.Unpowered) | CreatureCmd.GainBlock, PowerCmd.Apply<BlockNextTurnPower> |
| SkillPotion |  | CardSelectCmd.FromChooseACardScreen, CardPileCmd.AddGeneratedCardToCombat |
| SneckoOil | CardsVar(7) | CardPileCmd.Draw |
| SoldiersStew |  |  |
| SpeedPotion | PowerVar<DexterityPower>(5m) | PowerCmd.Apply<SpeedPotionPower> |
| StableSerum | RepeatVar(2) | PowerCmd.Apply<RetainHandPower> |
| StarPotion | StarsVar(3) | PlayerCmd.GainStars |
| StrengthPotion | PowerVar<StrengthPower>(2m) | PowerCmd.Apply<StrengthPower> |
| SwiftPotion | CardsVar(3) | CardPileCmd.Draw |
| TouchOfInsanity |  | CardSelectCmd.FromHand |
| VulnerablePotion | PowerVar<VulnerablePower>(3m) | PowerCmd.Apply<VulnerablePower> |
| WeakPotion | PowerVar<WeakPower>(3m) | PowerCmd.Apply<WeakPower> |
