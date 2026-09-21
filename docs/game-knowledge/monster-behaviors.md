# Monster Behavior Index

> Auto-generated from extraction/decompiled in this repository.  
> Generated at: 2026-09-21 21:57:15 +08:00

Move-state and passive-command summaries extracted from monster source.

| Name | Moves | Passive |
| --- | --- | --- |
| Architect | NOTHING=HiddenIntent |  |
| AssassinRubyRaider | KILLSHOT_MOVE=SingleAttackIntent(KillshotDamage) |  |
| Axebot | ONE_TWO_MOVE=MultiAttackIntent(OneTwoDamage, 2); SHARPEN_MOVE=BuffIntent | PowerCmd.Apply<StockPower> |
| AxeRubyRaider | BIG_SWING=SingleAttackIntent(BigSwingDamage) |  |
| BattleFriendV1 |  | PowerCmd.Apply<BattlewornDummyTimeLimitPower> |
| BattleFriendV2 |  | PowerCmd.Apply<BattlewornDummyTimeLimitPower> |
| BattleFriendV3 |  | PowerCmd.Apply<BattlewornDummyTimeLimitPower> |
| BigDummy | NOTHING=HiddenIntent |  |
| BowlbugEgg |  |  |
| BowlbugNectar | THRASH_MOVE=SingleAttackIntent(ThrashDamage); BUFF_MOVE=BuffIntent; THRASH2_MOVE=SingleAttackIntent(ThrashDamage) |  |
| BowlbugRock | HEADBUTT_MOVE=SingleAttackIntent(HeadbuttDamage); DIZZY_MOVE=StunIntent | PowerCmd.Apply<ImbalancedPower> |
| BowlbugSilk | TRASH_MOVE=MultiAttackIntent(ThrashDamage, 2) |  |
| BruteRubyRaider | BEAT_MOVE=SingleAttackIntent(BeatDamage) |  |
| BygoneEffigy | INITIAL_SLEEP_MOVE=SleepIntent; WAKE_MOVE=BuffIntent; SLEEP_MOVE=SleepIntent; SLASHES_MOVE=SingleAttackIntent(SlashDamage) | PowerCmd.Apply<SlowPower> |
| Byrdonis | PECK_MOVE=MultiAttackIntent(PeckDamage, PeckRepeat); SWOOP_MOVE=SingleAttackIntent(SwoopDamage) | PowerCmd.Apply<TerritorialPower> |
| Byrdpip |  |  |
| CalcifiedCultist | INCANTATION_MOVE=BuffIntent |  |
| CeremonialBeast | STAMP_MOVE=BuffIntent; STUN_MOVE=StunIntent; STOMP_MOVE=SingleAttackIntent(StompDamage) |  |
| Chomper | CLAMP_MOVE=MultiAttackIntent(ClampDamage, 2) | PowerCmd.Apply<ArtifactPower> |
| CorpseSlug | WHIP_SLAP_MOVE=MultiAttackIntent(WhipSlapDamage, WhipSlapRepeat); GLOMP_MOVE=SingleAttackIntent(GlompDamage); GOOP_MOVE=DebuffIntent | PowerCmd.Apply<RavenousPower> |
| CrossbowRubyRaider | FIRE_MOVE=SingleAttackIntent(FireDamage) |  |
| Crusher | THRASH_MOVE=SingleAttackIntent(ThrashDamage); ENLARGING_STRIKE_MOVE=SingleAttackIntent(EnlargingStrikeDamage); ADAPT_MOVE=BuffIntent | PowerCmd.Apply<BackAttackLeftPower>, PowerCmd.Apply<CrabRagePower> |
| CubexConstruct | CHARGE_UP_MOVE=BuffIntent; EXPEL_BLAST=MultiAttackIntent(ExpelDamage, 2); SUBMERGE_MOVE=DefendIntent | CreatureCmd.GainBlock, PowerCmd.Apply<ArtifactPower> |
| DampCultist | INCANTATION_MOVE=BuffIntent |  |
| DecimillipedeSegment | WRITHE_MOVE=MultiAttackIntent(WritheDamage, 2); REATTACH_MOVE=HealIntent | CreatureCmd.SetMaxAndCurrentHp, PowerCmd.Apply<ReattachPower> |
| DecimillipedeSegmentBack |  |  |
| DecimillipedeSegmentFront |  |  |
| DecimillipedeSegmentMiddle |  |  |
| DevotedSculptor | FORBIDDEN_INCANTATION_MOVE=BuffIntent |  |
| Door | DOOR_SLAM_MOVE=MultiAttackIntent(DoorSlamDamage, DoorSlamRepeat) | PowerCmd.Apply<DoorRevivalPower> |
| Doormaker | WHAT_IS_IT_MOVE=StunIntent; BEAM_MOVE=SingleAttackIntent(LaserBeamDamage) |  |
| Entomancer | PHEROMONE_SPIT_MOVE=BuffIntent; BEES_MOVE=MultiAttackIntent(BeesDamage, BeesRepeat) | PowerCmd.Apply<PersonalHivePower> |
| Exoskeleton | SKITTER_MOVE=MultiAttackIntent(SkitterDamage, SkitterRepeats); MANDIBLE_MOVE=SingleAttackIntent(MandiblesDamage); ENRAGE_MOVE=BuffIntent | PowerCmd.Apply<HardToKillPower> |
| EyeWithTeeth | DISTRACT_MOVE=StatusIntent(3) | PowerCmd.Apply<IllusionPower> |
| Fabricator | FABRICATE_MOVE=SummonIntent; DISINTEGRATE_MOVE=SingleAttackIntent(DisintegrateDamage) |  |
| FakeMerchantMonster | SWIPE_MOVE=SingleAttackIntent(SwipeDamage); SPEW_COINS_MOVE=MultiAttackIntent(2, 8); ENRAGE_MOVE=BuffIntent |  |
| FatGremlin | SPAWNED_MOVE=StunIntent |  |
| FlailKnight | WAR_CHANT=BuffIntent; FLAIL_MOVE=MultiAttackIntent(FlailDamage, 2); RAM_MOVE=SingleAttackIntent(RamDamage) |  |
| Flyconid | VULNERABLE_SPORES_MOVE=DebuffIntent; SMASH_MOVE=SingleAttackIntent(SmashDamage) |  |
| Fogmog | ILLUSION_MOVE=SummonIntent; HEADBUTT_MOVE=SingleAttackIntent(HeadbuttDamage) |  |
| FossilStalker | LATCH_MOVE=SingleAttackIntent(LatchDamage); LASH_MOVE=MultiAttackIntent(LashDamage, LashRepeat) | PowerCmd.Apply<SuckPower> |
| FrogKnight | FOR_THE_QUEEN=BuffIntent; STRIKE_DOWN_EVIL=SingleAttackIntent(StrikeDownEvilDamage); BEETLE_CHARGE=SingleAttackIntent(BeetleChargeDamage) | PowerCmd.Apply<PlatingPower> |
| FuzzyWurmCrawler | FIRST_ACID_GOOP=SingleAttackIntent(AcidGoopDamage); ACID_GOOP=SingleAttackIntent(AcidGoopDamage) |  |
| GasBomb |  | PowerCmd.Apply<MinionPower> |
| GlobeHead | THUNDER_STRIKE=MultiAttackIntent(ThunderStrikeDamage, 3) | PowerCmd.Apply<GalvanicPower> |
| GremlinMerc | GIMME_MOVE=MultiAttackIntent(GimmeDamage, GimmeRepeat) | PowerCmd.Apply<SurprisePower>, PowerCmd.Apply |
| Guardbot | GUARD_MOVE=DefendIntent |  |
| HauntedShip | SWIPE_MOVE=SingleAttackIntent(SwipeDamage); STOMP_MOVE=MultiAttackIntent(StompDamage, StompRepeat); HAUNT_MOVE=DebuffIntent |  |
| HunterKiller | TENDERIZING_GOOP_MOVE=DebuffIntent; BITE_MOVE=SingleAttackIntent(BiteDamage); PUNCTURE_MOVE=MultiAttackIntent(PunctureDamage, 3) |  |
| InfestedPrism | JAB_MOVE=SingleAttackIntent(JabDamage); WHIRLWIND_MOVE=MultiAttackIntent(WhirlwindDamage, WhirlwindRepeat) | PowerCmd.Apply<VitalSparkPower> |
| Inklet | JAB_MOVE=SingleAttackIntent(JabDamage); WHIRLWIND_MOVE=MultiAttackIntent(WhirlwindDamage, 3); PIERCING_GAZE_MOVE=SingleAttackIntent(PiercingGazeDamage) | PowerCmd.Apply<SlipperyPower> |
| KinFollower | QUICK_SLASH_MOVE=SingleAttackIntent(QuickSlashDamage); BOOMERANG_MOVE=MultiAttackIntent(BoomerangDamage, 2); POWER_DANCE_MOVE=BuffIntent | PowerCmd.Apply<MinionPower> |
| KinPriest | BEAM_MOVE=MultiAttackIntent(BeamDamage, 3); RITUAL_MOVE=BuffIntent |  |
| KnowledgeDemon | CURSE_OF_KNOWLEDGE_MOVE=DebuffIntent; SLAP_MOVE=SingleAttackIntent(SlapDamage); KNOWLEDGE_OVERWHELMING_MOVE=MultiAttackIntent(KnowledgeOverwhelmingDamage, 3) |  |
| LagavulinMatriarch | SLEEP_MOVE=SleepIntent; SLASH_MOVE=SingleAttackIntent(SlashDamage); DISEMBOWEL_MOVE=MultiAttackIntent(DisembowelDamage, DisembowelRepeat) | CreatureCmd.TriggerAnim, PowerCmd.Apply<PlatingPower>, PowerCmd.Apply<AsleepPower> |
| LeafSlimeM | CLUMP_SHOT=SingleAttackIntent(ClumpDamage); STICKY_SHOT=StatusIntent(2) |  |
| LeafSlimeS | BUTT_MOVE=SingleAttackIntent(TackleDamage); GOOP_MOVE=StatusIntent(1) |  |
| LivingFog | SUPER_GAS_BLAST_MOVE=SingleAttackIntent(SuperGasBlastDamage) |  |
| LivingShield | SHIELD_SLAM_MOVE=SingleAttackIntent(ShieldSlamDamage) | PowerCmd.Apply<RampartPower> |
| LouseProgenitor | POUNCE_MOVE=SingleAttackIntent(PounceDamage) | PowerCmd.Apply<CurlUpPower> |
| MagiKnight | DAMPEN_MOVE=DebuffIntent; PREP_MOVE=DefendIntent; MAGIC_BOMB=SingleAttackIntent(BombDamage); RAM_MOVE=SingleAttackIntent(SpearDamage) |  |
| Mawler | RIP_AND_TEAR_MOVE=SingleAttackIntent(RipAndTearDamage); ROAR_MOVE=DebuffIntent; CLAW_MOVE=MultiAttackIntent(ClawDamage, 2) |  |
| MechaKnight | CHARGE_MOVE=SingleAttackIntent(ChargeDamage); FLAMETHROWER_MOVE=StatusIntent(4); HEAVY_CLEAVE_MOVE=SingleAttackIntent(HeavyCleaveDamage) | PowerCmd.Apply<ArtifactPower> |
| MultiAttackMoveMonster | POKE=MultiAttackIntent(1, 5) |  |
| MysteriousKnight |  | PowerCmd.Apply<StrengthPower>, PowerCmd.Apply<PlatingPower> |
| Myte | TOXIC_MOVE=StatusIntent(2); BITE_MOVE=SingleAttackIntent(BiteDamage) |  |
| Nibbit | BUTT_MOVE=SingleAttackIntent(ButtDamage); HISS_MOVE=BuffIntent |  |
| Noisebot | NOISE_MOVE=StatusIntent(2) |  |
| OneHpMonster | NOTHING=HiddenIntent |  |
| Osty |  |  |
| Ovicopter | LAY_EGGS_MOVE=SummonIntent; SMASH_MOVE=SingleAttackIntent(SmashDamage); NUTRITIONAL_PASTE_MOVE=BuffIntent | SfxCmd.PlayLoop |
| OwlMagistrate | MAGISTRATE_SCRUTINY=SingleAttackIntent(ScrutinyDamage); PECK_ASSAULT=MultiAttackIntent(PeckAssaultDamage, 6); JUDICIAL_FLIGHT=BuffIntent |  |
| PaelsLegion |  |  |
| Parafright | SLAM_MOVE=SingleAttackIntent(SlamDamage) | PowerCmd.Apply<IllusionPower> |
| PhantasmalGardener | BITE_MOVE=SingleAttackIntent(BiteDamage); LASH_MOVE=SingleAttackIntent(LashDamage); FLAIL_MOVE=MultiAttackIntent(FlailDamage, FlailRepeat); ENLARGE_MOVE=BuffIntent | PowerCmd.Apply<SkittishPower> |
| PhrogParasite | INFECT_MOVE=StatusIntent(3); LASH_MOVE=MultiAttackIntent(LashDamage, 4) | PowerCmd.Apply<InfestedPower> |
| PunchConstruct | READY_MOVE=DefendIntent; STRONG_PUNCH_MOVE=SingleAttackIntent(StrongPunchDamage) | PowerCmd.Apply<ArtifactPower> |
| Queen | PUPPET_STRINGS_MOVE=CardDebuffIntent; YOUR_MINE_MOVE=DebuffIntent; OFF_WITH_YOUR_HEAD_MOVE=MultiAttackIntent(OffWithYourHeadDamage, 5); EXECUTION_MOVE=SingleAttackIntent(ExecutionDamage) |  |
| Rocket | TARGETING_RETICLE_MOVE=SingleAttackIntent(TargetingReticleDamage); PRECISION_BEAM_MOVE=SingleAttackIntent(PrecisionBeamDamage); CHARGE_UP_MOVE=BuffIntent; LASER_MOVE=SingleAttackIntent(LaserDamage); RECHARGE_MOVE=SleepIntent | PowerCmd.Apply<SurroundedPower>, PowerCmd.Apply<BackAttackRightPower>, PowerCmd.Apply<CrabRagePower> |
| ScrollOfBiting | CHOMP=SingleAttackIntent(ChompDamage); CHEW=MultiAttackIntent(ChewDamage, 2); MORE_TEETH=BuffIntent | PowerCmd.Apply<PaperCutsPower> |
| Seapunk | SEA_KICK_MOVE=SingleAttackIntent(SeaKickDamage); SPINNING_KICK_MOVE=MultiAttackIntent(SpinningKickDamage, SpinningKickRepeat) |  |
| SewerClam | PRESSURIZE_MOVE=BuffIntent | PowerCmd.Apply<PlatingPower> |
| ShrinkerBeetle | SHRINKER_MOVE=DebuffIntent(strong: true); CHOMP_MOVE=SingleAttackIntent(ChompDamage); STOMP_MOVE=SingleAttackIntent(StompDamage) |  |
| SingleAttackMoveMonster | POKE=SingleAttackIntent(1) |  |
| SkulkingColony | ZOOM_MOVE=SingleAttackIntent(ZoomDamage); SUPER_CRAB_MOVE=MultiAttackIntent(SuperCrabDamage, SuperCrabRepeat) | PowerCmd.Apply<HardenedShellPower> |
| SlimedBerserker | VOMIT_ICHOR_MOVE=StatusIntent(10); SMOTHER_MOVE=SingleAttackIntent(SmotherDamage) |  |
| SlitheringStrangler | CONSTRICT=DebuffIntent; LASH=SingleAttackIntent(LashDamage) |  |
| SludgeSpinner | SLAM_MOVE=SingleAttackIntent(SlamDamage) |  |
| SlumberingBeetle | SNORE_MOVE=SleepIntent | PowerCmd.Apply<PlatingPower>, PowerCmd.Apply<SlumberPower>, SfxCmd.PlayLoop |
| SnappingJaxfruit |  | SfxCmd.PlayLoop |
| SneakyGremlin | SPAWNED_MOVE=StunIntent |  |
| SoulFysh | BECKON_MOVE=StatusIntent(BeckonMoveAmount); DE_GAS_MOVE=SingleAttackIntent(DeGasDamage); FADE_MOVE=BuffIntent |  |
| SoulNexus | SOUL_BURN_MOVE=SingleAttackIntent(SoulBurnDamage); MAELSTROM_MOVE=MultiAttackIntent(MaelstromDamage, MaelstromRepeat) |  |
| SpectralKnight | HEX=DebuffIntent; SOUL_SLASH=SingleAttackIntent(SoulSlashDamage); SOUL_FLAME=MultiAttackIntent(SoulFlameDamage, 3) |  |
| SpinyToad | PROTRUDING_SPIKES_MOVE=BuffIntent; SPIKE_EXPLOSION_MOVE=SingleAttackIntent(ExplosionDamage); TONGUE_LASH_MOVE=SingleAttackIntent(LashDamage) |  |
| Stabbot |  |  |
| TenHpMonster | NOTHING=HiddenIntent |  |
| TerrorEel | CRASH_MOVE=SingleAttackIntent(CrashDamage); STUN_MOVE=StunIntent | PowerCmd.Apply<ShriekPower> |
| TestSubject | BITE_MOVE=SingleAttackIntent(BiteDamage); POUNCE_MOVE=SingleAttackIntent(PounceDamage); PHASE3_LACERATE_MOVE=MultiAttackIntent(Phase3LacerateDamage, 3); BIG_POUNCE_MOVE=SingleAttackIntent(BigPounceDamage) | PowerCmd.Apply<AdaptablePower>, PowerCmd.Apply<EnragePower> |
| TheAdversaryMkOne | SMASH_MOVE=SingleAttackIntent(SmashDamage); BEAM_MOVE=SingleAttackIntent(BeamDamage) | PowerCmd.Apply<ArtifactPower> |
| TheAdversaryMkThree | CRASH_MOVE=SingleAttackIntent(CrashDamage); FLAME_BEAM_MOVE=SingleAttackIntent(FlameBeamDamage) | PowerCmd.Apply<ArtifactPower> |
| TheAdversaryMkTwo | BASH_MOVE=SingleAttackIntent(BashDamage); FLAME_BEAM_MOVE=SingleAttackIntent(FlameBeamDamage) | PowerCmd.Apply<ArtifactPower> |
| TheForgotten |  | PowerCmd.Apply<PossessSpeedPower> |
| TheInsatiable | THRASH_MOVE_1=MultiAttackIntent(ThrashDamage, 2); THRASH_MOVE_2=MultiAttackIntent(ThrashDamage, 2); LUNGING_BITE_MOVE=SingleAttackIntent(BiteDamage); SALIVATE_MOVE=BuffIntent |  |
| TheLost |  | PowerCmd.Apply<PossessStrengthPower> |
| TheObscura | ILLUSION_MOVE=SummonIntent; PIERCING_GAZE_MOVE=SingleAttackIntent(PiercingGazeDamage); SAIL_MOVE=BuffIntent |  |
| ThievingHopper | NAB_MOVE=SingleAttackIntent(NabDamage); HAT_TRICK_MOVE=SingleAttackIntent(HatTrickDamage); FLUTTER_MOVE=BuffIntent; ESCAPE_MOVE=EscapeIntent | PowerCmd.Apply<EscapeArtistPower> |
| Toadpole | SPIKE_SPIT_MOVE=MultiAttackIntent(SpikeSpitDamage, SpikeSpitRepeat); WHIRL_MOVE=SingleAttackIntent(WhirlDamage); SPIKEN_MOVE=BuffIntent |  |
| TorchHeadAmalgam | TACKLE_1_MOVE=SingleAttackIntent(TackleDamage); TACKLE_2_MOVE=SingleAttackIntent(TackleDamage); BEAM_MOVE=MultiAttackIntent(SoulBeamDamage, 3); TACKLE_3_MOVE=SingleAttackIntent(WeakTackleDamage); TACKLE_4_MOVE=SingleAttackIntent(WeakTackleDamage) | PowerCmd.Apply<MinionPower> |
| ToughEgg | HATCH_MOVE=SummonIntent | PowerCmd.Apply<HatchPower> |
| TrackerRubyRaider | TRACK_MOVE=DebuffIntent |  |
| Tunneler | BITE_MOVE=SingleAttackIntent(BiteDamage); BELOW_MOVE_1=SingleAttackIntent(BelowDamage); DIZZY_MOVE=StunIntent |  |
| TurretOperator | UNLOAD_MOVE_1=MultiAttackIntent(FireDamage, 5); UNLOAD_MOVE_2=MultiAttackIntent(FireDamage, 5); RELOAD_MOVE=BuffIntent |  |
| TwigSlimeM | CLUMP_SHOT_MOVE=SingleAttackIntent(ClumpDamage); STICKY_SHOT_MOVE=StatusIntent(1) |  |
| TwigSlimeS | BUTT_MOVE=SingleAttackIntent(TackleDamage) |  |
| TwoTailedRat | SCRATCH_MOVE=SingleAttackIntent(ScratchDamage); DISEASE_BITE_MOVE=SingleAttackIntent(DiseaseBiteDamage); SCREECH_MOVE=DebuffIntent; CALL_FOR_BACKUP_MOVE=SummonIntent |  |
| Vantom | INK_BLOT_MOVE=SingleAttackIntent(InkBlotDamage); INKY_LANCE_MOVE=MultiAttackIntent(InkyLanceDamage, 2); PREPARE_MOVE=BuffIntent | PowerCmd.Apply<SlipperyPower> |
| VineShambler | SWIPE_MOVE=MultiAttackIntent(SwipeDamage, 2); CHOMP_MOVE=SingleAttackIntent(ChompDamage) |  |
| WaterfallGiant | PRESSURIZE_MOVE=BuffIntent | SfxCmd.PlayLoop |
| Wriggler | NASTY_BITE_MOVE=SingleAttackIntent(BiteDamage); SPAWNED_MOVE=StunIntent |  |
| Zapbot | ZAP=SingleAttackIntent(ZapDamage) | PowerCmd.Apply<HighVoltagePower> |
