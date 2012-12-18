﻿// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------
namespace Questor.Modules.Lookup
{
    public enum Group
    {
        Star = 6,
        Station = 15,
        Stargate = 10,
        AsteroidBelt = 9,
        AccelerationGate = 366,
        JumpBridge = 707,

        Capsule = 29,

        // Note: This includes MicroWarpDrives as well!
        Afterburner = 46,

        CapacitorInjector = 76,
        AncillaryShieldBooster = 1156,
        ShieldBoosters = 40,
        ShieldHardeners = 77,
        ArmorRepairer = 62,
        ArmorHardeners = 328,
        DamageControl = 60,
        ECCM = 202,
        CloakingDevice = 330,

        TrackingLink = 209,
        //TrackingEnhancer = 211, //no script needed
        SensorBooster = 212,
        SensorDampener = 208,
        TrackingComputer = 213,
        TrackingDisruptor = 291,

        TrackingScript = 907,
        WarpDisruptionScript = 908,
        TrackingDisruptionScript = 909,
        SensorBoosterScript = 910,
        SensorDampenerScript = 911,

        ProximityDrone = 97,
        CombatDrone = 100,
        MiningDrone = 101,
        FighterDrone = 549,
        ElectronicWarfareDrone = 639,
        LogisticsDrone = 640,
        WebbingDrone = 641,

        CapacitorGroupCharge = 87,
        CriminalTags = 370,
        EmpireInsigniaDrops = 409,

        StasisWeb = 65,
        NOS = 68,
        TargetPainter = 379,

        EnergyWeapon = 53,
        ProjectileWeapon = 55,
        HybridWeapon = 74,

        CruiseMissileLaunchers = 506,
        RocketLaunchers = 507,
        TorpedoLaunchers = 508,
        StandardMissileLaunchers = 509,
        HeavyMissilelaunchers = 510,
        AssaultMissilelaunchers = 511,
        DefenderMissilelaunchers = 512,

        Miners = 54, // Miner I, Miner II, etc - Mining Tool GroupIDs
        StripMiners = 464, // Strip Miner I, etc - Mining Tool GroupIDs
        ModulatedStripMiners = 483, // Modulated Strip Miners, etc - Mining Tool GroupIDs

        SentryGun = 99,                       //Protecting the weak and upholding the law

        ProtectiveSentryGun = 180,
        MobileSentryGun = 336,
        DestructibleSentryGun = 383,
        MobileMissileSentry = 417,
        MobileProjectileSentry = 426,
        MobileLaserSentry = 430,
        StasisWebificationBattery = 441,
        MobileHybridSentry = 449,
        DeadspaceOverseersSentry = 495,
        EnergyNeutralizingBattery = 837,

        LargeCollidableStructure = 319,
        LargeCollidableObject = 226,
        LargeCollidableShip = 784,

        CargoContainer = 12,
        SpawnContainer = 306,
        SecureContainer = 340,
        AuditLogSecureContainer = 448,
        FreightContainer = 649,
        MissionContainer = 952,

        Wreck = 186,

        Livestock = 283, //this includes most CommonMissionCompletionitems like Militants and Marines
        MiscSpecialMissionItems = 314, //Misc special mission items that should not be on the market and not available from random missions.

        ConcordDrone = 301,
        PoliceDrone = 182,
        CustomsOfficial = 446,
        Billboard = 323,
        Merchant = 297,
        Mission_Merchant = 1006, //dread pirate scarlet
        FactionWarfareNPC = 288,

        Shuttle = 31,
        Industrial = 28,
        TransportShip = 380,
        Freighter = 513,

        Frigate = 25,
        AssaultShip = 324,
        Destroyer = 420,
        Interdictor = 541,
        Interceptor = 831,
        StealthBomber = 834,
        ElectronicAttackShip = 893,
        PrototypeExplorationShip = 1022,

        Cruiser = 26,
        HeavyAssaultShip = 358,
        Logistics = 832,
        ForceReconShip = 833,
        CombatReconShip = 906,
        HeavyInterdictor = 894,

        Battlecruiser = 419,
        CommandShip = 540,
        StrategicCruiser = 963,

        Battleship = 27,
        EliteBattleship = 381,
        BlackOps = 898,
        Marauder = 900,

        Salvager = 1122,
        DataMiners = 538, //codebreaker, analyzer, data subverter
        TractorBeam = 650,

        Drugs = 313,
        ToxicWaste = 282,
        Slaves = 283,
        SmallArms = 280,
        AccelerationGateKeys = 474,
        Commodities = 526,

        Minerals = 18,

        Plagioclase = 458,
        Spodumain = 461,
        Kernite = 457,
        Hedbergite = 454,
        Arkonor = 450,
        Bistot = 451,
        Pyroxeres = 459,
        Crokite = 452,
        Jaspet = 456,
        Omber = 469,
        Scordite = 460,
        Gneiss = 467,
        Veldspar = 462,
        Hemorphite = 455,
        DarkOchre = 453,
        Ice = 465,

        //
        // various NPCs
        //
        Storyline_Battleship = 523,
        Storyline_Mission_Battleship = 534,
        Asteroid_Angel_Cartel_Battleship = 552,
        Asteroid_Blood_Raiders_Battleship = 556,
        Asteroid_Guristas_Battleship = 560,
        Asteroid_Sanshas_Nation_Battleship = 565,
        Asteroid_Serpentis_Battleship = 570,
        Deadspace_Angel_Cartel_Battleship = 594,
        Deadspace_Blood_Raiders_Battleship = 603,
        Deadspace_Guristas_Battleship = 612,
        Deadspace_Sanshas_Nation_Battleship = 621,
        Deadspace_Serpentis_Battleship = 630,
        Mission_Amarr_Empire_Battleship = 667,
        Mission_Caldari_State_Battleship = 674,
        Mission_Gallente_Federation_Battleship = 680,
        Mission_Khanid_Battleship = 691,
        Mission_CONCORD_Battleship = 697,
        Mission_Mordu_Battleship = 703,
        Mission_Minmatar_Republic_Battleship = 706,
        Asteroid_Rogue_Drone_Battleship = 756,
        Deadspace_Rogue_Drone_Battleship = 802,
        Mission_Generic_Battleships = 816,
        Deadspace_Overseer_Battleship = 821,
        Mission_Thukker_Battleship = 823,
        Asteroid_Rogue_Drone_Commander_Battleship = 844,
        Asteroid_Angel_Cartel_Commander_Battleship = 848,
        Asteroid_Blood_Raiders_Commander_Battleship = 849,
        Asteroid_Guristas_Commander_Battleship = 850,
        Asteroid_Sanshas_Nation_Commander_Battleship = 851,
        Asteroid_Serpentis_Commander_Battleship = 852,
        Mission_Faction_Battleship = 924,
        Asteroid_Angel_Cartel_BattleCruiser = 576,
        Asteroid_Blood_Raiders_BattleCruiser = 578,
        Asteroid_Guristas_BattleCruiser = 580,
        Asteroid_Sanshas_Nation_BattleCruiser = 582,
        Asteroid_Serpentis_BattleCruiser = 584,
        Deadspace_Angel_Cartel_BattleCruiser = 593,
        Deadspace_Blood_Raiders_BattleCruiser = 602,
        Deadspace_Guristas_BattleCruiser = 611,
        Deadspace_Sanshas_Nation_BattleCruiser = 620,
        Deadspace_Serpentis_BattleCruiser = 629,
        Mission_Amarr_Empire_Battlecruiser = 666,
        Mission_Caldari_State_Battlecruiser = 672,
        Mission_Gallente_Federation_Battlecruiser = 681,
        Mission_Minmatar_Republic_Battlecruiser = 685,
        Mission_Khanid_Battlecruiser = 690,
        Mission_CONCORD_Battlecruiser = 696,
        Mission_Mordu_Battlecruiser = 702,
        Asteroid_Rogue_Drone_BattleCruiser = 755,
        Asteroid_Angel_Cartel_Commander_BattleCruiser = 793,
        Asteroid_Blood_Raiders_Commander_BattleCruiser = 795,
        Asteroid_Guristas_Commander_BattleCruiser = 797,
        Deadspace_Rogue_Drone_BattleCruiser = 801,
        Asteroid_Sanshas_Nation_Commander_BattleCruiser = 807,
        Asteroid_Serpentis_Commander_BattleCruiser = 811,
        Mission_Thukker_Battlecruiser = 822,
        Asteroid_Rogue_Drone_Commander_BattleCruiser = 843,
        Storyline_Cruiser = 522,
        Storyline_Mission_Cruiser = 533,
        Asteroid_Angel_Cartel_Cruiser = 551,
        Asteroid_Blood_Raiders_Cruiser = 555,
        Asteroid_Guristas_Cruiser = 561,
        Asteroid_Sanshas_Nation_Cruiser = 566,
        Asteroid_Serpentis_Cruiser = 571,
        Deadspace_Angel_Cartel_Cruiser = 595,
        Deadspace_Blood_Raiders_Cruiser = 604,
        Deadspace_Guristas_Cruiser = 613,
        Deadspace_Sanshas_Nation_Cruiser = 622,
        Deadspace_Serpentis_Cruiser = 631,
        Mission_Amarr_Empire_Cruiser = 668,
        Mission_Caldari_State_Cruiser = 673,
        Mission_Gallente_Federation_Cruiser = 678,
        Mission_Khanid_Cruiser = 689,
        Mission_CONCORD_Cruiser = 695,
        Mission_Mordu_Cruiser = 701,
        Mission_Minmatar_Republic_Cruiser = 705,
        Asteroid_Rogue_Drone_Cruiser = 757,
        Asteroid_Angel_Cartel_Commander_Cruiser = 790,
        Asteroid_Blood_Raiders_Commander_Cruiser = 791,
        Asteroid_Guristas_Commander_Cruiser = 798,
        Deadspace_Rogue_Drone_Cruiser = 803,
        Asteroid_Sanshas_Nation_Commander_Cruiser = 808,
        Asteroid_Serpentis_Commander_Cruiser = 812,
        Mission_Generic_Cruisers = 817,
        Deadspace_Overseer_Cruiser = 820,
        Mission_Thukker_Cruiser = 824,
        Mission_Generic_Battle_Cruisers = 828,
        Asteroid_Rogue_Drone_Commander_Cruiser = 845,
        Mission_Faction_Cruiser = 1006,
        Asteroid_Angel_Cartel_Destroyer = 575,
        Asteroid_Blood_Raiders_Destroyer = 577,
        Asteroid_Guristas_Destroyer = 579,
        Asteroid_Sanshas_Nation_Destroyer = 581,
        Asteroid_Serpentis_Destroyer = 583,
        Deadspace_Angel_Cartel_Destroyer = 596,
        Deadspace_Blood_Raiders_Destroyer = 605,
        Deadspace_Guristas_Destroyer = 614,
        Deadspace_Sanshas_Nation_Destroyer = 623,
        Deadspace_Serpentis_Destroyer = 632,
        Mission_Amarr_Empire_Destroyer = 669,
        Mission_Caldari_State_Destroyer = 676,
        Mission_Gallente_Federation_Destroyer = 679,
        Mission_Minmatar_Republic_Destroyer = 684,
        Mission_Khanid_Destroyer = 688,
        Mission_CONCORD_Destroyer = 694,
        Mission_Mordu_Destroyer = 700,
        Asteroid_Rogue_Drone_Destroyer = 758,
        Asteroid_Angel_Cartel_Commander_Destroyer = 794,
        Asteroid_Blood_Raiders_Commander_Destroyer = 796,
        Asteroid_Guristas_Commander_Destroyer = 799,
        Deadspace_Rogue_Drone_Destroyer = 804,
        Asteroid_Sanshas_Nation_Commander_Destroyer = 809,
        Asteroid_Serpentis_Commander_Destroyer = 813,
        Mission_Thukker_Destroyer = 825,
        Mission_Generic_Destroyers = 829,
        Asteroid_Rogue_Drone_Commander_Destroyer = 846,
        asteroid_angel_cartel_frigate = 550,
        asteroid_blood_raiders_frigate = 557,
        asteroid_guristas_frigate = 562,
        asteroid_sanshas_nation_frigate = 567,
        asteroid_serpentis_frigate = 572,
        deadspace_angel_cartel_frigate = 597,
        deadspace_blood_raiders_frigate = 606,
        deadspace_guristas_frigate = 615,
        deadspace_sanshas_nation_frigate = 624,
        deadspace_serpentis_frigate = 633,
        mission_amarr_empire_frigate = 665,
        mission_caldari_state_frigate = 671,
        mission_gallente_federation_frigate = 677,
        mission_minmatar_republic_frigate = 683,
        mission_khanid_frigate = 687,
        mission_concord_frigate = 693,
        mission_mordu_frigate = 699,
        asteroid_rouge_drone_frigate = 759,
        asteroid_rouge_drone_frigate2 = 805,
        asteroid_angel_cartel_commander_frigate = 789,
        asteroid_blood_raiders_commander_frigate = 792,
        asteroid_guristas_commander_frigate = 800,
        asteroid_sanshas_nation_commander_frigate = 810,
        asteroid_serpentis_commander_frigate = 814,
        mission_generic_frigates = 818,
        mission_thukker_frigate = 826,
        asteroid_rouge_drone_commander_frigate = 847,
    }
}