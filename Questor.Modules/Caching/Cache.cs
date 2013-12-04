// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

using LavishSettingsAPI;

namespace Questor.Modules.Caching
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using System.Xml.XPath;
    using System.Threading;
    using global::Questor.Modules.Actions;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.Logging;
    using DirectEve;
    //using InnerSpaceAPI;

    public class Cache
    {
        /// <summary>
        ///   Singleton implementation
        /// </summary>
        private static Cache _instance = new Cache();

        /// <summary>
        ///   Active Drones //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _activeDrones;

        /// <summary>
        ///   _agent cache //cleared in InvalidateCache 
        /// </summary>
        private DirectAgent _agent;

        /// <summary>
        ///   agentId cache
        /// </summary>
        private long? _agentId;

        /// <summary>
        ///   Current Storyline Mission Agent
        /// </summary>
        public long CurrentStorylineAgentId { get; set; }

        /// <summary>
        ///   Agent blacklist
        /// </summary>
        public List<long> AgentBlacklist;

        /// <summary>
        ///   Approaching cache //cleared in InvalidateCache
        /// </summary>
        private EntityCache _approaching;

        /// <summary>
        ///   BigObjects we are likely to bump into (mainly LCOs) //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _bigObjects;

        /// <summary>
        ///   BigObjects we are likely to bump into (mainly LCOs) //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _gates;

        /// <summary>
        ///   BigObjects we are likely to bump into (mainly LCOs) //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _bigObjectsAndGates;

        /// <summary>
        ///   objects we are likely to bump into (Anything that is not an NPC a wreck or a can) //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _objects;

        /// <summary>
        ///   Returns all non-empty wrecks and all containers //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _containers;

        /// <summary>
        ///   _CombatTarget Entities cache - list of things we have targeted to kill //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _combatTargets;

        /// <summary>
        ///   _PotentialCombatTarget Entities cache - list of things we can kill //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _potentialCombatTargets;

        /// <summary>
        ///   Safespot Bookmark cache (all bookmarks that start with the defined safespot prefix) //cleared in InvalidateCache 
        /// </summary>
        private List<DirectBookmark> _safeSpotBookmarks;

        /// <summary>
        ///   Damaged drones
        /// </summary>
        public IEnumerable<EntityCache> DamagedDrones;

        /// <summary>
        ///   Entities by Id //cleared in InvalidateCache
        /// </summary>
        private readonly Dictionary<long, EntityCache> _entitiesById;

        /// <summary>
        ///   Module cache //cleared in InvalidateCache
        /// </summary>
        private List<ModuleCache> _modules;

        /// <summary>
        ///   Primary Weapon Priority targets (e.g. mission kill targets) //cleared in InvalidateCache
        /// </summary>
        public List<EntityCache> _entitiesthatHaveExploded;

        /// <summary>
        ///  Primary Weapon target chosen by GetBest Target
        /// </summary>
       
        public long? PreferredPrimaryWeaponTargetID;
        private EntityCache _preferredPrimaryWeaponTarget;
        public EntityCache PreferredPrimaryWeaponTarget
        {
            get
            {
                if (Cache.Instance._preferredPrimaryWeaponTarget == null)
                {
                    if (Cache.Instance.PreferredPrimaryWeaponTargetID != null)
                    {
                        Cache.Instance._preferredPrimaryWeaponTarget = Cache.Instance.EntitiesOnGrid.FirstOrDefault(i => i.Id == Cache.Instance.PreferredPrimaryWeaponTargetID);

                        return Cache.Instance._preferredPrimaryWeaponTarget ?? null;    
                    }

                    return null;
                }

                return Cache.Instance._preferredPrimaryWeaponTarget;
            }
            set
            {
                if (value == null)
                {
                    if (Cache.Instance._preferredPrimaryWeaponTarget != null)
                    {
                        Cache.Instance._preferredPrimaryWeaponTarget = null;
                        Cache.Instance.PreferredPrimaryWeaponTargetID = null;
                        if (Settings.Instance.DebugPreferredPrimaryWeaponTarget) Logging.Log("PreferredPrimaryWeaponTarget.Set", "[ null ]", Logging.Debug);
                        return;
                    }
                }
                else if ((Cache.Instance._preferredPrimaryWeaponTarget != null && Cache.Instance._preferredPrimaryWeaponTarget.Id != value.Id) || Cache.Instance._preferredPrimaryWeaponTarget == null)
                {
                    Cache.Instance._preferredPrimaryWeaponTarget = value;
                    Cache.Instance.PreferredPrimaryWeaponTargetID = value.Id;
                    if (Settings.Instance.DebugPreferredPrimaryWeaponTarget) Logging.Log("PreferredPrimaryWeaponTarget.Set", value.Name + " [" + Cache.Instance.MaskedID(value.Id) + "][" + Math.Round(value.Distance / 1000, 0) + "k] isTarget [" + value.IsTarget + "]", Logging.Debug);
                    return;
                }

                //if (Settings.Instance.DebugPreferredPrimaryWeaponTarget) Logging.Log("PreferredPrimaryWeaponTarget", "Cache.Instance._preferredPrimaryWeaponTarget [" + Cache.Instance._preferredPrimaryWeaponTarget.Name + "] is already set (no need to change)", Logging.Debug);
                return;
            }
        }
        
        public DateTime LastPreferredPrimaryWeaponTargetDateTime;

        /// <summary>
        ///   Drone target chosen by GetBest Target
        /// </summary>
        public long? PreferredDroneTargetID;
        private EntityCache _preferredDroneTarget;
        public EntityCache PreferredDroneTarget
        {
            get
            {
                if (Cache.Instance._preferredDroneTarget == null)
                {
                    if (Cache.Instance.PreferredDroneTargetID != null)
                    {
                        Cache.Instance._preferredDroneTarget = Cache.Instance.EntitiesOnGrid.FirstOrDefault(i => i.Id == Cache.Instance.PreferredDroneTargetID);
                        return Cache.Instance._preferredDroneTarget ?? null;
                    }
                }

                return Cache.Instance._preferredDroneTarget;
            }
            set
            {
                if (value == null)
                {
                    if (Cache.Instance._preferredDroneTarget != null)
                    {
                        Cache.Instance._preferredDroneTarget = null;
                        Cache.Instance.PreferredDroneTargetID = null;
                        Logging.Log("PreferredPrimaryWeaponTarget.Set", "[ null ]", Logging.Debug);
                        return;
                    }
                }
                else
                {
                    if (Cache.Instance._preferredDroneTarget != null && _preferredDroneTarget.Id != value.Id)
                    {
                        Cache.Instance._preferredDroneTarget = value;
                        Cache.Instance.PreferredDroneTargetID = value.Id;
                        if (Settings.Instance.DebugGetBestTarget) Logging.Log("PreferredPrimaryWeaponTarget.Set", value + " [" + Cache.Instance.MaskedID(value.Id) + "]", Logging.Debug);
                        return;
                    }
                }
            }
        }

        public DateTime LastPreferredDroneTargetDateTime;
        public long LastDroneTargetID;

        public string OrbitEntityNamed;

        public DirectLocation MissionSolarSystem;

        public string DungeonId;

        /// <summary>
        ///   Star cache //cleared in InvalidateCache
        /// </summary>
        private EntityCache _star;

        /// <summary>
        ///   Station cache //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _stations;

        /// <summary>
        ///   Stargate cache //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _stargates;

        /// <summary>
        ///   Stargate by name //cleared in InvalidateCache
        /// </summary>
        private EntityCache _stargate;

        /// <summary>
        ///   JumpBridges //cleared in InvalidateCache
        /// </summary>
        private IEnumerable<EntityCache> _jumpBridges;

        /// <summary>
        ///   Targeted by cache //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _targetedBy;

        /// <summary>
        ///   Targeting cache //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _targeting;

        /// <summary>
        ///   Targets cache //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _targets;

        /// <summary>
        ///   Aggressed cache //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _aggressed;

        /// <summary>
        ///   IDs in Inventory window tree (on left) //cleared in InvalidateCache
        /// </summary>
        public List<long> _IDsinInventoryTree;

        /// <summary>
        ///   Returns all unlooted wrecks & containers //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _unlootedContainers;

        /// <summary>
        ///   Returns all unlooted wrecks & containers and secure cans //cleared in InvalidateCache
        /// </summary>
        private List<EntityCache> _unlootedWrecksAndSecureCans;

        /// <summary>
        ///   Returns all windows //cleared in InvalidateCache
        /// </summary>
        private List<DirectWindow> _windows;

        /// <summary>
        ///   Returns maxLockedTargets, the minimum between the character and the ship //cleared in InvalidateCache
        /// </summary>
        private int? _maxLockedTargets = null;

        /// <summary>
        ///  Dictionary for cached EWAR target
        /// </summary>
        public HashSet<long> ListOfWarpScramblingEntities = new HashSet<long>();
        public HashSet<long> ListOfJammingEntities = new HashSet<long>();
        public HashSet<long> ListOfTrackingDisruptingEntities = new HashSet<long>();
        public HashSet<long> ListNeutralizingEntities = new HashSet<long>();
        public HashSet<long> ListOfTargetPaintingEntities = new HashSet<long>();
        public HashSet<long> ListOfDampenuingEntities = new HashSet<long>();
        public HashSet<long> ListofWebbingEntities = new HashSet<long>();

        public void DirecteveDispose()
        {
            Logging.Log("Questor", "started calling DirectEve.Dispose()", Logging.White);
            Cache.Instance.DirectEve.Dispose(); //could this hang?
            Logging.Log("Questor", "finished calling DirectEve.Dispose()", Logging.White);
            Process.GetCurrentProcess().Kill();
            Environment.Exit(0);
        }

        public void IterateInvTypes(string module)
        {
            string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (path != null)
            {
                string invtypesXmlFile = System.IO.Path.Combine(path, "InvTypes.xml");
                InvTypesById = new Dictionary<int, InvType>();

                if (!File.Exists(invtypesXmlFile))
                {
                    Logging.Log(module, "IterateInvTypes - unable to find [" + invtypesXmlFile + "]", Logging.White);
                    return;
                }

                try
                {
                    Logging.Log(module, "IterateInvTypes - Loading [" + invtypesXmlFile + "]", Logging.White);
                    InvTypes = XDocument.Load(invtypesXmlFile);
                    if (InvTypes.Root != null)
                    {
                        foreach (XElement element in InvTypes.Root.Elements("invtype"))
                        {
                            InvTypesById.Add((int)element.Attribute("id"), new InvType(element));
                        }
                    }
                }
                catch (Exception exception)
                {
                    Logging.Log(module, "IterateInvTypes - Exception: [" + exception + "]", Logging.Red);
                }
                
            }
            else
            {
                Logging.Log(module, "IterateInvTypes - unable to find [" + System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "]", Logging.White);
            }
        }
        
        public void IterateShipTargetValues(string module)
        {
            string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (path != null)
            {
                string ShipTargetValuesXmlFile = System.IO.Path.Combine(path, "ShipTargetValues.xml");
                ShipTargetValues = new List<ShipTargetValue>();

                if (!File.Exists(ShipTargetValuesXmlFile))
                {
                    Logging.Log(module, "IterateShipTargetValues - unable to find [" + ShipTargetValuesXmlFile + "]", Logging.White);
                    return;
                }

                try
                {
                    Logging.Log(module, "IterateShipTargetValues - Loading [" + ShipTargetValuesXmlFile + "]", Logging.White);
                    XDocument values = XDocument.Load(ShipTargetValuesXmlFile);
                    if (values.Root != null)
                    {
                        foreach (XElement value in values.Root.Elements("ship"))
                        {
                            ShipTargetValues.Add(new ShipTargetValue(value));
                        }
                    }
                }
                catch (Exception exception)
                {
                    Logging.Log(module, "IterateShipTargetValues - Exception: [" + exception + "]", Logging.Red);
                }
            }
        }

        public void IterateUnloadLootTheseItemsAreLootItems(string module)
        {
            string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (path != null)
            {
                string UnloadLootTheseItemsAreLootItemsXmlFile = System.IO.Path.Combine(path, "UnloadLootTheseItemsAreLootItems.xml");
                UnloadLootTheseItemsAreLootById = new Dictionary<int, string>();

                if (!File.Exists(UnloadLootTheseItemsAreLootItemsXmlFile))
                {
                    Logging.Log(module, "IterateUnloadLootTheseItemsAreLootItems - unable to find [" + UnloadLootTheseItemsAreLootItemsXmlFile + "]", Logging.White);
                    return;
                }

                try
                {
                    Logging.Log(module, "IterateUnloadLootTheseItemsAreLootItems - Loading [" + UnloadLootTheseItemsAreLootItemsXmlFile + "]", Logging.White);
                    Cache.Instance.UnloadLootTheseItemsAreLootItems = XDocument.Load(UnloadLootTheseItemsAreLootItemsXmlFile);

                    if (UnloadLootTheseItemsAreLootItems.Root != null)
                    {
                        foreach (XElement element in UnloadLootTheseItemsAreLootItems.Root.Elements("invtype"))
                        {
                            UnloadLootTheseItemsAreLootById.Add((int)element.Attribute("id"), (string)element.Attribute("name"));
                        }
                    }
                }
                catch (Exception exception)
                {
                    Logging.Log(module, "IterateUnloadLootTheseItemsAreLootItems - Exception: [" + exception + "]", Logging.Red);
                }
            }
            else
            {
                Logging.Log(module, "IterateUnloadLootTheseItemsAreLootItems - unable to find [" + System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "]", Logging.White);
            }
        }

        public static int CacheInstances = 0;

        public Cache()
        {
            NextDockAction = DateTime.UtcNow;
            NextUndockAction = DateTime.UtcNow;
            NextAlign = DateTime.UtcNow;
            NextBookmarkPocketAttempt = DateTime.UtcNow;
            NextActivateAction = DateTime.UtcNow;
            NextPainterAction = DateTime.UtcNow;
            NextNosAction = DateTime.UtcNow;
            NextWebAction = DateTime.UtcNow;
            NextWeaponAction = DateTime.UtcNow;
            NextReload = DateTime.UtcNow;
            NextTargetAction = DateTime.UtcNow;
            NextTravelerAction = DateTime.UtcNow;
            NextApproachAction = DateTime.UtcNow;
            NextRemoveBookmarkAction = DateTime.UtcNow;
            NextActivateSupportModules = DateTime.UtcNow;
            NextRepModuleAction = DateTime.UtcNow;
            NextAfterburnerAction = DateTime.UtcNow;
            NextDefenseModuleAction = DateTime.UtcNow;
            LastJettison = DateTime.UtcNow;
            NextArmAction = DateTime.UtcNow;
            NextTractorBeamAction = DateTime.UtcNow;
            NextLootAction = DateTime.UtcNow;
            NextSalvageAction = DateTime.UtcNow;
            //string line = "Cache: new cache instance being instantiated";
            //InnerSpace.Echo(string.Format("{0:HH:mm:ss} {1}", DateTime.UtcNow, line));
            //line = string.Empty;

            _entitiesthatHaveExploded = new List<EntityCache>();
            LastModuleTargetIDs = new Dictionary<long, long>();
            TargetingIDs = new Dictionary<long, DateTime>();
            _entitiesById = new Dictionary<long, EntityCache>();

            //InvTypesById = new Dictionary<int, InvType>();
            //ShipTargetValues = new List<ShipTargetValue>();
            //UnloadLootTheseItemsAreLootById = new Dictionary<int, InvType>();
            
            LootedContainers = new HashSet<long>();
            IgnoreTargets = new HashSet<string>();
            MissionItems = new List<string>();
            ChangeMissionShipFittings = false;
            UseMissionShip = false;
            ArmLoadedCache = false;
            MissionAmmo = new List<Ammo>();
            MissionUseDrones = null;

            PanicAttemptsThisPocket = 0;
            LowestShieldPercentageThisPocket = 100;
            LowestArmorPercentageThisPocket = 100;
            LowestCapacitorPercentageThisPocket = 100;
            PanicAttemptsThisMission = 0;
            LowestShieldPercentageThisMission = 100;
            LowestArmorPercentageThisMission = 100;
            LowestCapacitorPercentageThisMission = 100;
            LastKnownGoodConnectedTime = DateTime.UtcNow;

            Interlocked.Increment(ref CacheInstances);
        }

        ~Cache()
        {
            Interlocked.Decrement(ref CacheInstances);
        }

        /// <summary>
        ///   List of containers that have been looted
        /// </summary>
        public HashSet<long> LootedContainers { get; private set; }

        /// <summary>
        ///   List of targets to ignore
        /// </summary>
        public HashSet<string> IgnoreTargets { get; private set; }

        public static Cache Instance
        {
            get { return _instance; }
        }

        public bool ExitWhenIdle = false;
        public bool StopBot = false;
        public bool DoNotBreakInvul = false;
        public bool UseDrones = true;
        public bool LootAlreadyUnloaded = false;
        public bool MissionLoot = false;
        public bool SalvageAll = false;
        public bool RouteIsAllHighSecBool = false;
        public bool CurrentlyShouldBeSalvaging = false;
        public bool NeedRepair = false;

        public double Wealth { get; set; }

        public double WealthatStartofPocket { get; set; }

        public int PocketNumber { get; set; }

        public int StackLootHangarAttempts { get; set; }
        public int StackAmmoHangarAttempts { get; set; }

        public string ScheduleCharacterName; //= Program._character;
        public bool OpenWrecks = false;
        public bool NormalApproach = true;
        public bool CourierMission = false;
        public bool RepairAll = false;
        public bool doneUsingRepairWindow = false;
        public string MissionName = "";
        public int MissionsThisSession = 0;
        public int StopSessionAfterMissionNumber = int.MaxValue;
        public bool ConsoleLogOpened = false;
        public int TimeSpentReloading_seconds = 0;
        public int TimeSpentInMission_seconds = 0;
        public int TimeSpentInMissionInRange = 0;
        public int TimeSpentInMissionOutOfRange = 0;
        public int GreyListedMissionsDeclined = 0;
        public string LastGreylistMissionDeclined = string.Empty;
        public int BlackListedMissionsDeclined = 0;
        public string LastBlacklistMissionDeclined = string.Empty;

        public long AmmoHangarID = -99;
        public long LootHangarID = -99;

        public DirectAgentMission Mission;

        public DirectAgentMission FirstAgentMission;

        public IEnumerable<DirectAgentMission> myAgentMissionList { get; set; }

        public bool DronesKillHighValueTargets { get; set; }

        public bool InMission { get; set; }

        public bool normalNav = true;  //Do we want to bypass normal navigation for some reason?
        public bool onlyKillAggro { get; set; }

        public DateTime QuestorStarted_DateTime = DateTime.UtcNow;

        public DateTime NextSalvageTrip = DateTime.UtcNow;
        public DateTime LastStackAmmoHangar = DateTime.UtcNow;
        public DateTime LastStackLootHangar = DateTime.UtcNow;
        public DateTime LastStackItemHangar = DateTime.UtcNow;
        public DateTime LastStackShipsHangar = DateTime.UtcNow;
        public DateTime LastStackCargohold = DateTime.UtcNow;
        public DateTime LastStackLootContainer = DateTime.UtcNow;
        public DateTime LastAccelerationGateDetected = DateTime.UtcNow;

        public int StackLoothangarAttempts { get; set; }
        public int StackAmmohangarAttempts { get; set; }
        public int StackItemhangarAttempts { get; set; }
        
        public bool MissionXMLIsAvailable { get; set; }

        public string MissionXmlPath { get; set; }

        public XDocument InvTypes;
        public XDocument UnloadLootTheseItemsAreLootItems;
        public XDocument InvIgnore;
        public string Path;

        public bool _isCorpInWar = false;
        public DateTime nextCheckCorpisAtWar = DateTime.UtcNow;

        public bool IsCorpInWar
        {
            get
            {
                if (DateTime.UtcNow > nextCheckCorpisAtWar)
                {
                    bool war = DirectEve.Me.IsAtWar;
                    Cache.Instance._isCorpInWar = war;

                    nextCheckCorpisAtWar = DateTime.UtcNow.AddMinutes(15);
                    if (!_isCorpInWar)
                    {
                        if (Settings.Instance.DebugWatchForActiveWars) Logging.Log("IsCorpInWar", "Your corp is not involved in any wars (yet)", Logging.Green);
                    }
                    else
                    {
                        if (Settings.Instance.DebugWatchForActiveWars) Logging.Log("IsCorpInWar", "Your corp is involved in a war, be careful", Logging.Orange);
                    }

                    return _isCorpInWar;
                }
                
                return _isCorpInWar; 
            }
        }

        public bool LocalSafe(int maxBad, double stand)
        {
            int number = 0;
            DirectChatWindow local = (DirectChatWindow)GetWindowByName("Local");

            try
            {
                foreach (DirectCharacter localMember in local.Members)
                {
                    float[] alliance = { DirectEve.Standings.GetPersonalRelationship(localMember.AllianceId), DirectEve.Standings.GetCorporationRelationship(localMember.AllianceId), DirectEve.Standings.GetAllianceRelationship(localMember.AllianceId) };
                    float[] corporation = { DirectEve.Standings.GetPersonalRelationship(localMember.CorporationId), DirectEve.Standings.GetCorporationRelationship(localMember.CorporationId), DirectEve.Standings.GetAllianceRelationship(localMember.CorporationId) };
                    float[] personal = { DirectEve.Standings.GetPersonalRelationship(localMember.CharacterId), DirectEve.Standings.GetCorporationRelationship(localMember.CharacterId), DirectEve.Standings.GetAllianceRelationship(localMember.CharacterId) };

                    if (alliance.Min() <= stand || corporation.Min() <= stand || personal.Min() <= stand)
                    {
                        Logging.Log("Cache.LocalSafe", "Bad Standing Pilot Detected: [ " + localMember.Name + "] " + " [ " + number + " ] so far... of [ " + maxBad + " ] allowed", Logging.Orange);
                        number++;
                    }

                    if (number > maxBad)
                    {
                        Logging.Log("Cache.LocalSafe", "[" + number + "] Bad Standing pilots in local, We should stay in station", Logging.Orange);
                        return false;
                    }
                }
            }
            catch (Exception exception)
            {
                Logging.Log("LocalSafe", "Exception [" + exception + "]", Logging.Debug);
            }
            
            return true;
        }

        public DirectEve DirectEve { get; set; }

        public Dictionary<int, InvType> InvTypesById { get; private set; }

        public Dictionary<int, String> UnloadLootTheseItemsAreLootById { get; private set; }


        /// <summary>
        ///   List of ship target values, higher target value = higher kill priority
        /// </summary>
        public List<ShipTargetValue> ShipTargetValues { get; private set; }

        /// <summary>
        ///   Best damage type for the mission
        /// </summary>
        public DamageType DamageType { get; set; }

        /// <summary>
        ///   Best orbit distance for the mission
        /// </summary>
        public int OrbitDistance { get; set; }

        /// <summary>
        ///   Current OptimalRange during the mission (effected by e-war)
        /// </summary>
        public int OptimalRange { get; set; }

        /// <summary>
        ///   Force Salvaging after mission
        /// </summary>
        public bool AfterMissionSalvaging { get; set; }


        //cargo = 

        private DirectContainer _currentShipsCargo;

        public DirectContainer CurrentShipsCargo
        {
            get
            {
                try
                {
                    if ((Cache.Instance.InSpace && DateTime.UtcNow > Cache.Instance.LastInStation.AddSeconds(10)) || (Cache.Instance.InStation && DateTime.UtcNow > Cache.Instance.LastInSpace.AddSeconds(10)))
                    {
                        if (_currentShipsCargo == null)
                        {
                            _currentShipsCargo = Cache.Instance.DirectEve.GetShipsCargo();
                        }

                        if (_currentShipsCargo != null)
                        {
                            if (Cache.Instance._currentShipsCargo.Window == null)
                            {
                                // No?, then command it to open if we have not already tried to open it very recently
                                if (DateTime.UtcNow > Cache.Instance.NextOpenCurrentShipsCargoWindowAction)
                                {
                                    Cache.Instance.NextOpenCurrentShipsCargoWindowAction = DateTime.UtcNow.AddMilliseconds(1000 + Cache.Instance.RandomNumber(0, 2000));
                                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenCargoHoldOfActiveShip);
                                    if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenCargoHoldOfActiveShip);", Logging.Debug); 
                                    _currentShipsCargo = null;
                                    return _currentShipsCargo;
                                }

                                if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "Waiting on NextOpenCurrentShipsCargoWindowAction [" + DateTime.UtcNow.Subtract(Cache.Instance.NextOpenCurrentShipsCargoWindowAction).Seconds + "sec]", Logging.Debug);
                                _currentShipsCargo = null;
                                return _currentShipsCargo;
                            }

                            if (!Cache.Instance._currentShipsCargo.Window.IsReady)
                            {
                                Logging.Log("CurrentShipsCargo", "cargo window is not ready", Logging.White);
                                _currentShipsCargo = null;
                                return _currentShipsCargo;
                            }

                            if (!Cache.Instance._currentShipsCargo.Window.IsPrimary())
                            {
                                if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "DebugCargoHold: cargoHold window is ready and is a secondary inventory window", Logging.DebugHangars);
                                    
                                if (_currentShipsCargo != null)
                                {
                                    //if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "_currentShipsCargo is not null", Logging.Debug);
                                    return _currentShipsCargo;
                                }

                                if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "_currentShipsCargo is null!?", Logging.Debug);
                                return null;
                            }

                            if (Cache.Instance._currentShipsCargo.Window.IsPrimary())
                            {
                                if (DateTime.UtcNow > Cache.Instance.NextOpenCargoAction)
                                {
                                    if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "DebugCargoHold: Opening cargoHold window as secondary", Logging.DebugHangars);
                                    Cache.Instance.NextOpenCargoAction = DateTime.UtcNow.AddMilliseconds(1000 + Cache.Instance.RandomNumber(0, 2000));
                                    Cache.Instance._currentShipsCargo.Window.OpenAsSecondary();
                                    _currentShipsCargo = null;
                                    return _currentShipsCargo;
                                }

                                if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "_currentShipsCargo is null - Waiting on NextOpenCargoAction [" + DateTime.UtcNow.Subtract(Cache.Instance.NextOpenCargoAction).Seconds + "sec]", Logging.Debug);
                                _currentShipsCargo = null;
                                return _currentShipsCargo;
                            }

                            return _currentShipsCargo;
                        }

                        if (_currentShipsCargo == null)
                        {
                            if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "_currentShipsCargo is null", Logging.Debug);    
                        }
                            
                        return _currentShipsCargo;
                    }

                    int EntityCount = 0;
                    if (Cache.Instance.Entities.Any())
                    {
                        EntityCount = Cache.Instance.Entities.Count();
                    }

                    if (Settings.Instance.DebugCargoHold) Logging.Log("CurrentShipsCargo", "Cache.Instance.MyShipEntity is null: We have a total of [" + EntityCount + "] entities available at the moment.", Logging.Debug);
                    return null;
                   
                }
                catch (Exception exception)
                {
                    Logging.Log("CurrentShipsCargo", "Unable to complete ReadyCargoHold [" + exception + "]", Logging.Teal);
                    return null;
                }
            }
        }

        public DirectContainer _containerInSpace { get; set; }

        public DirectContainer ContainerInSpace
        {
            get
            {
                if (_containerInSpace == null)
                {
                    //_containerInSpace = 
                    return _containerInSpace;
                }

                return _containerInSpace;
            }
            set { _containerInSpace = value; }
        }


        public DirectActiveShip ActiveShip
        {
            get
            {
                return Cache.Instance.DirectEve.ActiveShip;
            }
        }

        private double? _maxDroneRange;

        public double MaxDroneRange
        {
            get
            {
                if (_maxDroneRange == null)
                {
                    _maxDroneRange = Math.Min(Settings.Instance.DroneControlRange, Cache.Instance.MaxTargetRange);
                    return _maxDroneRange ?? 0;
                }

                return _maxDroneRange ?? 0;
            }
        }

        private double? _maxrange;
        
        public double MaxRange
        {
            get
            {
                if (_maxrange == null)
                {
                    _maxrange = Math.Min(Cache.Instance.WeaponRange, Cache.Instance.MaxTargetRange);
                    return _maxrange ?? 0;
                }

                return _maxrange ?? 0;
            }
        }

        private double? _maxTargetRange;

        public double MaxTargetRange
        {
            get
            {
                if (_maxTargetRange == null)
                {
                    _maxTargetRange = Cache.Instance.ActiveShip.MaxTargetRange;
                    return _maxTargetRange ?? 0;
                }

                return _maxTargetRange ?? 0;
            }
        }

        public double LowValueTargetsHaveToBeWithinDistance
        {
            get
            {
                if (Cache.Instance.UseDrones && Cache.Instance.MaxDroneRange != 0)
                {
                    return Cache.Instance.MaxDroneRange;
                }
                
                //
                // if we are not using drones return min range (Weapons or targeting range whatever is lower)
                //
                return Cache.Instance.MaxRange;
                
            }
        }

        /// <summary>
        ///   Returns the maximum weapon distance
        /// </summary>
        public int WeaponRange
        {
            get
            {
                // Get ammo based on current damage type
                IEnumerable<Ammo> ammo = Settings.Instance.Ammo.Where(a => a.DamageType == DamageType).ToList();

                try
                {
                    // Is our ship's cargo available?
                    if ((Cache.Instance.CurrentShipsCargo != null) && (Cache.Instance.CurrentShipsCargo.IsValid))
                    {
                        ammo = ammo.Where(a => Cache.Instance.CurrentShipsCargo.Items.Any(i => a.TypeId == i.TypeId && i.Quantity >= Settings.Instance.MinimumAmmoCharges));
                    }
                    else
                    {
                        return System.Convert.ToInt32(Cache.Instance.MaxTargetRange);
                    }

                    // Return ship range if there's no ammo left
                    if (!ammo.Any())
                    {
                        return System.Convert.ToInt32(Cache.Instance.MaxTargetRange);
                    }

                    return ammo.Max(a => a.Range);
                }
                catch (Exception ex)
                {
                    if (Settings.Instance.DebugExceptions) Logging.Log("Cache.WeaponRange", "exception was:" + ex.Message, Logging.Teal);

                    // Return max range
                    if (Cache.Instance.ActiveShip != null)
                    {
                        return System.Convert.ToInt32(Cache.Instance.MaxTargetRange);
                    }

                    return 0;
                }
            }
        }

        /// <summary>
        ///   Last target for a certain module
        /// </summary>
        public Dictionary<long, long> LastModuleTargetIDs { get; private set; }

        /// <summary>
        ///   Targeting delay cache (used by LockTarget)
        /// </summary>
        public Dictionary<long, DateTime> TargetingIDs { get; private set; }

        /// <summary>
        ///   Used for Drones to know that it should retract drones
        /// </summary>
        public bool IsMissionPocketDone { get; set; }

        public string ExtConsole { get; set; }

        public string ConsoleLog { get; set; }

        public string ConsoleLogRedacted { get; set; }

        public bool AllAgentsStillInDeclineCoolDown { get; set; }

        public string _agentName = "";

        public DateTime NextWindowAction { get; set; }
        public DateTime NextGetAgentMissionAction { get; set; }
        public DateTime NextOpenContainerInSpaceAction { get; set; }
        public DateTime NextOpenLootContainerAction { get; set; }
        public DateTime NextOpenCorpBookmarkHangarAction { get; set; }
        public DateTime NextDroneBayAction { get; set; }
        public DateTime NextOpenHangarAction { get; set; }
        public DateTime NextOpenCargoAction { get; set; }
        public DateTime NextOpenCurrentShipsCargoWindowAction { get; set; }
        public DateTime NextMakeActiveTargetAction { get; set; }
        public DateTime NextArmAction { get; set; }
        public DateTime NextSalvageAction { get; set; }
        public DateTime NextTractorBeamAction { get; set; }
        public DateTime NextLootAction { get; set; }
        public DateTime LastJettison { get; set; }
        public DateTime NextDefenseModuleAction { get; set; }
        public DateTime NextAfterburnerAction { get; set; }
        public DateTime NextRepModuleAction { get; set; }
        public DateTime NextActivateSupportModules { get; set; }
        public DateTime NextRemoveBookmarkAction { get; set; }
        public DateTime NextApproachAction { get; set; }
        public DateTime NextOrbit { get; set; }
        public DateTime NextWarpTo { get; set; }
        public DateTime NextTravelerAction { get; set; }
        public DateTime NextTargetAction { get; set; }
        public DateTime NextReload { get; set; }
        public DateTime NextWeaponAction { get; set; }
        public DateTime NextWebAction { get; set; }
        public DateTime NextRemoteRepairAction { get; set; }
        public DateTime NextWarpDisruptorAction { get; set; }
        public DateTime NextNosAction { get; set; }
        public DateTime NextPainterAction { get; set; }
        public DateTime NextActivateAction { get; set; }
        public DateTime NextBookmarkPocketAttempt { get; set; }
        public DateTime NextAlign { get; set; }
        public DateTime NextUndockAction { get; set; }
        public DateTime NextDockAction { get; set; }
        public DateTime NextJumpAction { get; set; }
        public DateTime NextWarpAction { get; set; }
        public DateTime NextDroneRecall { get; set; }
        public DateTime NextStartupAction { get; set; }
        public DateTime NextRepairItemsAction { get; set; }
        public DateTime NextRepairDronesAction { get; set; }
        public DateTime NextEVEMemoryManagerAction { get; set; }
        public DateTime NextGetBestCombatTarget { get; set; }
        public DateTime NextGetBestDroneTarget { get; set; }
        public DateTime NextSkillTrainerProcessState;
        public DateTime NextSkillTrainerAction = DateTime.MinValue;
        public DateTime NextBastionAction { get; set; }
        public DateTime NextBastionModeDeactivate { get; set; }
        public DateTime NextLPStoreAction { get; set; }

        public DateTime LastLocalWatchAction = DateTime.UtcNow;
        public DateTime LastWalletCheck = DateTime.UtcNow;
        public DateTime LastScheduleCheck = DateTime.UtcNow;

        public DateTime LastUpdateOfSessionRunningTime;
        public DateTime NextInSpaceorInStation;
        public DateTime NextTimeCheckAction = DateTime.UtcNow;
        public DateTime NextQMJobCheckAction = DateTime.UtcNow;

        public DateTime LastFrame = DateTime.UtcNow;
        public DateTime LastSessionIsReady = DateTime.UtcNow;
        public DateTime LastLogMessage = DateTime.UtcNow;

        public int WrecksThisPocket;
        public int WrecksThisMission;
        public DateTime LastLoggingAction = DateTime.MinValue;

        public DateTime LastSessionChange = DateTime.UtcNow;

        private bool _paused;
        public bool Paused
        {
            get
            {
                return _paused;
            }
            set
            {
                _paused = value;
                Cache.Instance.ClearPerPocketCache();
            }
        }

        public int RepairCycleTimeThisPocket { get; set; }

        public int PanicAttemptsThisPocket { get; set; }

        private int GetShipsDroneBayAttempts { get; set; }

        public double LowestShieldPercentageThisMission { get; set; }

        public double LowestArmorPercentageThisMission { get; set; }

        public double LowestCapacitorPercentageThisMission { get; set; }

        public double LowestShieldPercentageThisPocket { get; set; }

        public double LowestArmorPercentageThisPocket { get; set; }

        public double LowestCapacitorPercentageThisPocket { get; set; }

        public int PanicAttemptsThisMission { get; set; }

        public DateTime StartedBoosting { get; set; }

        public int RepairCycleTimeThisMission { get; set; }

        public DateTime LastKnownGoodConnectedTime { get; set; }

        public long TotalMegaBytesOfMemoryUsed { get; set; }

        public double MyWalletBalance { get; set; }

        public string CurrentPocketAction { get; set; }

        public float AgentEffectiveStandingtoMe;
        public string AgentEffectiveStandingtoMeText;
        public float AgentCorpEffectiveStandingtoMe;
        public float AgentFactionEffectiveStandingtoMe;
        public float StandingUsedToAccessAgent;
        

        public bool MissionBookmarkTimerSet = false;
        public DateTime MissionBookmarkTimeout = DateTime.MaxValue;

        public long AgentStationID { get; set; }

        public string AgentStationName { get; set; }

        public long AgentSolarSystemID { get; set; }

        public string AgentSolarSystemName { get; set; }

        public string CurrentAgentText = string.Empty;

        public string CurrentAgent
        {
            get
            {
                if (Settings.Instance.CharacterXMLExists)
                {
                    if (_agentName == "")
                    {
                        try
                        {
                            _agentName = SwitchAgent();
                            Logging.Log("Cache.CurrentAgent", "[ " + _agentName + " ] AgentID [ " + AgentId + " ]", Logging.White);
                            Cache.Instance.CurrentAgentText = CurrentAgent;
                        }
                        catch (Exception ex)
                        {
                            Logging.Log("Cache.AgentId", "Exception [" + ex + "]",Logging.Debug);
                            return "";
                        }
                    }

                    return _agentName;
                }
                return "";
            }
            set
            {
                _agentName = value;
            }
        }

        private static readonly Func<DirectAgent, DirectSession, bool> AgentInThisSolarSystemSelector = (a, s) => a.SolarSystemId == s.SolarSystemId;
        private static readonly Func<DirectAgent, DirectSession, bool> AgentInThisStationSelector = (a, s) => a.StationId == s.StationId;

        private string SelectNearestAgent()
        {
            try
            {
                DirectAgentMission mission = null;

                foreach (AgentsList potentialAgent in Settings.Instance.AgentsList)
                {
                    if (Cache.Instance.DirectEve.AgentMissions.Any(m => m.State == (int)MissionState.Accepted && !m.Important && DirectEve.GetAgentById(m.AgentId).Name == potentialAgent.Name))
                    {
                        mission = Cache.Instance.DirectEve.AgentMissions.FirstOrDefault(m => m.State == (int)MissionState.Accepted && !m.Important && DirectEve.GetAgentById(m.AgentId).Name == potentialAgent.Name);
                    }
                }

                //DirectAgentMission mission = DirectEve.AgentMissions.FirstOrDefault(x => x.State == (int)MissionState.Accepted && !x.Important);
                if (mission == null && Cache.Instance.DirectEve.Session.IsReady)
                {
                    try
                    {
                        Func<DirectAgent, DirectSession, bool> selector = DirectEve.Session.IsInSpace ? AgentInThisSolarSystemSelector : AgentInThisStationSelector;
                        var nearestAgent = Settings.Instance.AgentsList
                            .Select(x => new { Agent = x, DirectAgent = DirectEve.GetAgentByName(x.Name) })
                            .FirstOrDefault(x => selector(x.DirectAgent, DirectEve.Session));

                        if (nearestAgent != null)
                        {
                            return nearestAgent.Agent.Name;
                        }


                        if (Settings.Instance.AgentsList.OrderBy(j => j.Priorit).Any())
                        {
                            AgentsList __HighestPriorityAgentInList = Settings.Instance.AgentsList.OrderBy(j => j.Priorit).FirstOrDefault();
                            if (__HighestPriorityAgentInList != null)
                            {
                                return __HighestPriorityAgentInList.Name;
                            }
                        }

                        return null;
                    }
                    catch (NullReferenceException) {}
                }

                if (mission != null)
                {
                    return DirectEve.GetAgentById(mission.AgentId).Name;
                }
            }
            catch (Exception ex)
            {
                Logging.Log("SelectNearestAgent", "Exception [" + ex + "]", Logging.Debug);
            }

            return null;
        }

        private string SelectFirstAgent()
        {
            Func<DirectAgent, DirectSession, bool> selector = Cache.Instance.InSpace ? AgentInThisSolarSystemSelector : AgentInThisStationSelector;
            AgentsList FirstAgent = Settings.Instance.AgentsList.OrderBy(j => j.Priorit).FirstOrDefault();
            if (FirstAgent == null)
            {
                Logging.Log("SelectFirstAgent", "Unable to find the first agent, are your agents configured?", Logging.Debug);
            }
            if (FirstAgent != null)
            {
                return FirstAgent.Name;    
            }

            return null;
        }

        public string SwitchAgent()
        {
            if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.PrepareStorylineSwitchAgents)
            {
                return SelectFirstAgent();
            }

            if (_agentName == "")
            {
                // it means that this is first switch for Questor, so we'll check missions, then station or system for agents.
                AllAgentsStillInDeclineCoolDown = false;
                return SelectNearestAgent();
            }

            AgentsList agent = Settings.Instance.AgentsList.OrderBy(j => j.Priorit).FirstOrDefault(i => DateTime.UtcNow >= i.DeclineTimer);
            if (agent == null)
            {
                try
                {
                    agent = Settings.Instance.AgentsList.OrderBy(j => j.Priorit).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    Logging.Log("Cache.SwitchAgent", "Unable to process agent section of [" + Settings.Instance.CharacterSettingsPath + "] make sure you have a valid agent listed! Pausing so you can fix it. [" + ex.Message + "]",Logging.Debug);
                    Cache.Instance.Paused = true;
                }
                AllAgentsStillInDeclineCoolDown = true; //this literally means we have no agents available at the moment (decline timer likely)
            }
            else
            {
                AllAgentsStillInDeclineCoolDown = false; //this literally means we DO have agents available (at least one agents decline timer has expired and is clear to use)
            }

            if (agent != null) return agent.Name;
            return null;
        }

        public long AgentId
        {
            get
            {
                if (Settings.Instance.CharacterXMLExists)
                {
                    try
                    {
                        if (_agent == null) _agent = DirectEve.GetAgentByName(CurrentAgent);
                        _agentId = _agent.AgentId;

                        return (long)_agentId;
                    }
                    catch (Exception ex)
                    {
                        Logging.Log("Cache.AgentId", "Is your Agent List defined properly? Unable to get agent details for the Agent Named [" + CurrentAgent + "][" + ex.Message + "]", Logging.Debug);
                        return -1;
                    }
                }
                return -1;
            }
        }

        public DirectAgent Agent
        {
            get
            {
                if (Settings.Instance.CharacterXMLExists)
                {
                    try
                    {
                        if (_agent == null) _agent = DirectEve.GetAgentByName(CurrentAgent);
                        if (_agent != null)
                        {
                            _agentId = _agent.AgentId;
                            //Logging.Log("Cache: CurrentAgent", "Processing Agent Info...", Logging.White);
                            Cache.Instance.AgentStationName = Cache.Instance.DirectEve.GetLocationName(Cache.Instance._agent.StationId);
                            Cache.Instance.AgentStationID = Cache.Instance._agent.StationId;
                            Cache.Instance.AgentSolarSystemName = Cache.Instance.DirectEve.GetLocationName(Cache.Instance._agent.SolarSystemId);
                            Cache.Instance.AgentSolarSystemID = Cache.Instance._agent.SolarSystemId;
                            //Logging.Log("Cache: CurrentAgent", "AgentStationName [" + Cache.Instance.AgentStationName + "]", Logging.White);
                            //Logging.Log("Cache: CurrentAgent", "AgentStationID [" + Cache.Instance.AgentStationID + "]", Logging.White);
                            //Logging.Log("Cache: CurrentAgent", "AgentSolarSystemName [" + Cache.Instance.AgentSolarSystemName + "]", Logging.White);
                            //Logging.Log("Cache: CurrentAgent", "AgentSolarSystemID [" + Cache.Instance.AgentSolarSystemID + "]", Logging.White);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log("Cache.Agent", "Unable to process agent section of [" + Settings.Instance.CharacterSettingsPath + "] make sure you have a valid agent listed! Pausing so you can fix it. [" + ex.Message + "]", Logging.Debug);
                        Cache.Instance.Paused = true;
                    }
                    if (_agentId != null) return _agent ?? (_agent = DirectEve.GetAgentById(_agentId.Value));
                }
                return null;
            }
        }


        public IEnumerable<ItemCache> _modulesAsItemCache;

        public IEnumerable<ItemCache> ModulesAsItemCache
        {
            get
            {
                try
                {
                    if (_modulesAsItemCache == null && Cache.Instance.ActiveShip.GroupId != (int)Group.Shuttle)
                    {
                        DirectContainer _modulesAsContainer = Cache.Instance.DirectEve.GetShipsModules();
                        if (_modulesAsContainer != null && _modulesAsContainer.Items.Any())
                        {
                            _modulesAsItemCache = _modulesAsContainer.Items.Select(i => new ItemCache(i)).ToList();
                            if (_modulesAsItemCache.Any())
                            {
                                return _modulesAsItemCache;    
                            }

                            return null;
                        }

                        return null;
                    }

                    return _modulesAsItemCache;
                }
                catch (Exception exception)
                {
                    Logging.Log("Cache.ModulesAsContainer", "Exception [" + exception + "]", Logging.Debug);
                }

                return _modulesAsItemCache;
            }
        }

        public IEnumerable<ModuleCache> Modules
        {
            get
            {
                try
                {
                    if (_modules == null || !_modules.Any())
                    {
                        _modules = Cache.Instance.DirectEve.Modules.Select(m => new ModuleCache(m)).ToList();
                    }

                    return _modules;
                }
                catch (Exception exception)
                {
                    Logging.Log("Cache.Modules", "Exception [" + exception + "]", Logging.Debug);
                }

                return _modules;
            }
        }

        //
        // this CAN and should just list all possible weapon system groupIDs
        //
        public IEnumerable<ModuleCache> Weapons
        {
            get
            {
                if (Cache.Instance.MissionWeaponGroupId != 0)
                {
                    return Modules.Where(m => m.GroupId == Cache.Instance.MissionWeaponGroupId);
                }

                return Modules.Where(m => m.GroupId == Settings.Instance.WeaponGroupId); // ||
                //m.GroupId == (int)Group.ProjectileWeapon ||
                //m.GroupId == (int)Group.EnergyWeapon ||
                //m.GroupId == (int)Group.HybridWeapon ||
                //m.GroupId == (int)Group.CruiseMissileLaunchers ||
                //m.GroupId == (int)Group.RocketLaunchers ||
                //m.GroupId == (int)Group.StandardMissileLaunchers ||
                //m.GroupId == (int)Group.TorpedoLaunchers ||
                //m.GroupId == (int)Group.AssaultMissilelaunchers ||
                //m.GroupId == (int)Group.HeavyMissilelaunchers ||
                //m.GroupId == (int)Group.DefenderMissilelaunchers);
            }
        }

        public int MaxLockedTargets
        {
            get
            {
                if (_maxLockedTargets == null)
                {
                    _maxLockedTargets = Math.Min(Cache.Instance.DirectEve.Me.MaxLockedTargets, Cache.Instance.ActiveShip.MaxLockedTargets);
                    return _maxLockedTargets ?? 0;
                }

                return _maxLockedTargets ?? 0;
            }
        }

        public IEnumerable<EntityCache> Containers
        {
            get
            {
                return _containers ?? (_containers = Cache.Instance.EntitiesOnGrid.Where(e =>
                           e.IsContainer && 
                           e.HaveLootRights && 
                          (e.GroupId != (int)Group.Wreck || !e.IsWreckEmpty) &&
                          (e.Name != "Abandoned Container")).ToList());
            }
        }

        public IEnumerable<EntityCache> ContainersIgnoringLootRights
        {
            get
            {
                return _containers ?? (_containers = Cache.Instance.EntitiesOnGrid.Where(e =>
                           e.IsContainer &&
                          (e.GroupId != (int)Group.Wreck || !e.IsWreckEmpty) &&
                          (e.Name != "Abandoned Container")).ToList());
            }
        }

        public IEnumerable<EntityCache> Wrecks
        {
            get { return _containers ?? (_containers = Cache.Instance.EntitiesOnGrid.Where(e => (e.GroupId == (int)Group.Wreck)).ToList()); }
        }

        public IEnumerable<EntityCache> UnlootedContainers
        {
            get
            {
                return _unlootedContainers ?? (_unlootedContainers = Cache.Instance.EntitiesOnGrid.Where(e =>
                          e.IsContainer &&
                          e.HaveLootRights &&
                          (!LootedContainers.Contains(e.Id) || e.GroupId == (int)Group.Wreck)).OrderBy(
                              e => e.Distance).
                              ToList());
            }
        }

        //This needs to include items you can steal from (thus gain aggro)
        public IEnumerable<EntityCache> UnlootedWrecksAndSecureCans
        {
            get
            {
                return _unlootedWrecksAndSecureCans ?? (_unlootedWrecksAndSecureCans = Cache.Instance.EntitiesOnGrid.Where(e =>
                          (e.GroupId == (int)Group.Wreck || e.GroupId == (int)Group.SecureContainer ||
                           e.GroupId == (int)Group.AuditLogSecureContainer ||
                           e.GroupId == (int)Group.FreightContainer) && !e.IsWreckEmpty).OrderBy(e => e.Distance).
                          ToList());
            }
        }

        public IEnumerable<EntityCache> _TotalTargetsandTargeting;

        public IEnumerable<EntityCache> TotalTargetsandTargeting
        {
            get
            {
                if (_TotalTargetsandTargeting == null)
                {
                    _TotalTargetsandTargeting = Cache.Instance.Targets.Concat(Cache.Instance.Targeting.Where(i => !i.IsTarget));
                    return _TotalTargetsandTargeting;
                }

                return _TotalTargetsandTargeting;
            }
        }

        public IEnumerable<EntityCache> Targets
        {
            get
            {
                if (_targets == null)
                {
                    _targets = Cache.Instance.EntitiesOnGrid.Where(e => e.IsTarget).ToList();
                }
                
                // Remove the target info from the TargetingIDs Queue (its been targeted)
                foreach (EntityCache target in _targets.Where(t => TargetingIDs.ContainsKey(t.Id)))
                {
                    TargetingIDs.Remove(target.Id);
                }

                return _targets;
            }
        }

        public IEnumerable<EntityCache> Targeting
        {
            get
            {
                if (_targeting == null)
                {
                    _targeting = Cache.Instance.EntitiesOnGrid.Where(e => e.IsTargeting || Cache.Instance.TargetingIDs.ContainsKey(e.Id)).ToList();
                }

                if (_targeting.Any())
                {
                    return _targeting;
                }

                return new List<EntityCache>();
            }
        }

        public List<long> IDsinInventoryTree
        {
            get
            {
                Logging.Log("Cache.IDsinInventoryTree", "Refreshing IDs from inventory tree, it has been longer than 30 seconds since the last refresh", Logging.Teal);
                return _IDsinInventoryTree ?? (_IDsinInventoryTree = Cache.Instance.PrimaryInventoryWindow.GetIdsFromTree(false));
            }
        }

        public IEnumerable<EntityCache> TargetedBy
        {
            get { return _targetedBy ?? (_targetedBy = Cache.Instance.PotentialCombatTargets.Where(e => e.IsTargetedBy).ToList()); }
        }

        public IEnumerable<EntityCache> Aggressed
        {
            get { return _aggressed ?? (_aggressed = Cache.Instance.PotentialCombatTargets.Where(e => e.IsAttacking).ToList()); }
        }

        //
        // entities that have been locked (or are being locked now)
        // entities that are IN range
        // entities that eventually we want to shoot (and now that they are locked that will happen shortly)
        //
        public IEnumerable<EntityCache> combatTargets
        {
            get
            {
                if (_combatTargets == null)
                {
                    //List<EntityCache>
                    if (Cache.Instance.InSpace)
                    {
                        if (_combatTargets == null)
                        {
                            List<EntityCache> targets = new List<EntityCache>();
                            targets.AddRange(Cache.Instance.Targets);
                            targets.AddRange(Cache.Instance.Targeting);

                            _combatTargets = targets.Where(e => e.CategoryId == (int)CategoryID.Entity && e.Distance < (double)Distances.OnGridWithMe
                                                                && !e.IsIgnored
                                                                && (!e.IsSentry || (e.IsSentry && Settings.Instance.KillSentries) || (e.IsSentry && e.IsEwarTarget))
                                                                && (e.IsNpc || e.IsNpcByGroupID)
                                                                && e.Distance < Cache.Instance.MaxRange
                                                                && !e.IsContainer
                                                                && !e.IsFactionWarfareNPC
                                                                && !e.IsEntityIShouldLeaveAlone
                                                                && !e.IsBadIdea
                                                                && !e.IsCelestial
                                                                && !e.IsAsteroid)
                                                                .ToList();

                            return _combatTargets;
                        }

                        return _combatTargets;
                    }

                    return Cache.Instance.Targets.ToList(); 
                }

                return _combatTargets;
            }
        }

        //
        // entities that have potentially not been locked yet
        // entities that may not be in range yet
        // entities that eventually we want to shoot
        //
        public IEnumerable<EntityCache> PotentialCombatTargets
        {
            get
            {
                if (_potentialCombatTargets == null)
                {
                    //List<EntityCache>
                    if (Cache.Instance.InSpace)
                    {
                        _potentialCombatTargets = EntitiesOnGrid.Where(e => e.CategoryId == (int)CategoryID.Entity
                                                            && !e.IsIgnored
                                                            && (!e.IsSentry || (e.IsSentry && Settings.Instance.KillSentries) || (e.IsSentry && e.IsEwarTarget))
                                                            && (e.IsNpcByGroupID || e.IsAttacking) //|| e.isPreferredPrimaryWeaponTarget || e.IsPrimaryWeaponKillPriority || e.IsDronePriorityTarget || e.isPreferredDroneTarget) //|| e.IsNpc)
                            //&& !e.IsTarget
                                                            && !e.IsContainer
                                                            && !e.IsFactionWarfareNPC
                                                            && !e.IsEntityIShouldLeaveAlone
                                                            && !e.IsBadIdea // || e.IsBadIdea && e.IsAttacking)
                                                            && (!e.IsPlayer || e.IsPlayer && e.IsAttacking)
                                                            && !e.IsMiscJunk
                                                            && (!e.IsLargeCollidable || e.IsPrimaryWeaponPriorityTarget)
                                                            )
                                                            .ToList();

                        if (Settings.Instance.DebugPotentialCombatTargets)
                        {
                            if (!_potentialCombatTargets.Any())
                            {
                                Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                                List<EntityCache> __entities = EntitiesOnGrid.Where(e => e.CategoryId == (int)CategoryID.Entity
                                                                && !e.IsBadIdea //|| e.IsBadIdea && e.IsAttacking)
                                                                && (!e.IsPlayer || e.IsPlayer && e.IsAttacking)
                                                                && !e.IsMiscJunk
                                                                && !e.IsAsteroid
                                                                && !e.IsIgnored
                                                                )
                                                                .ToList();

                                int _entitiescount = 0;

                                if (__entities.Any())
                                {
                                    _entitiescount = __entities.Count();
                                    Logging.Log("Cache.potentialCombatTargets", "DebugPotentialCombatTargets: list of __entities below", Logging.Debug);
                                    int i = 0;
                                    foreach (EntityCache t in __entities)
                                    {
                                        i++;
                                        Logging.Log("Cache.potentialCombatTargets", "[" + i + "] Name [" + t.Name + "] Distance [" + Math.Round(t.Distance / 1000, 2) + "] TypeID [" + t.TypeId + "] groupID [" + t.GroupId + "] IsNPC [" + t.IsNpc + "] IsNPCByGroupID [" + t.IsNpcByGroupID + "]", Logging.Debug);
                                        continue;
                                    }

                                    Logging.Log("Cache.potentialCombatTargets", "DebugPotentialCombatTargets: list of __entities above", Logging.Debug);
                                }

                                if (Settings.Instance.DebugPotentialCombatTargets) Logging.Log("Cache.potentialCombatTargets", "[1]: no targets found !!! _entities [" + _entitiescount + "]", Logging.Debug);
                            }
                        }

                        return _potentialCombatTargets;
                    }

                    return new List<EntityCache>();
                }

                return _potentialCombatTargets;
            }
        }

        /// <summary>
        ///   Entities cache (all entities within 256km) //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _entitiesOnGrid;

        public IEnumerable<EntityCache> EntitiesOnGrid
        {
            get
            {
                try
                {
                    if (_entitiesOnGrid == null)
                    {
                        return Cache.Instance.Entities.Where(e => e.IsOnGridWithMe);
                    }

                    return _entitiesOnGrid;
                }
                catch (NullReferenceException) { }  // this can happen during session changes

                return new List<EntityCache>();
            }
        }

        /// <summary>
        ///   Entities cache (all entities within 256km) //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _entities;

        public IEnumerable<EntityCache> Entities
        {
            get
            {
                try
                {
                    if (_entities == null)
                    {
                        return Cache.Instance.DirectEve.Entities.Where(e => e.IsValid && e.CategoryId != (int)CategoryID.Charge).Select(i => new EntityCache(i)).ToList();
                    }

                    return _entities;
                }
                catch (NullReferenceException) { }  // this can happen during session changes

                return new List<EntityCache>();
            }
        }

        public Dictionary<long, string> EntityNames = new Dictionary<long, string>();
        public Dictionary<long, int> EntityTypeID = new Dictionary<long, int>();
        public Dictionary<long, int> EntityGroupID = new Dictionary<long, int>();
        public Dictionary<long, long> EntityBounty = new Dictionary<long, long>();
        public Dictionary<long, bool> EntityIsFrigate = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsNPCFrigate = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsCruiser = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsNPCCruiser = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsBattleCruiser = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsNPCBattleCruiser = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsBattleShip = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsNPCBattleShip = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsHighValueTarget = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsLowValueTarget = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsLargeCollidable = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsMiscJunk = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsBadIdea = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsFactionWarfareNPC = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsNPCByGroupID = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsEntutyIShouldLeaveAlone = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsSentry = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityHaveLootRights = new Dictionary<long, bool>();
        public Dictionary<long, bool> EntityIsStargate = new Dictionary<long, bool>();


        public IEnumerable<EntityCache> EntitiesActivelyBeingLocked
        {
            get
            {
                if (!InSpace)
                {
                    return new List<EntityCache>();
                }

                IEnumerable<EntityCache> _entitiesActivelyBeingLocked = Cache.Instance.EntitiesOnGrid.Where(i => i.IsTargeting).ToList();
                if (_entitiesActivelyBeingLocked.Any())
                {
                    return _entitiesActivelyBeingLocked;
                }

                return new List<EntityCache>();
            }
        }

        /// <summary>
        ///   Entities cache (all entities within 256km) //cleared in InvalidateCache 
        /// </summary>
        private List<EntityCache> _entitiesNotSelf;

        public IEnumerable<EntityCache> EntitiesNotSelf
        {
            get
            {
                if (_entitiesNotSelf == null)
                {
                    _entitiesNotSelf = Cache.Instance.EntitiesOnGrid.Where(i => i.CategoryId != (int)CategoryID.Asteroid && i.Id != Cache.Instance.ActiveShip.ItemId).ToList();
                    if (_entitiesNotSelf.Any())
                    {
                        return _entitiesNotSelf;
                    }

                    return new List<EntityCache>();
                }

                return _entitiesNotSelf;
            }
        }

        private EntityCache _myShipEntity;
        public EntityCache MyShipEntity 
        {
            get
            {
                if (_myShipEntity == null)
                {
                    if (!Cache.Instance.InSpace)
                    {
                        return null;
                    }

                    _myShipEntity = Cache.Instance.EntitiesOnGrid.FirstOrDefault(e => e.Id == Cache.Instance.ActiveShip.ItemId);
                    return _myShipEntity;
                }

                return _myShipEntity;
            }
        }

        public bool InSpace
        {
            get
            {
                try
                {
                    if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddMilliseconds(800))
                    {
                        //if We already set the LastInStation timestamp this iteration we do not need to check if we are in station
                        return true;
                    }

                    if (DirectEve.Session.IsInSpace)
                    {
                        if (!Cache.Instance.InStation)
                        {
                            if (Cache.Instance.DirectEve.ActiveShip.Entity != null)
                            {
                                if (DirectEve.Session.IsReady)
                                {
                                    if (Cache.Instance.Entities.Any())
                                    {
                                        Cache.Instance.LastInSpace = DateTime.UtcNow;
                                        return true;    
                                    }
                                }
                                
                                if (Settings.Instance.DebugInSpace) Logging.Log("InSpace", "Session is Not Ready", Logging.Debug);
                                return false;
                            }
                            
                            if (Settings.Instance.DebugInSpace) Logging.Log("InSpace", "Cache.Instance.DirectEve.ActiveShip.Entity is null", Logging.Debug);
                            return false;
                        }

                        if (Settings.Instance.DebugInSpace) Logging.Log("InSpace", "NOT InStation is False", Logging.Debug);
                        return false;
                    }

                    if (Settings.Instance.DebugInSpace) Logging.Log("InSpace", "InSpace is False", Logging.Debug);
                    return false;
                }
                catch (Exception ex)
                {
                    if (Settings.Instance.DebugExceptions) Logging.Log("Cache.InSpace", "if (DirectEve.Session.IsInSpace && !DirectEve.Session.IsInStation && DirectEve.Session.IsReady && Cache.Instance.ActiveShip.Entity != null) <---must have failed exception was [" + ex.Message + "]", Logging.Teal);
                    return false;
                }
            }
        }

        public bool InStation
        {
            get
            {
                try
                {
                    if (DateTime.UtcNow < Cache.Instance.LastInStation.AddMilliseconds(800))
                    {
                        //if We already set the LastInStation timestamp this iteration we do not need to check if we are in station
                        return true;
                    }

                    if (DirectEve.Session.IsInStation && !DirectEve.Session.IsInSpace && DirectEve.Session.IsReady)
                    {
                        if (!Cache.Instance.Entities.Any())
                        {
                            Cache.Instance.LastInStation = DateTime.UtcNow;
                            return true;
                        }
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    if (Settings.Instance.DebugExceptions) Logging.Log("Cache.InStation", "if (DirectEve.Session.IsInStation && !DirectEve.Session.IsInSpace && DirectEve.Session.IsReady) <---must have failed exception was [" + ex.Message + "]", Logging.Teal);
                    return false;
                }
            }
        }

        public bool InWarp
        {
            get
            {
                try
                {
                    if (Cache.Instance.InSpace && !Cache.Instance.InStation)
                    {
                        if (Cache.Instance.ActiveShip != null)
                        {
                            if (Cache.Instance.ActiveShip.Entity != null)
                            {
                                if (Cache.Instance.ActiveShip.Entity.Mode == 3)
                                {
                                    return Cache.Instance.ActiveShip != null && (Cache.Instance.ActiveShip.Entity != null && Cache.Instance.ActiveShip.Entity.Mode == 3);
                                }
                                else
                                {
                                    if (Settings.Instance.DebugInWarp && !Cache.Instance.Paused) Logging.Log("Cache.InWarp", "We are not in warp.Cache.Instance.ActiveShip.Entity.Mode  is [" + Cache.Instance.ActiveShip.Entity.Mode + "]", Logging.Teal);
                                    return false;
                                }
                            }
                            else
                            {
                                if (Settings.Instance.DebugInWarp && !Cache.Instance.Paused) Logging.Log("Cache.InWarp", "Why are we checking for InWarp if Cache.Instance.ActiveShip.Entity is Null? (session change?)", Logging.Teal);
                                return false;
                            }
                        }
                        else
                        {
                            if (Settings.Instance.DebugInWarp && !Cache.Instance.Paused) Logging.Log("Cache.InWarp", "Why are we checking for InWarp if Cache.Instance.ActiveShip is Null? (session change?)", Logging.Teal);
                            return false;
                        }
                    }
                    else
                    {
                        if (Settings.Instance.DebugInWarp && !Cache.Instance.Paused) Logging.Log("Cache.InWarp", "Why are we checking for InWarp while docked or between session changes?", Logging.Teal);
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    Logging.Log("Cache.InWarp", "InWarp check failed, exception [" + exception + "]", Logging.Teal);
                }

                return false;
            }
        }

        public bool IsOrbiting(long EntityWeWantToBeOrbiting = 0)
        {
            if (Cache.Instance.Approaching != null)
            {
                bool _followIDIsOnGrid = false;

                if (EntityWeWantToBeOrbiting != 0)
                {
                    _followIDIsOnGrid = (EntityWeWantToBeOrbiting == Cache.Instance.ActiveShip.Entity.FollowId);
                }
                else
                {
                    _followIDIsOnGrid = Cache.Instance.EntitiesOnGrid.Any(i => i.Id == Cache.Instance.ActiveShip.Entity.FollowId);
                }

                if (Cache.Instance.ActiveShip.Entity != null && Cache.Instance.ActiveShip.Entity.Mode == 4 && _followIDIsOnGrid)
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        public bool IsApproaching(long EntityWeWantToBeApproaching = 0)
        {
            if (Cache.Instance.Approaching != null)
            {
                bool _followIDIsOnGrid = false;
                
                if (EntityWeWantToBeApproaching != 0)
                {
                    _followIDIsOnGrid = (EntityWeWantToBeApproaching == Cache.Instance.ActiveShip.Entity.FollowId);
                }
                else
                {
                    _followIDIsOnGrid = Cache.Instance.EntitiesOnGrid.Any(i => i.Id == Cache.Instance.ActiveShip.Entity.FollowId);
                }

                if (Cache.Instance.ActiveShip.Entity != null && Cache.Instance.ActiveShip.Entity.Mode == 1 && _followIDIsOnGrid)
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        public bool IsApproachingOrOrbiting(long EntityWeWantToBeApproachingOrOrbiting = 0)
        {   
            if (IsApproaching(EntityWeWantToBeApproachingOrOrbiting))
            {
                return true;
            }

            if (IsOrbiting(EntityWeWantToBeApproachingOrOrbiting))
            {
                return true;
            }

            return false;
        }

        public IEnumerable<EntityCache> ActiveDrones
        {
            get { return _activeDrones ?? (_activeDrones = Cache.Instance.DirectEve.ActiveDrones.Select(d => new EntityCache(d)).ToList()); }
        }

        public IEnumerable<EntityCache> Stations
        {
            get
            {
                if (_stations == null)
                {
                    if (Cache.Instance.Entities.Any())
                    {
                        _stations = Cache.Instance.Entities.Where(e => e.CategoryId == (int)CategoryID.Station).OrderBy(i => i.Distance).ToList();
                        if (_stations.Any())
                        {
                            return _stations;
                        }

                        return null;
                    }

                    return null;
                }

                return _stations;   
            }
        }

        public EntityCache ClosestStation
        {
            get
            {
                if (Stations != null &&  Stations.Any())
                {
                    return Stations.OrderBy(s => s.Distance).FirstOrDefault() ?? Cache.Instance.Entities.OrderByDescending(s => s.Distance).FirstOrDefault();    
                }

                return null;
            }
        }

        public EntityCache StationByName(string stationName)
        {
            EntityCache station = Stations.First(x => x.Name.ToLower() == stationName.ToLower());
            return station;
        }

        public IEnumerable<DirectSolarSystem> SolarSystems
        {
            get
            {
                var solarSystems = DirectEve.SolarSystems.Values.OrderBy(s => s.Name).ToList();
                return solarSystems;
            }
        }

        public IEnumerable<EntityCache> JumpBridges
        {
            get { return _jumpBridges ?? (_jumpBridges = Cache.Instance.Entities.Where(e => e.GroupId == (int)Group.JumpBridge).ToList()); }
        }

        public IEnumerable<EntityCache> Stargates
        {
            get
            {
                if (_stargates == null)
                {
                    if (Cache.Instance.EntityIsStargate.Any())
                    {
                        if (_stargates != null && _stargates.Any()) _stargates.Clear();
                        if (_stargates == null) _stargates = new List<EntityCache>();
                        foreach (KeyValuePair<long, bool> __stargate in Cache.Instance.EntityIsStargate)
                        {
                            _stargates.Add(Cache.Instance.Entities.FirstOrDefault(i => i.Id == __stargate.Key));
                        }

                        if (_stargates.Any()) return _stargates;
                    }

                    _stargates = Cache.Instance.Entities.Where(e => e.GroupId == (int)Group.Stargate).ToList();
                    foreach (EntityCache __stargate in _stargates)
                    {
                        if (Cache.Instance.EntityIsStargate.Any())
                        {
                            if (!Cache.Instance.EntityIsStargate.ContainsKey(__stargate.Id))
                            {
                                Cache.Instance.EntityIsStargate.Add(__stargate.Id, true);
                                continue;
                            }

                            continue;
                        }

                        Cache.Instance.EntityIsStargate.Add(__stargate.Id, true);
                        continue;
                    }
                    
                }

                return _stargates ?? null;
            }
        }

        public EntityCache ClosestStargate
        {
            get
            {
                if (Stargates.Any())
                {
                    return Stargates.OrderBy(s => s.Distance).FirstOrDefault() ?? null;
                }

                return null;
            }
        }

        public EntityCache StargateByName(string locationName)
        {
            {
                return _stargate ?? (_stargate = Cache.Instance.EntitiesByName(locationName, Cache.Instance.Entities.Where(i => i.GroupId == (int)Group.Stargate)).FirstOrDefault(e => e.GroupId == (int)Group.Stargate));
            }
        }

        public IEnumerable<EntityCache> BigObjects
        {
            get
            {
                return _bigObjects ?? (_bigObjects = Cache.Instance.EntitiesOnGrid.Where(e =>
                       e.Distance < (double)Distances.OnGridWithMe &&
                       (e.IsLargeCollidable || e.CategoryId == (int)CategoryID.Asteroid || e.GroupId == (int)Group.SpawnContainer)
                       ).OrderBy(t => t.Distance).ToList());
            }
        }

        public IEnumerable<EntityCache> AccelerationGates
        {
            get
            {
                return _gates ?? (_gates = Cache.Instance.EntitiesOnGrid.Where(e =>
                       e.Distance < (double)Distances.OnGridWithMe &&
                       e.GroupId == (int)Group.AccelerationGate &&
                       e.Distance < (double)Distances.OnGridWithMe).OrderBy(t => t.Distance).ToList());
            }
        }

        public IEnumerable<EntityCache> BigObjectsandGates
        {
            get
            {
                return _bigObjectsAndGates ?? (_bigObjectsAndGates = Cache.Instance.EntitiesOnGrid.Where(e => 
                       (e.IsLargeCollidable || e.CategoryId == (int)CategoryID.Asteroid || e.GroupId == (int)Group.AccelerationGate || e.GroupId == (int)Group.SpawnContainer)
                       && e.Distance < (double)Distances.DirectionalScannerCloseRange).OrderBy(t => t.Distance).ToList());
            }
        }

        public IEnumerable<EntityCache> Objects
        {
            get
            {
                return _objects ?? (_objects = Cache.Instance.EntitiesOnGrid.Where(e =>
                       !e.IsPlayer &&
                       e.GroupId != (int)Group.SpawnContainer &&
                       e.GroupId != (int)Group.Wreck &&
                       e.Distance < 200000).OrderBy(t => t.Distance).ToList());
            }
        }

        public EntityCache Star
        {
            get { return _star ?? (_star = Entities.FirstOrDefault(e => e.CategoryId == (int)CategoryID.Celestial && e.GroupId == (int)Group.Star)); }
        }



        private List<PriorityTarget> _primaryWeaponPriorityTargetsPerFrameCaching;
        
        private List<PriorityTarget> _primaryWeaponPriorityTargets;
        
        public List<PriorityTarget> PrimaryWeaponPriorityTargets
        {
            get
            {
                if (_primaryWeaponPriorityTargetsPerFrameCaching == null)
                {
                    //
                    // remove targets that no longer exist
                    //
                    if (_primaryWeaponPriorityTargets != null && _primaryWeaponPriorityTargets.Any())
                    {
                        foreach (PriorityTarget _primaryWeaponPriorityTarget in _primaryWeaponPriorityTargets)
                        {
                            if (Cache.Instance.EntitiesOnGrid.All(i => i.Id != _primaryWeaponPriorityTarget.EntityID))
                            {
                                Logging.Log("PrimaryWeaponPriorityTargets", "Remove Target that is no longer in the Entities list [" + _primaryWeaponPriorityTarget.Name + "]ID[" + Cache.Instance.MaskedID(_primaryWeaponPriorityTarget.EntityID) + "] PriorityLevel [" + _primaryWeaponPriorityTarget.PrimaryWeaponPriority + "]", Logging.Debug);
                                _primaryWeaponPriorityTargets.Remove(_primaryWeaponPriorityTarget);
                                break;
                            }
                        }

                        _primaryWeaponPriorityTargetsPerFrameCaching = _primaryWeaponPriorityTargets;
                        return _primaryWeaponPriorityTargets;
                    }

                    //
                    // initialize a fresh list - to be filled in during panic (updated every tick)
                    //
                    _primaryWeaponPriorityTargets = new List<PriorityTarget>();
                    _primaryWeaponPriorityTargetsPerFrameCaching = _primaryWeaponPriorityTargets;
                    return _primaryWeaponPriorityTargets;
                }

                return _primaryWeaponPriorityTargetsPerFrameCaching;
            }
            set
            {
                _primaryWeaponPriorityTargets = value;
            }
        }

        private IEnumerable<EntityCache> _primaryWeaponPriorityEntities;

        public IEnumerable<EntityCache> PrimaryWeaponPriorityEntities
        {
            get
            {
                //
                // every frame re-populate the PrimaryWeaponPriorityEntities from the list of IDs we have tucked away in PrimaryWeaponPriorityEntities
                // this occurs because in Invalidatecache() we are, necessarily,  clearing this every frame!
                //
                if (_primaryWeaponPriorityEntities == null)
                {
                    if (_primaryWeaponPriorityTargets != null && _primaryWeaponPriorityTargets.Any())
                    {
                        _primaryWeaponPriorityEntities = PrimaryWeaponPriorityTargets.OrderByDescending(pt => pt.PrimaryWeaponPriority).ThenBy(pt => pt.Entity.Distance).Select(pt => pt.Entity).ToList();
                        return _primaryWeaponPriorityEntities;
                    }

                    if (Settings.Instance.DebugAddPrimaryWeaponPriorityTarget) Logging.Log("PrimaryWeaponPriorityEntities", "if (_primaryWeaponPriorityTargets.Any()) none available yet", Logging.Debug);
                    _primaryWeaponPriorityEntities = new List<EntityCache>();
                    return _primaryWeaponPriorityEntities;
                }

                //
                // if we have already populated the list this frame return the list we already generated
                //
                return _primaryWeaponPriorityEntities;
            }
        }

        private List<PriorityTarget> _dronePriorityTargets;

        public List<PriorityTarget> DronePriorityTargets
        {
            get
            {
                //
                // remove targets that no longer exist
                //
                if (_dronePriorityTargets != null && _dronePriorityTargets.Any())
                {
                    foreach (PriorityTarget dronePriorityTarget in _dronePriorityTargets)
                    {
                        if (Cache.Instance.EntitiesOnGrid.All(i => i.Id != dronePriorityTarget.EntityID))
                        {
                            _dronePriorityTargets.Remove(dronePriorityTarget);
                            break;
                        }
                    }

                    return _dronePriorityTargets;
                }

                //
                // initialize a fresh list - to be filled in during panic (updated every tick)
                //
                _dronePriorityTargets = new List<PriorityTarget>();
                return _dronePriorityTargets;
            }
        }

        private IEnumerable<EntityCache> _dronePriorityEntities;

        public IEnumerable<EntityCache> DronePriorityEntities
        {
            get
            {
                //
                // every frame re-populate the DronePriorityEntities from the list of IDs we have tucked away in DronePriorityTargets
                // this occurs because in Invalidatecache() we are, necessarily,  clearing this every frame!
                //
                if (_dronePriorityEntities == null)
                {
                    if (DronePriorityTargets != null && DronePriorityTargets.Any())
                    {
                        _dronePriorityEntities = DronePriorityTargets.OrderByDescending(pt => pt.DronePriority).ThenBy(pt => pt.Entity.Distance).Select(pt => pt.Entity);
                        return _dronePriorityEntities;
                    }

                    _dronePriorityEntities = new List<EntityCache>();
                    return _dronePriorityEntities;
                }

                //
                // if we have already populated the list this frame return the list we already generated
                //
                return _dronePriorityEntities;
            }
        }

        public EntityCache Approaching
        {
            get
            {
                //if (_approaching == null)
                //{
                    DirectEntity ship = Cache.Instance.ActiveShip.Entity;
                    if (ship != null && ship.IsValid)
                    {
                        _approaching = EntityById(ship.FollowId);
                    }
                //}

                if (_approaching != null && _approaching.IsValid)
                {
                    return _approaching;
                }

                return null;
            }
            set { _approaching = value; }
        }

        public List<DirectWindow> Windows
        {
            get
            {
                try
                {
                    if (Cache.Instance.InSpace && DateTime.UtcNow > Cache.Instance.LastInStation.AddSeconds(20) || (Cache.Instance.InStation && DateTime.UtcNow > Cache.Instance.LastInSpace.AddSeconds(20)))
                    {
                        return _windows ?? (_windows = DirectEve.Windows);
                    }

                    return new List<DirectWindow>();
                }
                catch (Exception exception)
                {
                    Logging.Log("Cache.Windows", "Exception [" + exception + "]", Logging.Debug);
                }

                return null;
            }
        }

        /// <summary>
        ///   Returns the mission for a specific agent
        /// </summary>
        /// <param name="agentId"></param>
        /// <param name="ForceUpdate"> </param>
        /// <returns>null if no mission could be found</returns>
        public DirectAgentMission GetAgentMission(long agentId, bool ForceUpdate)
        {
            if (DateTime.UtcNow < NextGetAgentMissionAction)
            {
                if (FirstAgentMission != null)
                {
                    return FirstAgentMission;
                }

                return null;
            }

            try
            {
                if (ForceUpdate || myAgentMissionList == null || !myAgentMissionList.Any())
                {
                    myAgentMissionList = DirectEve.AgentMissions.Where(m => m.AgentId == agentId).ToList();
                    NextGetAgentMissionAction = DateTime.UtcNow.AddSeconds(5);
                }

                if (myAgentMissionList.Any())
                {
                    FirstAgentMission = myAgentMissionList.FirstOrDefault();
                    return FirstAgentMission;
                }

                return null;
            }
            catch (Exception exception)
            {
                Logging.Log("Cache.Instance.GetAgentMission", "DirectEve.AgentMissions failed: [" + exception + "]", Logging.Teal);
                return null;
            }
        }

        /// <summary>
        ///   Returns the mission objectives from
        /// </summary>
        public List<string> MissionItems { get; private set; }

        /// <summary>
        ///   Returns the item that needs to be brought on the mission
        /// </summary>
        /// <returns></returns>
        public string BringMissionItem { get; private set; }

        public int BringMissionItemQuantity { get; private set; }

        public string BringOptionalMissionItem { get; private set; }

        public int BringOptionalMissionItemQuantity { get; set; }

        /// <summary>
        ///   Range for warp to mission bookmark
        /// </summary>
        public double MissionWarpAtDistanceRange { get; set; } //in km

        public string Fitting { get; set; } // stores name of the final fitting we want to use

        public string MissionShip { get; set; } //stores name of mission specific ship

        public string DefaultFitting { get; set; } //stores name of the default fitting

        public string CurrentFit { get; set; }

        public string FactionFit { get; set; }

        public string FactionName { get; set; }

        public bool ArmLoadedCache { get; set; } // flags whether arm has already loaded the mission

        public bool UseMissionShip { get; set; } // flags whether we're using a mission specific ship

        public bool ChangeMissionShipFittings { get; set; } // used for situations in which missionShip's specified, but no faction or mission fittings are; prevents default

        public List<Ammo> MissionAmmo;

        public int MissionWeaponGroupId { get; set; }

        public bool? MissionUseDrones { get; set; }

        public bool? MissionKillSentries { get; set; }

        public bool StopTimeSpecified = true;

        public DateTime StopTime = DateTime.Now.AddHours(10);

        public DateTime ManualStopTime = DateTime.Now.AddHours(10);

        public DateTime ManualRestartTime = DateTime.Now.AddHours(10);

        public DateTime StartTime { get; set; }

        public int MaxRuntime { get; set; }

        public DateTime LastInStation = DateTime.MinValue;

        public DateTime LastInSpace = DateTime.MinValue;

        public DateTime LastInWarp = DateTime.UtcNow.AddMinutes(5);

        public DateTime WehaveMoved = DateTime.UtcNow;

        public bool CloseQuestorCMDLogoff; //false;

        public bool CloseQuestorCMDExitGame = true;

        public bool CloseQuestorEndProcess = false;

        public bool GotoBaseNow; //false;

        public string ReasonToStopQuestor { get; set; }

        public string SessionState { get; set; }

        public double SessionIskGenerated { get; set; }

        public double SessionLootGenerated { get; set; }

        public double SessionLPGenerated { get; set; }

        public int SessionRunningTime { get; set; }

        public double SessionIskPerHrGenerated { get; set; }

        public double SessionLootPerHrGenerated { get; set; }

        public double SessionLPPerHrGenerated { get; set; }

        public double SessionTotalPerHrGenerated { get; set; }

        public bool QuestorJustStarted = true;

        public bool InvalidateCacheQuestorJustStartedFlag = false;

        public DateTime EnteredCloseQuestor_DateTime;

        public bool DropMode { get; set; }

        public DirectWindow GetWindowByCaption(string caption)
        {
            return Windows.FirstOrDefault(w => w.Caption.Contains(caption));
        }

        public DirectWindow GetWindowByName(string name)
        {
            DirectWindow WindowToFind = null;
            try
            {
                if (!Cache.Instance.Windows.Any())
                {
                    return null;
                }

                // Special cases
                if (name == "Local")
                {
                    WindowToFind = Windows.FirstOrDefault(w => w.Name.StartsWith("chatchannel_solarsystemid"));
                }

                if (WindowToFind == null)
                {
                    WindowToFind = Windows.FirstOrDefault(w => w.Name == name);
                }

                if (WindowToFind != null)
                {
                    return WindowToFind;
                }
            }
            catch (Exception exception)
            {
                Logging.Log("Cache.GetWindowByName", "Exception [" + exception + "]", Logging.Debug);    
            }

            return null;
        }

        /// <summary>
        ///   Return entities by name
        /// </summary>
        /// <param name = "nameToSearchFor"></param>
        /// <returns></returns>
        public IEnumerable<EntityCache> EntitiesByName(string nameToSearchFor, IEnumerable<EntityCache> EntitiesToLookThrough)
        {
            return EntitiesToLookThrough.Where(e => e.Name.ToLower() == nameToSearchFor.ToLower()).ToList();
        }

        /// <summary>
        ///   Return entity by name
        /// </summary>
        /// <param name = "name"></param>
        /// <returns></returns>
        public EntityCache EntityByName(string name)
        {
            return Cache.Instance.Entities.FirstOrDefault(e => System.String.Compare(e.Name, name, System.StringComparison.OrdinalIgnoreCase) == 0);
        }

        public IEnumerable<EntityCache> EntitiesByPartialName(string nameToSearchFor)
        {
            IEnumerable<EntityCache> _entitiesByPartialName = Cache.Instance.Entities.Where(e => e.Name.Contains(nameToSearchFor)).ToList();
            if(_entitiesByPartialName != null && !_entitiesByPartialName.Any())
            {
                _entitiesByPartialName = Cache.Instance.Entities.Where(e => e.Name == nameToSearchFor).ToList();
            }
            if (_entitiesByPartialName != null && !_entitiesByPartialName.Any())
            {
                _entitiesByPartialName = null;
            }

            return _entitiesByPartialName;
        }

        /// <summary>
        ///   Return entities that contain the name
        /// </summary>
        /// <returns></returns>
        public IEnumerable<EntityCache> EntitiesThatContainTheName(string label)
        {
            return Cache.Instance.Entities.Where(e => !string.IsNullOrEmpty(e.Name) && e.Name.ToLower().Contains(label.ToLower())).ToList();
        }

        /// <summary>
        ///   Return a cached entity by Id
        /// </summary>
        /// <param name = "id"></param>
        /// <returns></returns>
        public EntityCache EntityById(long id)
        {
            if (_entitiesById.ContainsKey(id))
            {
                return _entitiesById[id];
            }

            EntityCache entity = Cache.Instance.EntitiesOnGrid.FirstOrDefault(e => e.Id == id);
            _entitiesById[id] = entity;
            return entity;
        }

        public List<DirectBookmark> _allBookmarks;

        public List<DirectBookmark> AllBookmarks
        {
            get
            {
                try
                {
                    if (_allBookmarks == null || !_allBookmarks.Any())
                    {
                        if (DirectEve.Bookmarks.Any())
                        {
                            _allBookmarks = DirectEve.Bookmarks;
                            return _allBookmarks;
                        }

                        return null; //there are no bookmarks to list...    
                    }

                    return _allBookmarks;
                }
                catch (Exception exception)
                {
                    Logging.Log("Cache.allBookmarks", "Exception [" + exception + "]", Logging.Debug);
                    return null;
                }
            }
            set
            {
                _allBookmarks = value;
            }
        }

        /// <summary>
        ///   Returns the first mission bookmark that starts with a certain string
        /// </summary>
        /// <returns></returns>
        public DirectAgentMissionBookmark GetMissionBookmark(long agentId, string startsWith)
        {
            // Get the missions
            DirectAgentMission missionForBookmarkInfo = GetAgentMission(agentId, false);
            if (missionForBookmarkInfo == null)
            {
                Logging.Log("Cache.DirectAgentMissionBookmark", "missionForBookmarkInfo [null] <---bad  parameters passed to us:  agentid [" + agentId + "] startswith [" + startsWith + "]", Logging.White);
                return null;
            }

            // Did we accept this mission?
            if (missionForBookmarkInfo.State != (int)MissionState.Accepted || missionForBookmarkInfo.AgentId != agentId)
            {
                //Logging.Log("missionForBookmarkInfo.State: [" + missionForBookmarkInfo.State.ToString(CultureInfo.InvariantCulture) + "]");
                //Logging.Log("missionForBookmarkInfo.AgentId: [" + missionForBookmarkInfo.AgentId.ToString(CultureInfo.InvariantCulture) + "]");
                //Logging.Log("agentId: [" + agentId.ToString(CultureInfo.InvariantCulture) + "]");
                return null;
            }

            return missionForBookmarkInfo.Bookmarks.FirstOrDefault(b => b.Title.ToLower().StartsWith(startsWith.ToLower()));
        }

        /// <summary>
        ///   Return a bookmark by id
        /// </summary>
        /// <param name = "bookmarkId"></param>
        /// <returns></returns>
        public DirectBookmark BookmarkById(long bookmarkId)
        {
            if (Cache.Instance.AllBookmarks != null && Cache.Instance.AllBookmarks.Any())
            {
                return Cache.Instance.AllBookmarks.FirstOrDefault(b => b.BookmarkId == bookmarkId);
            }

            return null;
        }

        /// <summary>
        ///   Returns bookmarks that start with the supplied label
        /// </summary>
        /// <param name = "label"></param>
        /// <returns></returns>
        public List<DirectBookmark> BookmarksByLabel(string label)
        {
            // Does not seems to refresh the Corporate Bookmark list so it's having troubles to find Corporate Bookmarks
            if (Cache.Instance.AllBookmarks != null && Cache.Instance.AllBookmarks.Any())
            {
                return Cache.Instance.AllBookmarks.Where(b => !string.IsNullOrEmpty(b.Title) && b.Title.ToLower().StartsWith(label.ToLower())).OrderBy(f => f.LocationId).ToList();
            }

            return null;
        }

        /// <summary>
        ///   Returns bookmarks that contain the supplied label anywhere in the title
        /// </summary>
        /// <param name = "label"></param>
        /// <returns></returns>
        public List<DirectBookmark> BookmarksThatContain(string label)
        {
            if (Cache.Instance.AllBookmarks != null && Cache.Instance.AllBookmarks.Any())
            {
                return Cache.Instance.AllBookmarks.Where(b => !string.IsNullOrEmpty(b.Title) && b.Title.ToLower().Contains(label.ToLower())).OrderBy(f => f.LocationId).ToList();
            }

            return null;
        }

        /// <summary>
        ///   Invalidate the cached items
        /// </summary>
        public void InvalidateCache()
        {
            try
            {
                //
                // this list of variables is cleared every pulse.
                //
                _activeDrones = null;
                _agent = null;
                _aggressed = null;
                _allBookmarks = null;
                _ammoHangar = null;
                _approaching = null;
                _activeDrones = null;
                _bestDroneTargets = null;
                _bestPrimaryWeaponTargets = null;
                _bigObjects = null;
                _bigObjectsAndGates = null;
                _combatTargets = null;
                _currentShipsCargo = null;
                _containerInSpace = null;
                _containers = null;
                _entities = null;
                _entitiesNotSelf = null;
                _entitiesOnGrid = null;
                _entitiesById.Clear();
                _fittingManagerWindow = null;
                _gates = null;
                _IDsinInventoryTree = null;
                _itemHangar = null;
                _jumpBridges = null;
                _lootContainer = null;
                _lootHangar = null;
                _lpStore = null;
                _maxLockedTargets = null;
                _maxDroneRange = null;
                _maxrange = null;
                _maxTargetRange = null;
                _modules = null;
                _modulesAsItemCache = null;
                _myShipEntity = null; 
                _objects = null;
                _potentialCombatTargets = null;
                _primaryWeaponPriorityTargetsPerFrameCaching = null;
                _safeSpotBookmarks = null;
                _star = null;
                _stations = null;
                _stargate = null;
                _stargates = null;
                _targets = null;
                _targeting = null;
                _targetedBy = null;
                _TotalTargetsandTargeting = null;
                _undockBookmark = null;
                _unlootedContainers = null;
                _unlootedWrecksAndSecureCans = null;
                _windows = null;

                _primaryWeaponPriorityEntities = null;
                _dronePriorityEntities = null;
                _preferredPrimaryWeaponTarget = null;
                    
                //if (QuestorJustStarted && InvalidateCacheQuestorJustStartedFlag)
                //{
                //    InvalidateCacheQuestorJustStartedFlag = false;
                //     if (Settings.Instance.DebugPreferredPrimaryWeaponTarget) Logging.Log("Cache.InvalidateCache", "QuestorJustStarted: initializing", Logging.Debug);
                    if (_primaryWeaponPriorityTargets != null && _primaryWeaponPriorityTargets.Any())
                    {
                        _primaryWeaponPriorityTargets.ForEach(pt => pt.ClearCache());
                    }
                    
                    if (_dronePriorityTargets != null && _dronePriorityTargets.Any())
                    {
                        _dronePriorityTargets.ForEach(pt => pt.ClearCache());
                    }
                //}
            }
            catch (Exception exception)
            {
                Logging.Log("Cache.InvalidateCache", "Exception [" + exception + "]", Logging.Debug);    
            }
        }

        public string FilterPath(string path)
        {
            if (path == null)
            {
                return string.Empty;
            }

            path = path.Replace("\"", "");
            path = path.Replace("?", "");
            path = path.Replace("\\", "");
            path = path.Replace("/", "");
            path = path.Replace("'", "");
            path = path.Replace("*", "");
            path = path.Replace(":", "");
            path = path.Replace(">", "");
            path = path.Replace("<", "");
            path = path.Replace(".", "");
            path = path.Replace(",", "");
            path = path.Replace("'", "");
            while (path.IndexOf("  ", System.StringComparison.Ordinal) >= 0)
                path = path.Replace("  ", " ");
            return path.Trim();
        }

        /// <summary>
        ///   Loads mission objectives from XML file
        /// </summary>
        /// <param name = "agentId"> </param>
        /// <param name = "pocketId"> </param>
        /// <param name = "missionMode"> </param>
        /// <returns></returns>
        public IEnumerable<Actions.Action> LoadMissionActions(long agentId, int pocketId, bool missionMode)
        {
            DirectAgentMission missiondetails = GetAgentMission(agentId, false);
            if (missiondetails == null && missionMode)
            {
                return new Actions.Action[0];
            }

            if (missiondetails != null)
            {
                Cache.Instance.SetmissionXmlPath(FilterPath(missiondetails.Name));
                if (!File.Exists(Cache.Instance.MissionXmlPath))
                {
                    //No mission file but we need to set some cache settings
                    OrbitDistance = Settings.Instance.OrbitDistance;
                    OptimalRange = Settings.Instance.OptimalRange;
                    AfterMissionSalvaging = Settings.Instance.AfterMissionSalvaging;
                    return new Actions.Action[0];
                }

                //
                // this loads the settings from each pocket... but NOT any settings global to the mission
                //
                try
                {
                    XDocument xdoc = XDocument.Load(Cache.Instance.MissionXmlPath);
                    if (xdoc.Root != null)
                    {
                        XElement xElement = xdoc.Root.Element("pockets");
                        if (xElement != null)
                        {
                            IEnumerable<XElement> pockets = xElement.Elements("pocket");
                            foreach (XElement pocket in pockets)
                            {
                                if ((int)pocket.Attribute("id") != pocketId)
                                {
                                    continue;
                                }

                                if (pocket.Element("orbitentitynamed") != null)
                                {
                                    OrbitEntityNamed = (string)pocket.Element("orbitentitynamed");
                                }

                                if (pocket.Element("damagetype") != null)
                                {
                                    DamageType = (DamageType)Enum.Parse(typeof(DamageType), (string)pocket.Element("damagetype"), true);
                                }

                                if (pocket.Element("orbitdistance") != null) 	//Load OrbitDistance from mission.xml, if present
                                {
                                    OrbitDistance = (int)pocket.Element("orbitdistance");
                                    Logging.Log("Cache", "Using Mission Orbit distance [" + OrbitDistance + "]", Logging.White);
                                }
                                else //Otherwise, use value defined in charname.xml file
                                {
                                    OrbitDistance = Settings.Instance.OrbitDistance;
                                    Logging.Log("Cache", "Using Settings Orbit distance [" + OrbitDistance + "]", Logging.White);
                                }

                                if (pocket.Element("optimalrange") != null) 	//Load OrbitDistance from mission.xml, if present
                                {
                                    OptimalRange = (int)pocket.Element("optimalrange");
                                    Logging.Log("Cache", "Using Mission OptimalRange [" + OptimalRange + "]", Logging.White);
                                }
                                else //Otherwise, use value defined in charname.xml file
                                {
                                    OptimalRange = Settings.Instance.OptimalRange;
                                    Logging.Log("Cache", "Using Settings OptimalRange [" + OptimalRange + "]", Logging.White);
                                }

                                if (pocket.Element("afterMissionSalvaging") != null) 	//Load afterMissionSalvaging setting from mission.xml, if present
                                {
                                    AfterMissionSalvaging = (bool)pocket.Element("afterMissionSalvaging");
                                }

                                if (pocket.Element("dronesKillHighValueTargets") != null) 	//Load afterMissionSalvaging setting from mission.xml, if present
                                {
                                    DronesKillHighValueTargets = (bool)pocket.Element("dronesKillHighValueTargets");
                                }
                                else //Otherwise, use value defined in charname.xml file
                                {
                                    DronesKillHighValueTargets = Settings.Instance.DronesKillHighValueTargets;

                                    //Logging.Log(string.Format("Cache: Using Character Setting DroneKillHighValueTargets  {0}", DronesKillHighValueTargets));
                                }

                                List<Actions.Action> actions = new List<Actions.Action>();
                                XElement elements = pocket.Element("actions");
                                if (elements != null)
                                {
                                    foreach (XElement element in elements.Elements("action"))
                                    {
                                        Actions.Action action = new Actions.Action
                                            {
                                                State = (ActionState)Enum.Parse(typeof(ActionState), (string)element.Attribute("name"), true)
                                            };
                                        XAttribute xAttribute = element.Attribute("name");
                                        if (xAttribute != null && xAttribute.Value == "ClearPocket")
                                        {
                                            action.AddParameter("", "");
                                        }
                                        else
                                        {
                                            foreach (XElement parameter in element.Elements("parameter"))
                                            {
                                                action.AddParameter((string)parameter.Attribute("name"), (string)parameter.Attribute("value"));
                                            }
                                        }
                                        actions.Add(action);
                                    }
                                }

                                return actions;
                            }

                            //actions.Add(action);
                        }
                        else
                        {
                            return new Actions.Action[0];
                        }
                    }
                    else
                    {
                        { return new Actions.Action[0]; }
                    }

                    // if we reach this code there is no mission XML file, so we set some things -- Assail

                    OptimalRange = Settings.Instance.OptimalRange;
                    OrbitDistance = Settings.Instance.OrbitDistance;
                    Logging.Log("Cache", "Using Settings Orbit distance [" + Settings.Instance.OrbitDistance + "]", Logging.White);

                    return new Actions.Action[0];
                }
                catch (Exception ex)
                {
                    Logging.Log("Cache", "Error loading mission XML file [" + ex.Message + "]", Logging.Orange);
                    return new Actions.Action[0];
                }
            }
            return new Actions.Action[0];
        }

        public void SetmissionXmlPath(string missionName)
        {
            if (!string.IsNullOrEmpty(Cache.Instance.FactionName))
            {
                Cache.Instance.MissionXmlPath = System.IO.Path.Combine(Settings.Instance.MissionsPath, FilterPath(missionName) + "-" + Cache.Instance.FactionName + ".xml");
                if (!File.Exists(Cache.Instance.MissionXmlPath))
                {   
                    //
                    // This will always fail for courier missions, can we detect those and suppress these log messages?
                    //
                    Logging.Log("Cache.SetmissionXmlPath","[" + Cache.Instance.MissionXmlPath +"] not found.", Logging.White);
                    Cache.Instance.MissionXmlPath = System.IO.Path.Combine(Settings.Instance.MissionsPath, FilterPath(missionName) + ".xml");
                    if (!File.Exists(Cache.Instance.MissionXmlPath))
                    {
                        Logging.Log("Cache.SetmissionXmlPath", "[" + Cache.Instance.MissionXmlPath + "] not found", Logging.White);
                    }

                    if (File.Exists(Cache.Instance.MissionXmlPath))
                    {
                        Logging.Log("Cache.SetmissionXmlPath", "[" + Cache.Instance.MissionXmlPath + "] found!", Logging.Green);
                    }
                }
            }
            else
            {
                Cache.Instance.MissionXmlPath = System.IO.Path.Combine(Settings.Instance.MissionsPath, FilterPath(missionName) + ".xml");
            }
        }

        /// <summary>
        ///   Refresh the mission items
        /// </summary>
        public void RefreshMissionItems(long agentId)
        {
            // Clear out old items
            MissionItems.Clear();
            BringMissionItem = string.Empty;
            BringOptionalMissionItem = string.Empty;

            if (_States.CurrentQuestorState != QuestorState.CombatMissionsBehavior)
            {
                Settings.Instance.UseFittingManager = false;

                //Logging.Log("Cache.RefreshMissionItems", "We are not running missions so we have no mission items to refresh", Logging.Teal);
                return;
            }

            DirectAgentMission missionDetailsForMissionItems = GetAgentMission(agentId, false);
            if (missionDetailsForMissionItems == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(FactionName))
            {
                FactionName = "Default";
            }

            if (Settings.Instance.UseFittingManager)
            {
                //Set fitting to default
                DefaultFitting = Settings.Instance.DefaultFitting.Fitting;
                Fitting = DefaultFitting;
                MissionShip = "";
                ChangeMissionShipFittings = false;
                if (Settings.Instance.MissionFitting.Any(m => m.Mission.ToLower() == missionDetailsForMissionItems.Name.ToLower())) //priority goes to mission-specific fittings
                {
                    MissionFitting missionFitting;

                    // if we have got multiple copies of the same mission, find the one with the matching faction
                    if (Settings.Instance.MissionFitting.Any(m => m.Faction.ToLower() == FactionName.ToLower() && (m.Mission.ToLower() == missionDetailsForMissionItems.Name.ToLower())))
                    {
                        missionFitting = Settings.Instance.MissionFitting.FirstOrDefault(m => m.Faction.ToLower() == FactionName.ToLower() && (m.Mission.ToLower() == missionDetailsForMissionItems.Name.ToLower()));
                    }
                    else //otherwise just use the first copy of that mission
                    {
                        missionFitting = Settings.Instance.MissionFitting.FirstOrDefault(m => m.Mission.ToLower() == missionDetailsForMissionItems.Name.ToLower());
                    }

                    if (missionFitting != null)
                    {
                        string missionFit = missionFitting.Fitting;
                        string missionShip = missionFitting.Ship;
                        if (!(missionFit == "" && missionShip != "")) // if we have both specified a mission specific ship and a fitting, then apply that fitting to the ship
                        {
                            ChangeMissionShipFittings = true;
                            Fitting = missionFit;
                        }
                        else if (!string.IsNullOrEmpty(FactionFit))
                        {
                            Fitting = FactionFit;
                        }

                        Logging.Log("Cache", "Mission: " + missionFitting.Mission + " - Faction: " + FactionName + " - Fitting: " + missionFit + " - Ship: " + missionShip + " - ChangeMissionShipFittings: " + ChangeMissionShipFittings, Logging.White);
                        MissionShip = missionShip;
                    }
                }
                else if (!string.IsNullOrEmpty(FactionFit)) // if no mission fittings defined, try to match by faction
                {
                    Fitting = FactionFit;
                }

                if (Fitting == "") // otherwise use the default
                {
                    Fitting = DefaultFitting;
                }
            }

            string missionName = FilterPath(missionDetailsForMissionItems.Name);
            Cache.Instance.MissionXmlPath = System.IO.Path.Combine(Settings.Instance.MissionsPath, missionName + ".xml");
            if (!File.Exists(Cache.Instance.MissionXmlPath))
            {
                return;
            }

            try
            {
                XDocument xdoc = XDocument.Load(Cache.Instance.MissionXmlPath);
                IEnumerable<string> items = ((IEnumerable)xdoc.XPathEvaluate("//action[(translate(@name, 'LOT', 'lot')='loot') or (translate(@name, 'LOTIEM', 'lotiem')='lootitem')]/parameter[translate(@name, 'TIEM', 'tiem')='item']/@value")).Cast<XAttribute>().Select(a => ((string)a ?? string.Empty).ToLower());
                MissionItems.AddRange(items);

                if (xdoc.Root != null)
                {
                    BringMissionItem = (string)xdoc.Root.Element("bring") ?? string.Empty;
                    BringMissionItem = BringMissionItem.ToLower();
                    if (Settings.Instance.DebugArm) Logging.Log("Cachwe.RefreshMissionItems", "bring XML [" + xdoc.Root.Element("bring") + "] BringMissionItem [" + BringMissionItem + "]", Logging.Debug);
                    BringMissionItemQuantity = (int?)xdoc.Root.Element("bringquantity") ?? 1;
                    if (Settings.Instance.DebugArm) Logging.Log("Cachwe.RefreshMissionItems", "bringquantity XML [" + xdoc.Root.Element("bringquantity") + "] BringMissionItemQuantity [" + BringMissionItemQuantity + "]", Logging.Debug);
                    
                    BringOptionalMissionItem = (string)xdoc.Root.Element("trytobring") ?? string.Empty;
                    BringOptionalMissionItem = BringOptionalMissionItem.ToLower();
                    if (Settings.Instance.DebugArm) Logging.Log("Cachwe.RefreshMissionItems", "trytobring XML [" + xdoc.Root.Element("trytobring") + "] BringOptionalMissionItem [" + BringOptionalMissionItem + "]", Logging.Debug);
                    BringOptionalMissionItemQuantity = (int?)xdoc.Root.Element("trytobringquantity") ?? 1;
                    if (Settings.Instance.DebugArm) Logging.Log("Cachwe.RefreshMissionItems", "trytobringquantity XML [" + xdoc.Root.Element("trytobringquantity") + "] BringOptionalMissionItemQuantity [" + BringOptionalMissionItemQuantity + "]", Logging.Debug); 
                    
                }

                //load fitting setting from the mission file
                //Fitting = (string)xdoc.Root.Element("fitting") ?? "default";
            }
            catch (Exception ex)
            {
                Logging.Log("Cache", "Error loading mission XML file [" + ex.Message + "]", Logging.Orange);
            }
        }

        /// <summary>
        ///   Remove targets from priority list
        /// </summary>
        /// <param name = "targets"></param>
        public bool RemovePrimaryWeaponPriorityTargets(IEnumerable<EntityCache> targets)
        {
            try
            {
                targets = targets.ToList();

                if (targets.Any() && _primaryWeaponPriorityTargets != null && _primaryWeaponPriorityTargets.Any(pt => targets.Any(t => t.Id == pt.EntityID)))
                {
                    _primaryWeaponPriorityTargets.RemoveAll(pt => targets.Any(t => t.Id == pt.EntityID));
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logging.Log("RemovePrimaryWeaponPriorityTargets", "Exception [" + ex + "]", Logging.Debug);  
            }
            return false;
        }

        /// <summary>
        ///   Remove targets from priority list
        /// </summary>
        /// <param name = "targets"></param>
        public bool RemoveDronePriorityTargets(IEnumerable<EntityCache> targets)
        {
            try
            {
                targets = targets.ToList();

                if (targets.Any() && _dronePriorityTargets != null && _dronePriorityTargets.Any(pt => targets.Any(t => t.Id == pt.EntityID)))
                {
                    _dronePriorityTargets.RemoveAll(pt => targets.Any(t => t.Id == pt.EntityID));
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logging.Log("RemoveDronePriorityTargets", "Exception [" + ex + "]", Logging.Debug);
            }
            return false;
        }

        public void AddPrimaryWeaponPriorityTarget(EntityCache ewarEntity, PrimaryWeaponPriority priority, string module, bool AddEwarTypeToPriorityTargetList = true)
        {
            try
            {
                if ((ewarEntity.IsIgnored) || PrimaryWeaponPriorityTargets.Any(p => p.EntityID == ewarEntity.Id))
                {
                    if (Settings.Instance.DebugAddPrimaryWeaponPriorityTarget) Logging.Log("AddPrimaryWeaponPriorityTargets", "if ((target.IsIgnored) || PrimaryWeaponPriorityTargets.Any(p => p.Id == target.Id)) continue", Logging.Debug);
                    return;
                }

                if (AddEwarTypeToPriorityTargetList)
                {
                    //
                    // Primary Weapons
                    //
                    if (Cache.Instance.DoWeCurrentlyHaveTurretsMounted() && (ewarEntity.IsNPCFrigate || ewarEntity.IsFrigate)) //we use turrets, and this PrimaryWeaponPriorityTarget is a frigate
                    {
                        if (ewarEntity.Velocity < Settings.Instance.SpeedNPCFrigatesShouldBeIgnoredByPrimaryWeapons        //slow enough to hit
                            || ewarEntity.Distance > Settings.Instance.DistanceNPCFrigatesShouldBeIgnoredByPrimaryWeapons) //far enough away to hit
                        {
                            if (PrimaryWeaponPriorityTargets.All(i => i.EntityID != ewarEntity.Id))
                            {
                                Logging.Log(module, "Adding [" + ewarEntity.Name + "] Speed [" + Math.Round(ewarEntity.Velocity, 2) + "m/s] Distance [" + Math.Round(ewarEntity.Distance / 1000, 2) + "k] [ID: " + Cache.Instance.MaskedID(ewarEntity.Id) + "] as a PrimaryWeaponPriorityTarget [" + priority.ToString() + "]", Logging.White);
                                Cache.Instance._primaryWeaponPriorityTargets.Add(new PriorityTarget { Name = ewarEntity.Name, EntityID = ewarEntity.Id, PrimaryWeaponPriority = priority });
                                if (Settings.Instance.DebugKillAction)
                                {
                                    Logging.Log("Statistics", "Entering StatisticsState.ListPrimaryWeaponPriorityTargets", Logging.Debug);
                                    _States.CurrentStatisticsState = StatisticsState.ListPrimaryWeaponPriorityTargets;
                                }
                            }
                        }

                        return;
                    }

                    if (PrimaryWeaponPriorityTargets.All(i => i.EntityID != ewarEntity.Id))
                    {
                        Logging.Log(module, "Adding [" + ewarEntity.Name + "] Speed [" + Math.Round(ewarEntity.Velocity, 2) + "m/s] Distance [" + Math.Round(ewarEntity.Distance / 1000, 2) + "] [ID: " + Cache.Instance.MaskedID(ewarEntity.Id) + "] as a PrimaryWeaponPriorityTarget [" + priority.ToString() + "]", Logging.White);
                        Cache.Instance._primaryWeaponPriorityTargets.Add(new PriorityTarget { Name = ewarEntity.Name, EntityID = ewarEntity.Id, PrimaryWeaponPriority = priority });
                        if (Settings.Instance.DebugKillAction)
                        {
                            Logging.Log("Statistics", "Entering StatisticsState.ListPrimaryWeaponPriorityTargets", Logging.Debug);
                            _States.CurrentStatisticsState = StatisticsState.ListPrimaryWeaponPriorityTargets;
                        }
                    }

                    return;
                }

                return;
            }
            catch (Exception ex)
            {
                Logging.Log("AddPrimaryWeaponPriorityTarget", "Exception [" + ex + "]", Logging.Debug);
            }

            return;
        }

        public void AddPrimaryWeaponPriorityTargets(IEnumerable<EntityCache> ewarEntities, PrimaryWeaponPriority priority, string module, bool AddEwarTypeToPriorityTargetList = true)
        {
            try
            {
                ewarEntities = ewarEntities.ToList();
                if (ewarEntities.Any())
                {
                    foreach (EntityCache ewarEntity in ewarEntities)
                    {
                        AddPrimaryWeaponPriorityTarget(ewarEntity, priority, module, AddEwarTypeToPriorityTargetList);
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                Logging.Log("AddPrimaryWeaponPriorityTargets", "Exception [" + ex + "]", Logging.Debug);
            }

            return;
        }

        public void AddPrimaryWeaponPriorityTargetsByName(string stringEntitiesToAdd)
        {
            try
            {
                if (Cache.Instance.EntitiesOnGrid.Any(i => i.Name == stringEntitiesToAdd))
                {
                    IEnumerable<EntityCache> entitiesToAdd = Cache.Instance.EntitiesOnGrid.Where(i => i.Name == stringEntitiesToAdd).ToList();
                    if (entitiesToAdd.Any())
                    {

                        foreach (EntityCache entityToAdd in entitiesToAdd)
                        {
                            AddPrimaryWeaponPriorityTarget(entityToAdd, PrimaryWeaponPriority.PriorityKillTarget, "AddPWPTByName");
                            continue;
                        }

                        return;
                    }

                    Logging.Log("Adding PWPT", "[" + stringEntitiesToAdd + "] was not found.", Logging.Debug);
                    return;
                }

                int EntitiesOnGridCount = 0;
                if (Cache.Instance.EntitiesOnGrid.Any())
                {
                    EntitiesOnGridCount = Cache.Instance.EntitiesOnGrid.Count();
                }
                
                int EntitiesCount = 0;
                if (Cache.Instance.EntitiesOnGrid.Any())
                {
                    EntitiesCount = Cache.Instance.EntitiesOnGrid.Count();
                }

                Logging.Log("Adding PWPT", "[" + stringEntitiesToAdd + "] was not found. [" + EntitiesOnGridCount + "] entities on grid [" + EntitiesCount + "] entities", Logging.Debug);
                return;
            }
            catch (Exception ex)
            {
                Logging.Log("AddPrimaryWeaponPriorityTargets", "Exception [" + ex + "]", Logging.Debug);
            }

            return;
        }

        public void RemovePrimaryWeaponPriorityTargetsByName(string stringEntitiesToRemove)
        {
            IEnumerable<EntityCache> entitiesToRemove = Cache.Instance.EntitiesByName(stringEntitiesToRemove, Cache.Instance.EntitiesOnGrid).ToList();
            if (entitiesToRemove.Any())
            {
                Logging.Log("RemovingPWPT", "removing [" + stringEntitiesToRemove + "] from the PWPT List", Logging.Debug);
                RemovePrimaryWeaponPriorityTargets(entitiesToRemove);
                return;
            }

            Logging.Log("RemovingPWPT", "[" + stringEntitiesToRemove + "] was not found on grid", Logging.Debug);
            return;
        }

        public void AddDronePriorityTargetsByName(string stringEntitiesToAdd)
        {
            IEnumerable<EntityCache> entitiesToAdd = Cache.Instance.EntitiesByPartialName(stringEntitiesToAdd).ToList();
            if (entitiesToAdd.Any())
            {
                foreach (EntityCache entityToAdd in entitiesToAdd)
                {
                    Logging.Log("RemovingPWPT", "adding [" + entityToAdd.Name + "][" + Math.Round(entityToAdd.Distance / 1000, 0) + "k][" + Cache.Instance.MaskedID(entityToAdd.Id) + "] to the PWPT List", Logging.Debug);
                    AddDronePriorityTarget(entityToAdd, DronePriority.PriorityKillTarget, "AddDPTByName");
                    continue;
                }

                return;
            }

            Logging.Log("Adding DPT", "[" + stringEntitiesToAdd + "] was not found on grid", Logging.Debug);
            return;
        }

        public void RemovedDronePriorityTargetsByName(string stringEntitiesToRemove)
        {
            List<EntityCache> entitiesToRemove = Cache.Instance.EntitiesByName(stringEntitiesToRemove, Cache.Instance.EntitiesOnGrid).ToList();
            if (entitiesToRemove.Any())
            {
                Logging.Log("RemovingDPT", "removing [" + stringEntitiesToRemove + "] from the DPT List", Logging.Debug);
                RemoveDronePriorityTargets(entitiesToRemove);
                return;
            }

            Logging.Log("RemovingDPT", "[" + stringEntitiesToRemove + "] was not found on grid", Logging.Debug);
            return;
        }

        public void AddDronePriorityTargets(IEnumerable<EntityCache> ewarEntities, DronePriority priority, string module, bool AddEwarTypeToPriorityTargetList = true)
        {
            ewarEntities = ewarEntities.ToList();
            if (ewarEntities.Any())
            {
                foreach (EntityCache ewarEntity in ewarEntities)
                {
                    AddDronePriorityTarget(ewarEntity, priority, module, AddEwarTypeToPriorityTargetList);
                    continue;
                }

                return;
            }

            return;
        }

        public void AddDronePriorityTarget(EntityCache ewarEntity, DronePriority priority, string module, bool AddEwarTypeToPriorityTargetList = true)
        {
            if (AddEwarTypeToPriorityTargetList && Cache.Instance.UseDrones)
            {
                if ((ewarEntity.IsIgnored) || DronePriorityTargets.Any(p => p.EntityID == ewarEntity.Id))
                {
                    if (Settings.Instance.DebugAddDronePriorityTarget) Logging.Log("AddDronePriorityTargets", "if ((target.IsIgnored) || DronePriorityTargets.Any(p => p.Id == target.Id))", Logging.Debug);
                    return;
                }

                if (DronePriorityTargets.All(i => i.EntityID != ewarEntity.Id))
                {
                    int DronePriorityTargetCount = 0;
                    if (Cache.Instance.DronePriorityTargets.Any())
                    {
                        DronePriorityTargetCount = Cache.Instance.DronePriorityTargets.Count();
                    }
                    Logging.Log(module, "Adding [" + ewarEntity.Name + "] Speed [" + Math.Round(ewarEntity.Velocity, 2) + " m/s] Distance [" + Math.Round(ewarEntity.Distance / 1000, 2) + "] [ID: " + Cache.Instance.MaskedID(ewarEntity.Id) + "] as a drone priority target [" + priority.ToString() + "] we have [" + DronePriorityTargetCount + "] other DronePriorityTargets", Logging.Teal);
                    _dronePriorityTargets.Add(new PriorityTarget { Name = ewarEntity.Name, EntityID = ewarEntity.Id, DronePriority = priority });
                }

                return;
            }

            if (Settings.Instance.DebugAddDronePriorityTarget) Logging.Log(module, "UseDrones is [" + Cache.Instance.UseDrones.ToString() + "] AddWarpScramblersToDronePriorityTargetList is [" + Settings.Instance.AddWarpScramblersToDronePriorityTargetList + "] [" + ewarEntity.Name + "] was not added as a Drone PriorityTarget (why did we even try?)", Logging.Teal);
            return;
        }

        public void AddWarpScramblerByName(string stringEntitiesToAdd, int numberToIgnore = 0, bool notTheClosest = false)
        {
            IEnumerable<EntityCache> entitiesToAdd = Cache.Instance.EntitiesByName(stringEntitiesToAdd, Cache.Instance.EntitiesOnGrid).OrderBy(i => i.Distance).ToList();
            if (notTheClosest)
            {
                entitiesToAdd = entitiesToAdd.OrderByDescending(i => i.Distance);
            } 

            if (entitiesToAdd.Any())
            {
                foreach (EntityCache entityToAdd in entitiesToAdd)
                {
                    if (numberToIgnore > 0)
                    {
                        numberToIgnore--;
                        continue;
                    }

                    Logging.Log("AddWarpScramblerByName", "adding [" + entityToAdd.Name + "][" + Math.Round(entityToAdd.Distance / 1000, 0) + "k][" + Cache.Instance.MaskedID(entityToAdd.Id) + "] to the WarpScrambler List", Logging.Debug);
                    if (!Cache.Instance.ListOfWarpScramblingEntities.Contains(entityToAdd.Id))
                    {
                        Cache.Instance.ListOfWarpScramblingEntities.Add(entityToAdd.Id);
                    }
                    continue;
                }

                return;
            }

            Logging.Log("AddWarpScramblerByName", "[" + stringEntitiesToAdd + "] was not found on grid", Logging.Debug);
            return;
        }

        public void AddWebifierByName(string stringEntitiesToAdd, int numberToIgnore = 0, bool notTheClosest = false)
        {
            IEnumerable<EntityCache> entitiesToAdd = Cache.Instance.EntitiesByName(stringEntitiesToAdd, Cache.Instance.EntitiesOnGrid).OrderBy(i => i.Distance).ToList();
            if (notTheClosest)
            {
                entitiesToAdd = entitiesToAdd.OrderByDescending(i => i.Distance);    
            }

            if (entitiesToAdd.Any())
            {
                foreach (EntityCache entityToAdd in entitiesToAdd)
                {
                    if (numberToIgnore > 0)
                    {
                        numberToIgnore--;
                        continue;
                    }
                    Logging.Log("AddWebifierByName", "adding [" + entityToAdd.Name + "][" + Math.Round(entityToAdd.Distance / 1000, 0) + "k][" + Cache.Instance.MaskedID(entityToAdd.Id) + "] to the Webifier List", Logging.Debug);
                    if (!Cache.Instance.ListofWebbingEntities.Contains(entityToAdd.Id))
                    {
                        Cache.Instance.ListofWebbingEntities.Add(entityToAdd.Id);
                    }
                    continue;
                }

                return;
            }

            Logging.Log("AddWebifierByName", "[" + stringEntitiesToAdd + "] was not found on grid", Logging.Debug);
            return;
        }

        /// <summary>
        ///   Calculate distance from me
        /// </summary>
        /// <param name = "x"></param>
        /// <param name = "y"></param>
        /// <param name = "z"></param>
        /// <returns></returns>
        public double DistanceFromMe(double x, double y, double z)
        {
            if (Cache.Instance.ActiveShip.Entity == null)
            {
                return double.MaxValue;
            }

            double curX = Cache.Instance.ActiveShip.Entity.X;
            double curY = Cache.Instance.ActiveShip.Entity.Y;
            double curZ = Cache.Instance.ActiveShip.Entity.Z;

            return Math.Round(Math.Sqrt((curX - x) * (curX - x) + (curY - y) * (curY - y) + (curZ - z) * (curZ - z)),2);
        }

        /// <summary>
        ///   Calculate distance from entity
        /// </summary>
        /// <param name = "x"></param>
        /// <param name = "y"></param>
        /// <param name = "z"></param>
        /// <param name="entity"> </param>
        /// <returns></returns>
        public double DistanceFromEntity(double x, double y, double z, DirectEntity entity)
        {
            if (entity == null)
            {
                return double.MaxValue;
            }

            double curX = entity.X;
            double curY = entity.Y;
            double curZ = entity.Z;

            return Math.Sqrt((curX - x) * (curX - x) + (curY - y) * (curY - y) + (curZ - z) * (curZ - z));
        }

        /// <summary>
        ///   Calculate distance between 2 entities
        /// </summary>
        public double DistanceBetween2Entities(DirectEntity entity1, DirectEntity entity2)
        {
            if (entity1 == null || entity2 == null)
            {
                return double.MaxValue;
            }

            double entity1X = entity1.X;
            double entity1Y = entity1.Y;
            double entity1Z = entity1.Z;

            double entity2X = entity2.X;
            double entity2Y = entity2.Y;
            double entity2Z = entity2.Z;

            return Math.Sqrt((entity1X - entity2X) * (entity1X - entity2X) + (entity1Y - entity2Y) * (entity1Y - entity2Y) + (entity1Z - entity2Z) * (entity1Z - entity2Z));
        }

        /// <summary>
        ///   Create a bookmark
        /// </summary>
        /// <param name = "label"></param>
        public void CreateBookmark(string label)
        {
            if (Cache.Instance.AfterMissionSalvageBookmarks.Count() < 100)
            {
                if (Settings.Instance.CreateSalvageBookmarksIn.ToLower() == "corp".ToLower())
                {
                    DirectBookmarkFolder folder = Cache.Instance.DirectEve.BookmarkFolders.FirstOrDefault(i => i.Name == Settings.Instance.BookmarkFolder);
                    if (folder != null)
                    {
                        Cache.Instance.DirectEve.CorpBookmarkCurrentLocation(label, "", folder.Id);
                    }
                    else
                    {
                        Cache.Instance.DirectEve.CorpBookmarkCurrentLocation(label, "", null);
                    }
                }
                else
                {
                    DirectBookmarkFolder folder = Cache.Instance.DirectEve.BookmarkFolders.FirstOrDefault(i => i.Name == Settings.Instance.BookmarkFolder);
                    if (folder != null)
                    {
                        Cache.Instance.DirectEve.BookmarkCurrentLocation(label, "", folder.Id);
                    }
                    else
                    {
                        Cache.Instance.DirectEve.BookmarkCurrentLocation(label, "", null);
                    }
                }
            }
            else
            {
                Logging.Log("CreateBookmark", "We already have over 100 AfterMissionSalvage bookmarks: their must be a issue processing or deleting bookmarks. No additional bookmarks will be created until the number of salvage bookmarks drops below 100.", Logging.Orange);
            }

            return;
        }

        //public void CreateBookmarkofWreck(IEnumerable<EntityCache> containers, string label)
        //{
        //    DirectEve.BookmarkEntity(Cache.Instance.Containers.FirstOrDefault, "a", "a", null);
        //}

        private Func<EntityCache, int> OrderByLowestHealth()
        {
            return t => (int)(t.ShieldPct + t.ArmorPct + t.StructurePct);
        }

        //public List <long> BookMarkToDestination(DirectBookmark bookmark)
        //{
        //    Directdestination = new MissionBookmarkDestination(Cache.Instance.GetMissionBookmark(Cache.Instance.AgentId, "Encounter"));
        //    return List<long> destination;
        //}

        public DirectItem CheckCargoForItem(int typeIdToFind, int quantityToFind)
        {
            try
            {
                if (Cache.Instance.CurrentShipsCargo != null && Cache.Instance.CurrentShipsCargo.Items.Any())
                {
                    DirectItem item = Cache.Instance.CurrentShipsCargo.Items.FirstOrDefault(i => i.TypeId == typeIdToFind && i.Quantity >= quantityToFind);
                    return item;    
                }

                return null; // no items found   
            }
            catch (Exception exception)
            {
                Logging.Log("Cache.CheckCargoForItem", "Exception [" + exception + "]", Logging.Debug);
            }

            return null;
        }

        public bool CheckifRouteIsAllHighSec()
        {
            Cache.Instance.RouteIsAllHighSecBool = false;

            try
            {
                // Find the first waypoint
                if (DirectEve.Navigation.GetDestinationPath() != null && DirectEve.Navigation.GetDestinationPath().Count > 0)
                {
                    List<int> currentPath = DirectEve.Navigation.GetDestinationPath();
                    if (currentPath == null || !currentPath.Any()) return false;
                    if (currentPath[0] == 0) return false; //No destination set - prevents exception if somehow we have got an invalid destination

                    foreach (int _system in currentPath)
                    {
                        if (_system < 6000000) // not a station
                        {
                            DirectSolarSystem solarSystemInRoute = Cache.Instance.DirectEve.SolarSystems[_system];
                            if (solarSystemInRoute != null)
                            {
                                if (solarSystemInRoute.Security < 0.45)
                                {
                                    //Bad bad bad
                                    Cache.Instance.RouteIsAllHighSecBool = false;
                                    return true;
                                }
                            }

                            Logging.Log("CheckifRouteIsAllHighSec", "Jump number [" + _system + "of" + currentPath.Count() + "] in the route came back as null, we could not get the system name or sec level", Logging.Debug);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Logging.Log("Cache.CheckifRouteIsAllHighSec", "Exception [" + exception +"]", Logging.Debug);
            }
            

            //
            // if DirectEve.Navigation.GetDestinationPath() is null or 0 jumps then it must be safe (can we assume we are not in lowsec or 0.0 already?!)
            //
            Cache.Instance.RouteIsAllHighSecBool = true;
            return true;
        }

        public string MaskedID(long? ID)
        {
            try
            {
                if (ID != null)
                {
                    int numofCharacters = ID.ToString().Length;
                    if (numofCharacters >= 5)
                    {
                        string maskedID = ID.ToString().Substring(numofCharacters - 4);
                        maskedID = "[MaskedID]" + maskedID;
                        return maskedID;
                    }    
                }

                return "!0!";
            }
            catch (Exception exception)
            {
                Logging.Log("Cache.DoWeCurrentlyHaveTurretsMounted", "Exception [" + exception + "]", Logging.Debug);
                return "!0!";
            }
        }

        public void ClearPerPocketCache()
        {
            _doWeCurrentlyHaveTurretsMounted = null;
            ListOfWarpScramblingEntities.Clear();
            ListOfJammingEntities.Clear();
            ListOfTrackingDisruptingEntities.Clear();
            ListNeutralizingEntities.Clear();
            ListOfTargetPaintingEntities.Clear();
            ListOfDampenuingEntities.Clear();
            ListofWebbingEntities.Clear();

            EntityNames.Clear();
            EntityTypeID.Clear();
            EntityGroupID.Clear();
            EntityBounty.Clear();
            EntityIsFrigate.Clear();
            EntityIsNPCFrigate.Clear();
            EntityIsCruiser.Clear();
            EntityIsNPCCruiser.Clear();
            EntityIsBattleCruiser.Clear();
            EntityIsNPCBattleCruiser.Clear();
            EntityIsBattleShip.Clear();
            EntityIsNPCBattleShip.Clear();
            EntityIsHighValueTarget.Clear();
            EntityIsLowValueTarget.Clear();
            EntityIsLargeCollidable.Clear();
            EntityIsSentry.Clear();
            EntityIsMiscJunk.Clear();
            EntityIsBadIdea.Clear();
            EntityIsFactionWarfareNPC.Clear();
            EntityIsNPCByGroupID.Clear();
            EntityIsEntutyIShouldLeaveAlone.Clear();
            EntityHaveLootRights.Clear();
            EntityIsStargate.Clear();
        }

        private bool? _doWeCurrentlyHaveTurretsMounted;

        public bool DoWeCurrentlyHaveTurretsMounted()
        {
            try
            {
                if (_doWeCurrentlyHaveTurretsMounted == null)
                {
                    //int ModuleNumber = 0;
                    foreach (ModuleCache m in Cache.Instance.Modules)
                    {
                        if (m.GroupId == (int)Group.ProjectileWeapon
                         || m.GroupId == (int)Group.EnergyWeapon
                         || m.GroupId == (int)Group.HybridWeapon
                            //|| m.GroupId == (int)Group.CruiseMissileLaunchers
                            //|| m.GroupId == (int)Group.RocketLaunchers
                            //|| m.GroupId == (int)Group.StandardMissileLaunchers
                            //|| m.GroupId == (int)Group.TorpedoLaunchers
                            //|| m.GroupId == (int)Group.AssaultMissilelaunchers
                            //|| m.GroupId == (int)Group.HeavyMissilelaunchers
                            //|| m.GroupId == (int)Group.DefenderMissilelaunchers
                           )
                        {
                            _doWeCurrentlyHaveTurretsMounted = true;
                            return _doWeCurrentlyHaveTurretsMounted ?? true;
                        }

                        continue;
                    }

                    _doWeCurrentlyHaveTurretsMounted = false;
                    return _doWeCurrentlyHaveTurretsMounted ?? false;
                }

                return _doWeCurrentlyHaveTurretsMounted ?? false;
            }
            catch (Exception exception)
            {
                Logging.Log("Cache.DoWeCurrentlyHaveTurretsMounted", "Exception [" + exception + "]", Logging.Debug);
            }

            return false;
        }

        public EntityCache CurrentWeaponTarget()
        {
            // Find the first active weapon's target
            EntityCache _currentWeaponTarget = null;
            double OptimalOfWeapon = 0;
            double FallOffOfWeapon = 0;

            try
            {
                // Find the target associated with the weapon
                ModuleCache weapon = Cache.Instance.Weapons.FirstOrDefault(m => m.IsOnline
                                                                                    && !m.IsReloadingAmmo
                                                                                    && !m.IsChangingAmmo
                                                                                    && m.IsActive);
                if (weapon != null)
                {
                    _currentWeaponTarget = Cache.Instance.EntityById(weapon.TargetId);

                    //
                    // in a perfect world we'd always use the same guns / missiles across the board, for those that do not this will at least come up with sane numbers
                    //
                    if (OptimalOfWeapon <= 1)
                    {
                        OptimalOfWeapon = Math.Min(OptimalOfWeapon, weapon.OptimalRange);
                    }

                    if (FallOffOfWeapon <= 1)
                    {
                        FallOffOfWeapon = Math.Min(FallOffOfWeapon, weapon.FallOff);
                    }

                    if (_currentWeaponTarget != null && _currentWeaponTarget.IsReadyToShoot)
                    {
                        return _currentWeaponTarget;
                    }

                    return null;
                }

                return null;
            }
            catch (Exception exception)
            {
                Logging.Log("GetCurrentWeaponTarget", "exception [" + exception + "]", Logging.Debug);
            }

            return null;
        }

        private IEnumerable<EntityCache> _bestPrimaryWeaponTargets;

        public IEnumerable<EntityCache> __GetBestWeaponTargets(double distance, IEnumerable<EntityCache> _potentialTargets = null)
        {
            try
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log("Debug: GetBestTarget (Weapons):", "Attempting to Get Best Weapon Targets", Logging.Teal);

                if (Settings.Instance.DebugDisableGetBestTarget)
                {
                    _bestPrimaryWeaponTargets = _potentialTargets ?? PotentialCombatTargets.OrderBy(i => i.Nearest5kDistance).ToList();
                    _bestPrimaryWeaponTargets = _bestPrimaryWeaponTargets.ToList();
                }

                if (_bestPrimaryWeaponTargets == null)
                {
                    _bestPrimaryWeaponTargets = _potentialTargets ?? Cache.Instance.PotentialCombatTargets; //.ToList();
                    //_bestPrimaryWeaponTargets = _bestPrimaryWeaponTargets.ToList();
                    long? currentWeaponId = Cache.Instance.CurrentWeaponTarget() != null ? Cache.Instance.CurrentWeaponTarget().Id : -1;
                    long? preferredTargetId = Cache.Instance.PreferredPrimaryWeaponTargetID ?? -1;

                    if (MaxLockedTargets > 0 && !Settings.Instance.DebugDisableGetBestTarget) //if we are not ECMd
                    {
                        if (_bestPrimaryWeaponTargets.Any())
                        {
                            if (PreferredPrimaryWeaponTarget == null || (PreferredPrimaryWeaponTarget != null && !PreferredPrimaryWeaponTarget.IsOnGridWithMe) || DateTime.UtcNow > Cache.Instance.NextGetBestCombatTarget)
                            {
                                _bestPrimaryWeaponTargets = _bestPrimaryWeaponTargets.Where(t => t.Distance < distance)
                                                                        .OrderByDescending(t => t.IsInOptimalRange)
                                                                        .ThenByDescending(t => t.isPreferredPrimaryWeaponTarget && t.IsInOptimalRange)
                                                                        .ThenByDescending(t => t.IsCorrectSizeForMyWeapons)                            // Weapons should fire big targets first
                                                                        .ThenByDescending(t => !t.IsTooCloseTooFastTooSmallToHit)
                                                                        .ThenByDescending(t => t.isPreferredPrimaryWeaponTarget)
                                                                        .ThenByDescending(t => t.IsTargetedBy)                                         // if something does not target us it's not too interesting
                                                                        .ThenByDescending(t => t.PrimaryWeaponPriorityLevel)                           // WarpScram over Webs over any other EWAR
                                                                        .ThenByDescending(t => t.Id == currentWeaponId)                                // Lets keep shooting
                                                                        .ThenByDescending(t => t.Id == preferredTargetId)                              // Keep the preferred target so we do not switch our targets too often
                                                                        .ThenByDescending(t => t.IsEntityIShouldKeepShooting && !t.IsLowValueTarget)   // Shoot targets that are in armor!
                                                                        .ThenBy(t => t.Nearest5kDistance);

                                Cache.Instance.NextGetBestCombatTarget = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(8, 12));
                            }
                            else
                            {
                                if (Settings.Instance.DebugGetBestTarget) Logging.Log("__GetBestWeaponTarget", "PreferredPrimaryWeaponTarget [" + PreferredPrimaryWeaponTarget.Name + "] IsOnGridWithMe [" + PreferredPrimaryWeaponTarget.IsOnGridWithMe + "] Time until NextGetBestCombatTarget [" + Cache.Instance.NextGetBestCombatTarget.Subtract(DateTime.UtcNow).Seconds + "]", Logging.Debug);
                            }
                        }
                        else
                        {
                            if (Settings.Instance.DebugGetBestTarget) Logging.Log("__GetBestWeaponTarget", "We have nothing to shoot yet. Waiting", Logging.Debug);
                        }
                    }
                    else
                    {
                        if (Settings.Instance.DebugGetBestTarget && !Settings.Instance.DebugDisableGetBestTarget) Logging.Log("__GetBestWeaponTarget", "We have no targeting slots (ECMd?). Waiting", Logging.Debug);
                    }
                }


                if (_bestPrimaryWeaponTargets != null && _bestPrimaryWeaponTargets.Any())
                {
                    int BestPrimaryWeaponTargetsCount = _bestPrimaryWeaponTargets.Count();
                    if (_bestPrimaryWeaponTargets.FirstOrDefault() != null)
                    {
                        Cache.Instance.PreferredPrimaryWeaponTarget = _bestPrimaryWeaponTargets.FirstOrDefault();
                        if (Cache.Instance.PreferredPrimaryWeaponTarget != null)
                        {
                            if (Settings.Instance.DebugGetBestTarget) Logging.Log("Debug: GetBestTarget (Weapons):", "PreferredPrimaryWeaponTarget [" + Cache.Instance.PreferredPrimaryWeaponTarget.Name + "][" + Cache.Instance.MaskedID(Cache.Instance.PreferredPrimaryWeaponTarget.Id) + "]", Logging.Debug);
                        }
                        else if (Cache.Instance.PreferredPrimaryWeaponTarget == null)
                        {
                            if (Settings.Instance.DebugGetBestTarget) Logging.Log("Debug: GetBestTarget (Weapons):", "PreferredPrimaryWeaponTarget [ null ] huh?", Logging.Debug);
                        }
                    }

                    if (Settings.Instance.DebugGetBestTarget)
                    {
                        if (_bestPrimaryWeaponTargets.Any())
                        {
                            if (Cache.Instance.PreferredPrimaryWeaponTarget != null) Logging.Log("Debug: GetBestTarget (Weapons):", "PreferredPrimaryWeaponTarget [" + Cache.Instance.PreferredPrimaryWeaponTarget.Name + "][" + Math.Round(Cache.Instance.PreferredPrimaryWeaponTarget.Distance / 1000, 0) + "k][" + Cache.Instance.MaskedID(PreferredPrimaryWeaponTargetID) + "] BestPrimaryWeaponTargets Total [" + BestPrimaryWeaponTargetsCount + "]", Logging.Teal);
                            int i = 0;
                            foreach (EntityCache bestPrimaryWeaponTarget in _bestPrimaryWeaponTargets)
                            {
                                i++;
                                Logging.Log("GetBestTarget (Weapons):", "[" + i + "] BestPrimaryWeaponTarget [" + bestPrimaryWeaponTarget.Name + "][" + Math.Round(bestPrimaryWeaponTarget.Distance / 1000, 0) + "k][" + Cache.Instance.MaskedID(bestPrimaryWeaponTarget.Id) + "] IsInOptimal [" + bestPrimaryWeaponTarget.IsInOptimalRange + "] isCorrectSize [" + bestPrimaryWeaponTarget.IsCorrectSizeForMyWeapons + "] isPPWPT [" + bestPrimaryWeaponTarget.isPreferredPrimaryWeaponTarget + "] IsPWPT [" + bestPrimaryWeaponTarget.IsPrimaryWeaponPriorityTarget + "] IsLockedTarget [" + bestPrimaryWeaponTarget.IsTarget + "] IsTargetedBy [" + bestPrimaryWeaponTarget.IsTargetedBy + "] IsEwarTarget [" + bestPrimaryWeaponTarget.IsEwarTarget + "] IsWarpScramblingMe [" + bestPrimaryWeaponTarget.IsWarpScramblingMe + "]", Logging.Teal);
                            }
                        }
                    }
                }

                if (Settings.Instance.DebugGetBestTarget) Logging.Log("Debug: GetBestTarget (Weapons):", "return BestPrimaryWeaponTargets;", Logging.Debug);
                return _bestPrimaryWeaponTargets;
            }
            catch (Exception exception)
            {
                Logging.Log("__GetBestWeaponTargets", "Exception [" + exception + "]", Logging.Debug);
            }

            return _bestPrimaryWeaponTargets;
        }

        private IEnumerable<EntityCache> _bestDroneTargets;

        public IEnumerable<EntityCache> __GetBestDroneTargets(double distance, IEnumerable<EntityCache> _potentialTargets = null)
        {
            try
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log("Debug: GetBestTarget (Drones):", "Attempting to get Best Drone Target", Logging.Teal);

                if (Settings.Instance.DebugDisableGetBestTarget)
                {
                    _bestDroneTargets = _potentialTargets ?? PotentialCombatTargets.OrderBy(i => i.Nearest5kDistance).ThenByDescending(i => i.IsFrigate).ToList();
                }

                if (_bestDroneTargets == null)
                {
                    _bestDroneTargets = _potentialTargets ?? PotentialCombatTargets.ToList();
                    _bestDroneTargets = _bestDroneTargets.ToList();
                    //long currentDroneTargetId = TargetingCache.CurrentDronesTarget != null ? TargetingCache.CurrentDronesTarget.Id : -1;
                    long? preferredTargetId = Cache.Instance.PreferredDroneTargetID ?? -1;

                    if (_bestDroneTargets.Any())
                    {
                        _bestDroneTargets = _bestDroneTargets.Where(t => t.Distance < distance)
                                                                  .Where(t => t.Distance < Cache.Instance.MaxDroneRange)
                                                                  .OrderByDescending(t => t.isPreferredDroneTarget)
                                                                  .ThenByDescending(t => t.IsTargetedBy)                                      // if something does not target us it's not too interesting
                                                                  .ThenByDescending(t => t.IsTarget || t.IsTargeting)                         // is the entity already targeted?
                                                                  .ThenByDescending(t => t.Id == Cache.Instance.LastDroneTargetID)            // Keep current target
                                                                  .ThenByDescending(t => t.Id == preferredTargetId)                           // Keep the preferred target so we do not switch our targets too often
                                                                  .ThenByDescending(t => t.IsEntityIShouldKeepShootingWithDrones)             // Shoot targets that are in armor!
                                                                  .ThenByDescending(t => t.DronePriorityLevel)
                                                                  .ThenByDescending(t => (t.IsFrigate || t.IsNPCFrigate) || (Settings.Instance.DronesKillHighValueTargets && t.IsBattleship))
                                                                  .ThenByDescending(t => t.IsTooCloseTooFastTooSmallToHit)
                                                                  .ThenBy(t => t.Nearest5kDistance);


                        int BestDroneTargetsCount = _bestDroneTargets.Count();
                        if (_bestDroneTargets.FirstOrDefault() != null)
                        {
                            Cache.Instance.PreferredDroneTarget = _bestDroneTargets.FirstOrDefault();
                            if (Cache.Instance.PreferredDroneTarget != null)
                            {
                                if (Settings.Instance.DebugGetBestTarget) Logging.Log("Debug: GetBestTarget (Drones):", "PreferredDroneTarget [" + Cache.Instance.PreferredDroneTarget.Name + "][" + Cache.Instance.MaskedID(Cache.Instance.PreferredDroneTarget.Id) + "]", Logging.Debug);
                            }
                            else if (Cache.Instance.PreferredDroneTarget == null)
                            {
                                if (Settings.Instance.DebugGetBestTarget) Logging.Log("Debug: GetBestTarget (Drones):", "PreferredDroneTarget [ null ] huh?", Logging.Debug);
                            }
                        }

                        if (Settings.Instance.DebugGetBestTarget)
                        {
                            if (_bestDroneTargets.Any())
                            {
                                if (Cache.Instance.PreferredDroneTarget != null) Logging.Log("Debug: GetBestTarget (Drones):", "PreferredDroneTarget [" + PreferredDroneTarget.Name + "][" + Math.Round(PreferredDroneTarget.Distance / 1000, 0) + "k][" + Cache.Instance.MaskedID(PreferredDroneTargetID) + "] BestPrimaryWeaponTargets Total [" + BestDroneTargetsCount + "]", Logging.Teal);
                                int i = 0;
                                foreach (EntityCache bestDroneTarget in _bestDroneTargets)
                                {
                                    i++;
                                    Logging.Log("GetBestTarget (Drones):", "[" + i + "] BestPrimaryWeaponTarget [" + bestDroneTarget.Name + "][" + Math.Round(bestDroneTarget.Distance / 1000, 0) + "k][" + Cache.Instance.MaskedID(bestDroneTarget.Id) + "] IsPWPT [" + bestDroneTarget.IsPrimaryWeaponPriorityTarget + "] IsLockedTarget [" + bestDroneTarget.IsTarget + "] IsTargetedBy [" + bestDroneTarget.IsTargetedBy + "] IsEwarTarget [" + bestDroneTarget.IsEwarTarget + "] IsWarpScramblingMe [" + bestDroneTarget.IsWarpScramblingMe + "]", Logging.Teal);
                                }
                            }
                        }
                    }
                }

                return _bestDroneTargets;
            }
            catch (Exception exception)
            {
                Logging.Log("__GetBestWeaponTargets", "Exception [" + exception + "]", Logging.Debug);
            }

            return _bestDroneTargets;
        }

        public EntityCache FindPrimaryWeaponPriorityTarget(EntityCache currentTarget, PrimaryWeaponPriority priorityType, bool AddECMTypeToPrimaryWeaponPriorityTargetList, double Distance, bool FindAUnTargetedEntity = true)
        {
            if (AddECMTypeToPrimaryWeaponPriorityTargetList)
            {
                //if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget", "Checking for Neutralizing priority targets (currentTarget first)", Logging.Teal);
                // Choose any Neutralizing primary weapon priority targets
                try
                {
                    EntityCache target = null;
                    try
                    {
                        if (Cache.Instance.PrimaryWeaponPriorityEntities.Any(pt => pt.PrimaryWeaponPriorityLevel == priorityType))
                        {
                            target = Cache.Instance.PrimaryWeaponPriorityEntities.Where(pt => ((FindAUnTargetedEntity || pt.IsReadyToShoot) && currentTarget != null && pt.Id == currentTarget.Id && pt.Distance < Distance && pt.IsActivePrimaryWeaponEwarType == priorityType && !pt.IsTooCloseTooFastTooSmallToHit)
                                                                                            || ((FindAUnTargetedEntity || pt.IsReadyToShoot) && pt.Distance < Distance && pt.PrimaryWeaponPriorityLevel == priorityType && !pt.IsTooCloseTooFastTooSmallToHit))
                                                                                    .OrderByDescending(pt => pt.IsReadyToShoot)
                                                                                    .ThenByDescending(pt => pt.IsCurrentTarget)
                                                                                    .ThenByDescending(pt => !pt.IsNPCFrigate)
                                                                                    .ThenByDescending(pt => pt.IsInOptimalRange)
                                                                                    .ThenBy(pt => (pt.ShieldPct + pt.ArmorPct + pt.StructurePct))
                                                                                    .ThenBy(pt => pt.Distance)
                                                                                    .FirstOrDefault();
                        }
                    }
                    catch (NullReferenceException) { }  // Not sure why this happens, but seems to be no problem

                    if (target != null)
                    {
                        if (!FindAUnTargetedEntity)
                        {
                            //if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget", "NeutralizingPrimaryWeaponPriorityTarget [" + NeutralizingPriorityTarget.Name + "][" + Math.Round(NeutralizingPriorityTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(NeutralizingPriorityTarget.Id) + "] GroupID [" + NeutralizingPriorityTarget.GroupId + "]", Logging.Debug);
                            Logging.Log("FindPrimaryWeaponPriorityTarget", "if (!FindAUnTargetedEntity) Cache.Instance.PreferredPrimaryWeaponTargetID = [ " + target.Name + "][" + Cache.Instance.MaskedID(target.Id) + "]", Logging.White);
                            Cache.Instance.PreferredPrimaryWeaponTarget = target;
                            Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                            return target;
                        }

                        return target;
                    }

                    return null;
                }
                catch (NullReferenceException) { }

                return null;
            }

            return null;
        }

        public EntityCache FindDronePriorityTarget(EntityCache currentTarget, DronePriority priorityType, bool AddECMTypeToDronePriorityTargetList, double Distance, bool FindAUnTargetedEntity = true)
        {
            if (AddECMTypeToDronePriorityTargetList)
            {
                //if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget", "Checking for Neutralizing priority targets (currentTarget first)", Logging.Teal);
                // Choose any Neutralizing primary weapon priority targets
                try
                {
                    EntityCache target = null;
                    try
                    {
                        if (Cache.Instance.DronePriorityEntities.Any(pt => pt.DronePriorityLevel == priorityType))
                        {
                            target = Cache.Instance.DronePriorityEntities.Where(pt => ((FindAUnTargetedEntity || pt.IsReadyToShoot) && currentTarget != null && pt.Id == currentTarget.Id && (pt.Distance < Distance) && pt.IsActiveDroneEwarType == priorityType)
                                                                                                || ((FindAUnTargetedEntity || pt.IsReadyToShoot) && pt.Distance < Distance && pt.IsActiveDroneEwarType == priorityType))
                                                                                                       .OrderByDescending(pt => !pt.IsNPCFrigate)
                                                                                                       .ThenByDescending(pt => pt.IsCurrentDroneTarget)
                                                                                                       .ThenByDescending(pt => pt.IsInDroneRange)
                                                                                                       .ThenBy(pt => pt.IsEntityIShouldKeepShootingWithDrones)
                                                                                                       .ThenBy(pt => (pt.ShieldPct + pt.ArmorPct + pt.StructurePct))
                                                                                                       .ThenBy(pt => pt.Nearest5kDistance)
                                                                                                       .FirstOrDefault();
                        }
                    }
                    catch (NullReferenceException) { }  // Not sure why this happens, but seems to be no problem

                    if (target != null)
                    {
                        //if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget", "NeutralizingPrimaryWeaponPriorityTarget [" + NeutralizingPriorityTarget.Name + "][" + Math.Round(NeutralizingPriorityTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(NeutralizingPriorityTarget.Id) + "] GroupID [" + NeutralizingPriorityTarget.GroupId + "]", Logging.Debug);
                        
                        if (!FindAUnTargetedEntity)
                        {
                            Cache.Instance.PreferredDroneTarget = target;
                            Cache.Instance.LastPreferredDroneTargetDateTime = DateTime.UtcNow;
                            return target;
                        }

                        return target;
                    }

                    return null;
                }
                catch (NullReferenceException) { }

                return null;
            }

            return null;
        }

        public EntityCache FindCurrentTarget()
        {
            EntityCache currentTarget = null;
            if (Cache.Instance.CurrentWeaponTarget() != null
                && Cache.Instance.CurrentWeaponTarget().IsReadyToShoot
                && !Cache.Instance.CurrentWeaponTarget().IsIgnored)
            {
                currentTarget = Cache.Instance.CurrentWeaponTarget();
            }

            if (DateTime.UtcNow < Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime.AddSeconds(6) && (Cache.Instance.PreferredPrimaryWeaponTarget != null && Cache.Instance.EntitiesOnGrid.Any(t => t.Id == Cache.Instance.PreferredPrimaryWeaponTargetID)))
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log("FindCurrentTarget", "We have a PreferredPrimaryWeaponTarget [" + Cache.Instance.PreferredPrimaryWeaponTarget.Name + "][" + Math.Round(Cache.Instance.PreferredPrimaryWeaponTarget.Distance / 1000, 0) + "k] that was chosen less than 6 sec ago, and is still alive.", Logging.Teal);
                return currentTarget;
            }

            return currentTarget;
        }

        public bool CheckForPrimaryWeaponPriorityTargetsInOrder(EntityCache currentTarget, double distance)
        {
            // Do we have ANY warp scrambling entities targeted starting with currentTarget
            // this needs Settings.Instance.AddWarpScramblersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindPrimaryWeaponPriorityTarget(currentTarget, PrimaryWeaponPriority.WarpScrambler, Settings.Instance.AddWarpScramblersToPrimaryWeaponsPriorityTargetList, distance) != null)
                return true;

            // Do we have ANY ECM entities targeted starting with currentTarget
            // this needs Settings.Instance.AddECMsToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindPrimaryWeaponPriorityTarget(currentTarget, PrimaryWeaponPriority.Jamming, Settings.Instance.AddECMsToPrimaryWeaponsPriorityTargetList, distance) != null)
                return true;

            // Do we have ANY tracking disrupting entities targeted starting with currentTarget
            // this needs Settings.Instance.AddTrackingDisruptorsToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindPrimaryWeaponPriorityTarget(currentTarget, PrimaryWeaponPriority.TrackingDisrupting, Settings.Instance.AddTrackingDisruptorsToPrimaryWeaponsPriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Neutralizing entities targeted starting with currentTarget
            // this needs Settings.Instance.AddNeutralizersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindPrimaryWeaponPriorityTarget(currentTarget, PrimaryWeaponPriority.Neutralizing, Settings.Instance.AddNeutralizersToPrimaryWeaponsPriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Target Painting entities targeted starting with currentTarget
            // this needs Settings.Instance.AddTargetPaintersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindPrimaryWeaponPriorityTarget(currentTarget, PrimaryWeaponPriority.TargetPainting, Settings.Instance.AddTargetPaintersToPrimaryWeaponsPriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Sensor Dampening entities targeted starting with currentTarget
            // this needs Settings.Instance.AddDampenersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindPrimaryWeaponPriorityTarget(currentTarget, PrimaryWeaponPriority.Dampening, Settings.Instance.AddDampenersToPrimaryWeaponsPriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Webbing entities targeted starting with currentTarget
            // this needs Settings.Instance.AddWebifiersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindPrimaryWeaponPriorityTarget(currentTarget, PrimaryWeaponPriority.Webbing, Settings.Instance.AddWebifiersToPrimaryWeaponsPriorityTargetList, distance) != null)
                return true;

            return false;
        }

        /// <summary>
        ///   Return the best possible target (based on current target, distance and low value first)
        /// </summary>
        /// <param name="_potentialTargets"></param>
        /// <param name="distance"></param>
        /// <param name="lowValueFirst"></param>
        /// <param name="callingroutine"> </param>
        /// <returns></returns>
        public bool GetBestPrimaryWeaponTarget(double distance, bool lowValueFirst, string callingroutine, List<EntityCache> _potentialTargets = null)
        {
            if (Settings.Instance.DebugDisableGetBestTarget)
            {
                return true;
            }

            if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Attempting to get Best Target", Logging.Teal);
            
            if (DateTime.UtcNow < NextGetBestCombatTarget)
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "No need to run GetBestTarget again so soon. We only want to run once per tick", Logging.Teal);
                return false;
            }

            NextGetBestCombatTarget = DateTime.UtcNow.AddMilliseconds(800);

            //if (!Cache.Instance.Targets.Any()) //&& _potentialTargets == null )
            //{
            //    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "We have no locked targets and [" + Cache.Instance.Targeting.Count() + "] targets being locked atm", Logging.Teal);
            //    return false;
            //}

            EntityCache currentTarget = FindCurrentTarget();
            
            //We need to make sure that our current Preferred is still valid, if not we need to clear it out
            //This happens when we have killed the last thing within our range (or the last thing in the pocket)
            //and there is nothing to replace it with.
            //if (Cache.Instance.PreferredPrimaryWeaponTarget != null
            //    && Cache.Instance.Entities.All(t => t.Id != Instance.PreferredPrimaryWeaponTargetID))
            //{
            //    if (Settings.Instance.DebugGetBestTarget) Logging.Log("GetBestTarget", "PreferredPrimaryWeaponTarget is not valid, clearing it", Logging.White);
            //    Cache.Instance.PreferredPrimaryWeaponTarget = null;
            //}

            //
            // process the list of PrimaryWeaponPriorityTargets in this order... Eventually the order itself should be user selectable
            // this allow us to kill the most 'important' things doing e-war first instead of just handling them by range
            //

            //
            // if currentTarget set to something (not null) and it is actually an entity...
            //
            if (currentTarget != null && currentTarget.IsReadyToShoot)
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons): currentTarget", "We have a target, testing conditions", Logging.Teal);

                #region Is our current target any other primary weapon priority target?
                //
                // Is our current target any other primary weapon priority target? AND if our target is just a PriorityKillTarget assume ALL E-war is more important.
                //
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons): currentTarget", "Checking Priority", Logging.Teal);
                if (PrimaryWeaponPriorityEntities.Any(pt => pt.IsReadyToShoot 
                                                        && pt.Distance < Cache.Instance.MaxRange
                                                        && pt.IsCurrentTarget
                                                        && !currentTarget.IsHigherPriorityPresent))
                {
                    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "CurrentTarget [" + currentTarget.Name + "][" + Math.Round(currentTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(currentTarget.Id) + "] GroupID [" + currentTarget.GroupId + "]", Logging.Debug);
                    Cache.Instance.PreferredPrimaryWeaponTarget = currentTarget;
                    Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                    return true;
                }
                #endregion Is our current target any other primary weapon priority target?

                /*
                #region Current Target Health Logging
                //
                // Current Target Health Logging
                //
                bool currentTargetHealthLogNow = true;
                if (Settings.Instance.DetailedCurrentTargetHealthLogging)
                {
                    if ((int)currentTarget.Id != (int)TargetingCache.CurrentTargetID)
                    {
                        if ((int)currentTarget.ArmorPct == 0 && (int)currentTarget.ShieldPct == 0 && (int)currentTarget.StructurePct == 0)
                        {
                            //assume that any NPC with no shields, armor or hull is dead or does not yet have valid data associated with it
                        }
                        else
                        {
                            //
                            // assign shields and armor to targetingcache variables - compare them to each other
                            // to see if we need to send another log message to the console, if the values have not changed no need to log it.
                            //
                            if ((int)currentTarget.ShieldPct >= TargetingCache.CurrentTargetShieldPct ||
                                (int)currentTarget.ArmorPct >= TargetingCache.CurrentTargetArmorPct ||
                                (int)currentTarget.StructurePct >= TargetingCache.CurrentTargetStructurePct)
                            {
                                currentTargetHealthLogNow = false;
                            }

                            //
                            // now that we are done comparing - assign new values for this tick
                            //
                            TargetingCache.CurrentTargetShieldPct = (int)currentTarget.ShieldPct;
                            TargetingCache.CurrentTargetArmorPct = (int)currentTarget.ArmorPct;
                            TargetingCache.CurrentTargetStructurePct = (int)currentTarget.StructurePct;
                            if (currentTargetHealthLogNow)
                            {
                                Logging.Log(callingroutine, ".GetBestTarget (Weapons): CurrentTarget is [" + currentTarget.Name +                              //name
                                            "][" + (Math.Round(currentTarget.Distance / 1000, 0)).ToString(CultureInfo.InvariantCulture) +           //distance
                                            "k][Shield%:[" + Math.Round(currentTarget.ShieldPct * 100, 0).ToString(CultureInfo.InvariantCulture) +   //shields
                                            "][Armor%:[" + Math.Round(currentTarget.ArmorPct * 100, 0).ToString(CultureInfo.InvariantCulture) + "]" //armor
                                            , Logging.White);
                            }
                        }
                    }
                }

                #endregion Current Target Health Logging
                */

                #region Is our current target already in armor? keep shooting the same target if so...
                //
                // Is our current target already in armor? keep shooting the same target if so...
                //
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons): currentTarget", "Checking Low Health", Logging.Teal);
                if (currentTarget.IsEntityIShouldKeepShooting)
                {
                    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "CurrentTarget [" + currentTarget.Name + "][" + Math.Round(currentTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(currentTarget.Id) + " GroupID [" + currentTarget.GroupId + "]] has less than 60% armor, keep killing this target", Logging.Debug);
                    Cache.Instance.PreferredPrimaryWeaponTarget = currentTarget;
                    Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                    return true;
                }

                #endregion Is our current target already in armor? keep shooting the same target if so...
                
                #region If none of the above matches, does our current target meet the conditions of being hittable and in range
                if (!currentTarget.IsHigherPriorityPresent)
                {
                    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons): currentTarget", "Does the currentTarget exist? can it be hit?", Logging.Teal);
                    if (currentTarget.IsReadyToShoot
                        && (!currentTarget.IsNPCFrigate || (!Cache.Instance.UseDrones && !currentTarget.IsTooCloseTooFastTooSmallToHit)) 
                        && currentTarget.Distance < Cache.Instance.MaxRange)
                    {
                        if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "if  the currentTarget exists and the target is the right size then continue shooting it;", Logging.Debug);
                        if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "currentTarget is [" + currentTarget.Name + "][" + Math.Round(currentTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(currentTarget.Id) + "] GroupID [" + currentTarget.GroupId + "]", Logging.Debug);

                        Cache.Instance.PreferredPrimaryWeaponTarget = currentTarget;
                        Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                        return true;
                    }
                }
                #endregion
            }

            if (CheckForPrimaryWeaponPriorityTargetsInOrder(currentTarget, distance)) return true;

            #region Get the closest primary weapon priority target
            //
            // Get the closest primary weapon priority target
            //
            if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Checking Closest PrimaryWeaponPriorityTarget", Logging.Teal);
            EntityCache primaryWeaponPriorityTarget = null;
            try
            {
                primaryWeaponPriorityTarget = Cache.Instance.PrimaryWeaponPriorityEntities.Where(p => p.Distance < Cache.Instance.MaxRange
                                                                            && !p.IsIgnored
                                                                            && p.IsReadyToShoot
                                                                            && ((!p.IsNPCFrigate && !p.IsFrigate ) || (!Cache.Instance.UseDrones && !p.IsTooCloseTooFastTooSmallToHit)))
                                                                           .OrderByDescending(pt => pt.IsTargetedBy)
                                                                           .ThenByDescending(pt =>  pt.IsCurrentTarget)
                                                                           .ThenByDescending(pt => pt.IsInOptimalRange)
                                                                           .ThenByDescending(pt => pt.IsEwarTarget)
                                                                           .ThenBy(pt => pt.PrimaryWeaponPriorityLevel)
                                                                           .ThenByDescending(pt => pt.TargetValue)
                                                                           .ThenBy(pt => pt.Nearest5kDistance)
                                                                           .FirstOrDefault();
            }
            catch (NullReferenceException) { }  // Not sure why this happens, but seems to be no problem

            if (primaryWeaponPriorityTarget != null)
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "primaryWeaponPriorityTarget is [" + primaryWeaponPriorityTarget.Name + "][" + Math.Round(primaryWeaponPriorityTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(primaryWeaponPriorityTarget.Id) + "] GroupID [" + primaryWeaponPriorityTarget.GroupId + "]", Logging.Debug);
                Cache.Instance.PreferredPrimaryWeaponTarget = primaryWeaponPriorityTarget;
                Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                return true;
            }

            #endregion Get the closest primary weapon priority target

            #region did our calling routine (CombatMissionCtrl?) pass us targets to shoot?
            //
            // This is where CombatMissionCtrl would pass targets to GetBestTarget
            //
            if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Checking Calling Target", Logging.Teal);
            if (_potentialTargets != null && _potentialTargets.Any())
            {
                EntityCache callingTarget = null;
                try
                {
                    callingTarget = _potentialTargets.OrderBy(t => t.Distance).FirstOrDefault();
                }
                catch (NullReferenceException) { }

                if (callingTarget != null && (callingTarget.IsReadyToShoot || callingTarget.IsLargeCollidable)
                        //((!callingTarget.IsNPCFrigate && !callingTarget.IsFrigate)                      
                        //|| (!Cache.Instance.UseDrones && !callingTarget.IsTooCloseTooFastTooSmallToHit))   
                   )
                {
                    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "if (callingTarget != null && !callingTarget.IsIgnored)", Logging.Debug);
                    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "callingTarget is [" + callingTarget.Name + "][" + Math.Round(callingTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(callingTarget.Id) + "] GroupID [" + callingTarget.GroupId + "]", Logging.Debug);
                    AddPrimaryWeaponPriorityTarget(callingTarget,PrimaryWeaponPriority.PriorityKillTarget, "GetBestTarget: callingTarget");
                    Cache.Instance.PreferredPrimaryWeaponTarget = callingTarget;
                    Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                    return true;
                }

                //return false; //do not return here, continue to process targets, we did not find one yet
            }
            #endregion

            #region Get the closest High Value Target

            if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Checking Closest High Value", Logging.Teal);
            EntityCache highValueTarget = null;

            if (PotentialCombatTargets.Any())
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "get closest: if (potentialCombatTargets.Any())", Logging.Teal);

                highValueTarget = PotentialCombatTargets.Where(t => t.IsHighValueTarget && t.IsReadyToShoot)
                    .OrderByDescending(t => !t.IsNPCFrigate)
                    .ThenByDescending(t => t.IsTargetedBy)
                    .ThenByDescending(t => !t.IsTooCloseTooFastTooSmallToHit)
                    .ThenByDescending(t => t.IsInOptimalRange)
                    .ThenByDescending(pt => pt.TargetValue) //highest value first
                    .ThenByDescending(t => !t.IsCruiser)
                    .ThenBy(OrderByLowestHealth())
                    .ThenBy(t => t.Nearest5kDistance)
                    .FirstOrDefault();
            }
            #endregion

            #region Get the closest low value target that is not moving too fast for us to hit
            //
            // Get the closest low value target //excluding things going too fast for guns to hit (if you have guns fitted)
            //
            if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Checking closest Low Value", Logging.Teal);
            EntityCache lowValueTarget = null;
            if (PotentialCombatTargets.Any())
            {
                lowValueTarget = PotentialCombatTargets.Where(t => t.IsLowValueTarget && t.IsReadyToShoot)
                    .OrderByDescending(t => t.IsNPCFrigate)
                    .ThenByDescending(t => t.IsTargetedBy)
                    .ThenByDescending(t => t.IsTooCloseTooFastTooSmallToHit) //this will return false (not to close to fast to small), then true due to .net sort order of bools
                    .ThenBy(pt => pt.TargetValue) //lowest value first
                    .ThenBy(OrderByLowestHealth())
                    .ThenBy(t => t.Nearest5kDistance)
                    .FirstOrDefault();
            }
            #endregion

            #region If lowValueFirst && lowValue aggrod or no high value aggrod
            if ((lowValueFirst && lowValueTarget != null)
                    && (lowValueTarget.IsTargetedBy 
                    || (highValueTarget == null 
                        || (highValueTarget != null 
                        && !highValueTarget.IsTargetedBy))))
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Checking Low Value First", Logging.Teal);
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "lowValueTarget is [" + lowValueTarget.Name + "][" + Math.Round(lowValueTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(lowValueTarget.Id) + "] GroupID [" + lowValueTarget.GroupId + "]", Logging.Debug);
                Cache.Instance.PreferredPrimaryWeaponTarget = lowValueTarget;
                Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                return true;
            }
            #endregion
            
            #region High Value - aggrod, or no low value aggrod
            // high value if aggrod
            // if no high value aggrod, low value thats aggrod
            // if no high aggro, and no low aggro, shoot high value thats present
            if (highValueTarget != null)
            {
                if (highValueTarget.IsTargetedBy 
                    || Cache.Instance.UseDrones
                    || (lowValueTarget == null 
                        || (lowValueTarget != null 
                        && !lowValueTarget.IsTargetedBy)))
                {
                    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Checking Use High Value", Logging.Teal);
                    if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "highValueTarget is [" + highValueTarget.Name + "][" + Math.Round(highValueTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(highValueTarget.Id) + "] GroupID [" + highValueTarget.GroupId + "]", Logging.Debug);
                    Cache.Instance.PreferredPrimaryWeaponTarget = highValueTarget;
                    Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                    return true;
                }
            }
            #endregion

            #region If we do not have a high value target but we do have a low value target
            if (lowValueTarget != null)
            {
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "Checking use Low Value", Logging.Teal);
                if (Settings.Instance.DebugGetBestTarget) Logging.Log(callingroutine + " Debug: GetBestTarget (Weapons):", "lowValueTarget is [" + lowValueTarget.Name + "][" + Math.Round(lowValueTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(lowValueTarget.Id) + "] GroupID [" + lowValueTarget.GroupId + "]", Logging.Debug);
                Cache.Instance.PreferredPrimaryWeaponTarget = lowValueTarget;
                Cache.Instance.LastPreferredPrimaryWeaponTargetDateTime = DateTime.UtcNow;
                return true;
            }
            #endregion

            if (Settings.Instance.DebugGetBestTarget) Logging.Log("GetBestTarget: none", "Could not determine a suitable target", Logging.Debug);
            #region If we did not find anything at all (wtf!?!?)
            if (Settings.Instance.DebugGetBestTarget)
            {
                if (Cache.Instance.Targets.Any())
                {
                    Logging.Log("GetBestTarget (Weapons): none", ".", Logging.Debug);
                    Logging.Log("GetBestTarget (Weapons): none", "*** ALL LOCKED/LOCKING TARGETS LISTED BELOW", Logging.Debug);
                    int LockedTargetNumber = 0; 
                    foreach (EntityCache __target in Cache.Instance.Targets)
                    {
                        LockedTargetNumber++;
                        Logging.Log("GetBestTarget (Weapons): none", "*** Target: [" + LockedTargetNumber + "][" + __target.Name + "][" + Math.Round(__target.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(__target.Id) + "][isTarget: " + __target.IsTarget + "][isTargeting: " + __target.IsTargeting + "] GroupID [" + __target.GroupId + "]", Logging.Debug);
                    }
                    Logging.Log("GetBestTarget (Weapons): none", "*** ALL LOCKED/LOCKING TARGETS LISTED ABOVE", Logging.Debug);
                    Logging.Log("GetBestTarget (Weapons): none", ".", Logging.Debug);
                }

                if (Cache.Instance.PotentialCombatTargets.Any(t => !t.IsTarget && !t.IsTargeting))
                {
                    if (Cache.Instance.IgnoreTargets.Any())
                    {
                        int IgnoreCount = Cache.Instance.IgnoreTargets.Count;
                        Logging.Log("GetBestTarget (Weapons): none", "Ignore List has [" + IgnoreCount + "] Entities in it.", Logging.Debug);
                    }

                    Logging.Log("GetBestTarget (Weapons): none", "***** ALL [" + Cache.Instance.PotentialCombatTargets.Count() + "] potentialCombatTargets LISTED BELOW (not yet targeted or targeting)", Logging.Debug);
                    int potentialCombatTargetNumber = 0;
                    foreach (EntityCache potentialCombatTarget in Cache.Instance.PotentialCombatTargets)
                    {
                        potentialCombatTargetNumber++;
                        Logging.Log("GetBestTarget (Weapons): none", "***** Unlocked [" + potentialCombatTargetNumber  + "]: [" + potentialCombatTarget.Name + "][" + Math.Round(potentialCombatTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(potentialCombatTarget.Id) + "][isTarget: " + potentialCombatTarget.IsTarget + "] GroupID [" + potentialCombatTarget.GroupId + "]", Logging.Debug);
                    }
                    Logging.Log("GetBestTarget (Weapons): none", "***** ALL [" + Cache.Instance.PotentialCombatTargets.Count() + "] potentialCombatTargets LISTED ABOVE (not yet targeted or targeting)", Logging.Debug);
                    Logging.Log("GetBestTarget (Weapons): none", ".", Logging.Debug);
                }
            }
            #endregion

            NextGetBestCombatTarget = DateTime.UtcNow;
            return false;
        }

        public bool GetBestDroneTarget(double distance, bool highValueFirst, string callingroutine, List<EntityCache> _potentialTargets = null)
        {
            if (Settings.Instance.DebugDisableGetBestDroneTarget || !Cache.Instance.UseDrones)
            {
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: DebugGetBestDroneTarget:", "!Cache.Instance.UseDrones - drones are disabled currently", Logging.Teal);
                return true;
            }

            if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: DebugGetBestDroneTarget:", "Attempting to get Best Drone Target", Logging.Teal);

            if (DateTime.UtcNow < NextGetBestDroneTarget)
            {
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: DebugGetBestDroneTarget:", "Cant GetBest yet....Too Soon!", Logging.Teal);
                return false;
            }

            NextGetBestDroneTarget = DateTime.UtcNow.AddMilliseconds(2000);

            //if (!Cache.Instance.Targets.Any()) //&& _potentialTargets == null )
            //{
            //    if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: DebugGetBestDroneTarget:", "We have no locked targets and [" + Cache.Instance.Targeting.Count() + "] targets being locked atm", Logging.Teal);
            //    return false;
            //}

            EntityCache currentDroneTarget = null;

            if (Cache.Instance.EntitiesOnGrid.Any(i => i.IsCurrentDroneTarget))
            {
                currentDroneTarget = Cache.Instance.EntitiesOnGrid.FirstOrDefault(i => i.IsCurrentDroneTarget);

            }
            
            if (DateTime.UtcNow < Cache.Instance.LastPreferredDroneTargetDateTime.AddSeconds(6) && (Cache.Instance.PreferredDroneTarget != null && Cache.Instance.EntitiesOnGrid.Any(t => t.Id == Cache.Instance.PreferredDroneTarget.Id)))
            {
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget:", "We have a PreferredDroneTarget [" + Cache.Instance.PreferredDroneTarget.Name + "] that was chosen less than 6 sec ago, and is still alive.", Logging.Teal);
                return true;
            }

            //We need to make sure that our current Preferred is still valid, if not we need to clear it out
            //This happens when we have killed the last thing within our range (or the last thing in the pocket)
            //and there is nothing to replace it with.
            //if (Cache.Instance.PreferredDroneTarget != null
            //    && Cache.Instance.Entities.All(t => t.Id != Instance.PreferredDroneTargetID))
            //{
            //    if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log("GetBestDroneTarget", "PreferredDroneTarget is not valid, clearing it", Logging.White);
            //    Cache.Instance.PreferredDroneTarget = null;
            //}

            //
            // if currentTarget set to something (not null) and it is actually an entity...
            //
            if (currentDroneTarget != null && currentDroneTarget.IsReadyToShoot && currentDroneTarget.IsLowValueTarget)
            {
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget (Drones): currentDroneTarget", "We have a currentTarget [" + currentDroneTarget.Name + "][" + currentDroneTarget.MaskedId + "][" + Math.Round(currentDroneTarget.Distance / 1000, 2) + "k], testing conditions", Logging.Teal);

                #region Is our current target any other drone priority target?
                //
                // Is our current target any other drone priority target? AND if our target is just a PriorityKillTarget assume ALL E-war is more important.
                //
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget (Drones): currentTarget", "Checking Priority", Logging.Teal);
                if (DronePriorityEntities.Any(pt => pt.IsReadyToShoot
                                                        && pt.Nearest5kDistance < Cache.Instance.MaxDroneRange
                                                        && pt.Id == currentDroneTarget.Id
                                                        && !currentDroneTarget.IsHigherPriorityPresent))
                {
                    if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget (Drones):", "CurrentTarget [" + currentDroneTarget.Name + "][" + Math.Round(currentDroneTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(currentDroneTarget.Id) + "] GroupID [" + currentDroneTarget.GroupId + "]", Logging.Debug);
                    Cache.Instance.PreferredDroneTarget = currentDroneTarget;
                    Cache.Instance.LastPreferredDroneTargetDateTime = DateTime.UtcNow;
                    return true;
                }
                #endregion Is our current target any other drone priority target?

                #region Is our current target already in armor? keep shooting the same target if so...
                //
                // Is our current target already low health? keep shooting the same target if so...
                //
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget: currentDroneTarget", "Checking Low Health", Logging.Teal);
                if (currentDroneTarget.IsEntityIShouldKeepShootingWithDrones)
                {
                    if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget:", "currentDroneTarget [" + currentDroneTarget.Name + "][" + Math.Round(currentDroneTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(currentDroneTarget.Id) + " GroupID [" + currentDroneTarget.GroupId + "]] has less than 80% shields, keep killing this target", Logging.Debug);
                    Cache.Instance.PreferredDroneTarget = currentDroneTarget;
                    Cache.Instance.LastPreferredDroneTargetDateTime = DateTime.UtcNow;
                    return true;
                }

                #endregion Is our current target already in armor? keep shooting the same target if so...

                #region If none of the above matches, does our current target meet the conditions of being hittable and in range
                if (!currentDroneTarget.IsHigherPriorityPresent)
                {
                    if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget: currentDroneTarget", "Does the currentTarget exist? Can it be hit?", Logging.Teal);
                    if (currentDroneTarget.IsReadyToShoot && currentDroneTarget.Nearest5kDistance < Cache.Instance.MaxDroneRange)
                    {
                        if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget:", "if  the currentDroneTarget exists and the target is the right size then continue shooting it;", Logging.Debug);
                        if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " Debug: GetBestDroneTarget:", "currentDroneTarget is [" + currentDroneTarget.Name + "][" + Math.Round(currentDroneTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(currentDroneTarget.Id) + "] GroupID [" + currentDroneTarget.GroupId + "]", Logging.Debug);

                        Cache.Instance.PreferredDroneTarget = currentDroneTarget;
                        Cache.Instance.LastPreferredDroneTargetDateTime = DateTime.UtcNow;
                        return true;
                    }
                }
                #endregion
            }

            //
            // process the list of PrimaryWeaponPriorityTargets in this order... Eventually the order itself should be user selectable
            // this allow us to kill the most 'important' things doing e-war first instead of just handling them by range
            //

            // Do we have ANY warp scrambling entities targeted starting with currentTarget
            // this needs Settings.Instance.AddWarpScramblersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindDronePriorityTarget(currentDroneTarget, DronePriority.WarpScrambler, Settings.Instance.AddWarpScramblersToDronePriorityTargetList, distance) != null)
                return true;

            // Do we have ANY ECM entities targeted starting with currentTarget
            // this needs Settings.Instance.AddECMsToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindDronePriorityTarget(currentDroneTarget, DronePriority.Webbing, Settings.Instance.AddECMsToDroneTargetList, distance) != null)
                return true;

            // Do we have ANY tracking disrupting entities targeted starting with currentTarget
            // this needs Settings.Instance.AddTrackingDisruptorsToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindDronePriorityTarget(currentDroneTarget, DronePriority.PriorityKillTarget, Settings.Instance.AddTrackingDisruptorsToDronePriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Neutralizing entities targeted starting with currentTarget
            // this needs Settings.Instance.AddNeutralizersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindDronePriorityTarget(currentDroneTarget, DronePriority.PriorityKillTarget, Settings.Instance.AddNeutralizersToDronePriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Target Painting entities targeted starting with currentTarget
            // this needs Settings.Instance.AddTargetPaintersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindDronePriorityTarget(currentDroneTarget, DronePriority.PriorityKillTarget, Settings.Instance.AddTargetPaintersToDronePriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Sensor Dampening entities targeted starting with currentTarget
            // this needs Settings.Instance.AddDampenersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindDronePriorityTarget(currentDroneTarget, DronePriority.PriorityKillTarget, Settings.Instance.AddDampenersToDronePriorityTargetList, distance) != null)
                return true;

            // Do we have ANY Webbing entities targeted starting with currentTarget
            // this needs Settings.Instance.AddWebifiersToPrimaryWeaponsPriorityTargetList true, otherwise they will just get handled in any order below...
            if (Cache.Instance.FindDronePriorityTarget(currentDroneTarget, DronePriority.PriorityKillTarget, Settings.Instance.AddWebifiersToDronePriorityTargetList, distance) != null)
                return true;

            #region Get the closest drone priority target
            //
            // Get the closest primary weapon priority target
            //
            if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "Checking Closest DronePriorityTarget", Logging.Teal);
            EntityCache dronePriorityTarget = null;
            try
            {
                dronePriorityTarget = Cache.Instance.DronePriorityEntities.Where(p => p.Nearest5kDistance < Cache.Instance.MaxDroneRange
                                                                            && !p.IsIgnored
                                                                            && p.IsReadyToShoot)
                                                                           .OrderBy(pt => pt.DronePriorityLevel)
                                                                           .ThenByDescending(pt => pt.IsTargetedBy)
                                                                           .ThenByDescending(pt => pt.IsEwarTarget)
                                                                           .ThenBy(pt => pt.Nearest5kDistance)
                                                                           .FirstOrDefault();
            }
            catch (NullReferenceException) { }  // Not sure why this happens, but seems to be no problem

            if (dronePriorityTarget != null)
            {
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "dronePriorityTarget is [" + dronePriorityTarget.Name + "][" + Math.Round(dronePriorityTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(dronePriorityTarget.Id) + "] GroupID [" + dronePriorityTarget.GroupId + "]", Logging.Debug);
                Cache.Instance.PreferredDroneTarget = dronePriorityTarget;
                Cache.Instance.LastPreferredDroneTargetDateTime = DateTime.UtcNow;
                return true;
            }

            #endregion Get the closest drone priority target

            #region did our calling routine (CombatMissionCtrl?) pass us targets to shoot?
            //
            // This is where CombatMissionCtrl would pass targets to GetBestDroneTarget
            //
            if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "Checking Calling Target", Logging.Teal);
            if (_potentialTargets != null && _potentialTargets.Any())
            {
                EntityCache callingDroneTarget = null;
                try
                {
                    callingDroneTarget = _potentialTargets.OrderBy(t => t.Nearest5kDistance).FirstOrDefault();
                }
                catch (NullReferenceException) { }

                if (callingDroneTarget != null && callingDroneTarget.IsReadyToShoot)
                {
                    if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "if (callingDroneTarget != null && !callingDroneTarget.IsIgnored)", Logging.Debug);
                    if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "callingDroneTarget is [" + callingDroneTarget.Name + "][" + Math.Round(callingDroneTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(callingDroneTarget.Id) + "] GroupID [" + callingDroneTarget.GroupId + "]", Logging.Debug);
                    AddDronePriorityTarget(callingDroneTarget, DronePriority.PriorityKillTarget, " GetBestDroneTarget: callingDroneTarget");
                    Cache.Instance.PreferredDroneTarget = callingDroneTarget;
                    Cache.Instance.LastPreferredDroneTargetDateTime = DateTime.UtcNow;
                    return true;
                }

                //return false; //do not return here, continue to process targets, we did not find one yet
            }
            #endregion

            #region Get the closest Low Value Target

            if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "Checking Closest Low Value", Logging.Teal);
            EntityCache lowValueTarget = null;

            if (PotentialCombatTargets.Any())
            {
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "get closest: if (potentialCombatTargets.Any())", Logging.Teal);

                lowValueTarget = PotentialCombatTargets.Where(t => t.IsLowValueTarget && t.IsReadyToShoot)
                    .OrderBy(t => t.IsEwarTarget)
                    .ThenByDescending(t => t.IsNPCFrigate)
                    .ThenByDescending(t => t.IsTargetedBy)
                    .ThenBy(OrderByLowestHealth())
                    .ThenBy(t => t.Nearest5kDistance)
                    .FirstOrDefault();
            }
            #endregion

            #region Get the closest high value target
            //
            // Get the closest low value target //excluding things going too fast for guns to hit (if you have guns fitted)
            //
            if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "Checking closest Low Value", Logging.Teal);
            EntityCache highValueTarget = null;
            if (PotentialCombatTargets.Any())
            {
                highValueTarget = PotentialCombatTargets.Where(t => t.IsHighValueTarget && t.IsReadyToShoot)
                    .OrderByDescending(t => !t.IsNPCFrigate)
                    .ThenByDescending(t => t.IsTargetedBy)
                    .ThenBy(OrderByLowestHealth())
                    .ThenBy(t => t.Nearest5kDistance)
                    .FirstOrDefault();
            }
            #endregion

            #region prefer to grab a lowvaluetarget, if none avail use a high value target
            if (lowValueTarget != null || highValueTarget != null)
            {
                if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log(callingroutine + " GetBestDroneTarget:", "Checking use High Value", Logging.Teal);
                if (Settings.Instance.DebugGetBestDroneTarget)
                {
                    if (highValueTarget != null)
                    {
                        Logging.Log(callingroutine + " GetBestDroneTarget:", "highValueTarget is [" + highValueTarget.Name + "][" + Math.Round(highValueTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(highValueTarget.Id) + "] GroupID [" + highValueTarget.GroupId + "]", Logging.Debug);
                    }
                    else
                    {
                        Logging.Log(callingroutine + " GetBestDroneTarget:", "highValueTarget is [ null ]", Logging.Debug);
                    }
                }
                Cache.Instance.PreferredDroneTarget = lowValueTarget ?? highValueTarget ?? null;
                Cache.Instance.LastPreferredDroneTargetDateTime = DateTime.UtcNow;
                return true;
            }
            #endregion

            if (Settings.Instance.DebugGetBestDroneTarget) Logging.Log("GetBestDroneTarget: none", "Could not determine a suitable Drone target", Logging.Debug);
            #region If we did not find anything at all (wtf!?!?)
            if (Settings.Instance.DebugGetBestDroneTarget)
            {
                if (Cache.Instance.Targets.Any())
                {
                    Logging.Log("GetBestDroneTarget (Drones): none", ".", Logging.Debug);
                    Logging.Log("GetBestDroneTarget (Drones): none", "*** ALL LOCKED/LOCKING TARGETS LISTED BELOW", Logging.Debug);
                    int LockedTargetNumber = 0;
                    foreach (EntityCache __target in Targets)
                    {
                        LockedTargetNumber++;
                        Logging.Log("GetBestDroneTarget (Drones): none", "*** Target: [" + LockedTargetNumber + "][" + __target.Name + "][" + Math.Round(__target.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(__target.Id) + "][isTarget: " + __target.IsTarget + "][isTargeting: " + __target.IsTargeting + "] GroupID [" + __target.GroupId + "]", Logging.Debug);
                    }
                    Logging.Log("GetBestDroneTarget (Drones): none", "*** ALL LOCKED/LOCKING TARGETS LISTED ABOVE", Logging.Debug);
                    Logging.Log("GetBestDroneTarget (Drones): none", ".", Logging.Debug);
                }

                if (Cache.Instance.PotentialCombatTargets.Any(t => !t.IsTarget && !t.IsTargeting))
                {
                    if (Cache.Instance.IgnoreTargets.Any())
                    {
                        int IgnoreCount = Cache.Instance.IgnoreTargets.Count;
                        Logging.Log("GetBestDroneTarget (Drones): none", "Ignore List has [" + IgnoreCount + "] Entities in it.", Logging.Debug);
                    }

                    Logging.Log("GetBestDroneTarget (Drones): none", "***** ALL [" + Cache.Instance.PotentialCombatTargets.Count() + "] potentialCombatTargets LISTED BELOW (not yet targeted or targeting)", Logging.Debug);
                    int potentialCombatTargetNumber = 0;
                    foreach (EntityCache potentialCombatTarget in Cache.Instance.PotentialCombatTargets)
                    {
                        potentialCombatTargetNumber++;
                        Logging.Log("GetBestDroneTarget (Drones): none", "***** Unlocked [" + potentialCombatTargetNumber + "]: [" + potentialCombatTarget.Name + "][" + Math.Round(potentialCombatTarget.Distance / 1000, 2) + "k][" + Cache.Instance.MaskedID(potentialCombatTarget.Id) + "][isTarget: " + potentialCombatTarget.IsTarget + "] GroupID [" + potentialCombatTarget.GroupId + "]", Logging.Debug);
                    }
                    Logging.Log("GetBestDroneTarget (Drones): none", "***** ALL [" + Cache.Instance.PotentialCombatTargets.Count() + "] potentialCombatTargets LISTED ABOVE (not yet targeted or targeting)", Logging.Debug);
                    Logging.Log("GetBestDroneTarget (Drones): none", ".", Logging.Debug);
                }
            }
            #endregion

            NextGetBestDroneTarget = DateTime.UtcNow;
            return false;
        }

        public int RandomNumber(int min, int max)
        {
            Random random = new Random();
            return random.Next(min, max);
        }

        public bool DebugInventoryWindows(string module)
        {
            List<DirectWindow> windows = Cache.Instance.Windows;

            Logging.Log(module, "DebugInventoryWindows: *** Start Listing Inventory Windows ***", Logging.White);
            int windownumber = 0;
            foreach (DirectWindow window in windows)
            {
                if (window.Type.ToLower().Contains("inventory"))
                {
                    windownumber++;
                    Logging.Log(module, "----------------------------  #[" + windownumber + "]", Logging.White);
                    Logging.Log(module, "DebugInventoryWindows.Name:    [" + window.Name + "]", Logging.White);
                    Logging.Log(module, "DebugInventoryWindows.Type:    [" + window.Type + "]", Logging.White);
                    Logging.Log(module, "DebugInventoryWindows.Caption: [" + window.Caption + "]", Logging.White);
                }
            }
            Logging.Log(module, "DebugInventoryWindows: ***  End Listing Inventory Windows  ***", Logging.White);
            return true;
        }

        public DirectContainer _itemHangar { get; set; }

        public DirectContainer ItemHangar
        {
            get
            {
                if (!Cache.Instance.InSpace && Cache.Instance.InStation)
                {
                    if (Cache.Instance._itemHangar == null)
                    {
                        Cache.Instance._itemHangar = Cache.Instance.DirectEve.GetItemHangar();
                        return Cache.Instance._itemHangar;
                    }

                    return Cache.Instance._itemHangar;
                }

                return null;
            }

            set { _itemHangar = value; }
        }

        public bool ReadyItemsHangarSingleInstance(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                DirectContainerWindow lootHangarWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.StationItems") && w.Caption.Contains("Item hangar"));

                // Is the items hangar open?
                if (lootHangarWindow == null)
                {
                    // No, command it to open
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenHangarFloor);
                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 5));
                    Logging.Log(module, "Opening Item Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    return false;
                }

                Cache.Instance.ItemHangar = Cache.Instance.DirectEve.GetContainer(lootHangarWindow.currInvIdItem);
                return true;
            }

            return false;
        }

        public bool CloseItemsHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenItemsHangar", "We are in Station", Logging.Teal);
                    Cache.Instance.ItemHangar = Cache.Instance.DirectEve.GetItemHangar();

                    if (Cache.Instance.ItemHangar == null)
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("OpenItemsHangar", "ItemsHangar was null", Logging.Teal);
                        return false;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("OpenItemsHangar", "ItemsHangar exists", Logging.Teal);

                    // Is the items hangar open?
                    if (Cache.Instance.ItemHangar.Window == null)
                    {
                        Logging.Log(module, "Item Hangar: is closed", Logging.White);
                        return true;
                    }

                    if (!Cache.Instance.ItemHangar.Window.IsReady)
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("OpenItemsHangar", "ItemsHangar.window is not yet ready", Logging.Teal);
                        return false;
                    }

                    if (Cache.Instance.ItemHangar.Window.IsReady)
                    {
                        Cache.Instance.ItemHangar.Window.Close();
                        return false;
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("CloseItemsHangar", "Unable to complete CloseItemsHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool ReadyItemsHangarAsLootHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (Settings.Instance.DebugItemHangar) Logging.Log("ReadyItemsHangarAsLootHangar", "We are in Station", Logging.Teal);
                    Cache.Instance.LootHangar = Cache.Instance.ItemHangar;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyItemsHangarAsLootHangar", "Unable to complete ReadyItemsHangarAsLootHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool ReadyItemsHangarAsAmmoHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                if (Settings.Instance.DebugHangars) Logging.Log("ReadyItemsHangarAsAmmoHangar", "if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Teal);
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("ReadyItemsHangarAsAmmoHangar", "if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)", Logging.Teal);
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("ReadyItemsHangarAsAmmoHangar", "We are in Station", Logging.Teal);
                    Cache.Instance.AmmoHangar = Cache.Instance.ItemHangar;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyItemsHangarAsAmmoHangar", "unable to complete ReadyItemsHangarAsAmmoHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackItemsHangarAsLootHangar(string module)
        {
            if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackItemHangar).TotalMinutes < 10 || DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalMinutes < 10)
            {
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Settings.Instance.DebugItemHangar) Logging.Log("StackItemsHangarAsLootHangar", "public bool StackItemsHangarAsLootHangar(String module)", Logging.Teal);

                if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalSeconds < 30)
                {
                    if (Settings.Instance.DebugItemHangar) Logging.Log("StackItemsHangarAsLootHangar", "if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalSeconds < 15)]", Logging.Teal);

                    if (!Cache.Instance.DirectEve.GetLockedItems().Any())
                    {
                        if (Settings.Instance.DebugItemHangar) Logging.Log("StackItemsHangarAsLootHangar", "if (!Cache.Instance.DirectEve.GetLockedItems().Any())", Logging.Teal);
                        return true;
                    }

                    if (Settings.Instance.DebugItemHangar) Logging.Log("StackItemsHangarAsLootHangar", "GetLockedItems(2) [" + Cache.Instance.DirectEve.GetLockedItems().Count() + "]", Logging.Teal);

                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalSeconds > 20)
                    {
                        Logging.Log(module, "Stacking Corp Loot Hangar timed out, clearing item locks", Logging.Orange);
                        Cache.Instance.DirectEve.UnlockItems();
                        Cache.Instance.LastStackLootHangar = DateTime.UtcNow.AddSeconds(-60);
                        return false;
                    }

                    if (Settings.Instance.DebugItemHangar) Logging.Log("StackItemsHangarAsLootHangar", "return false", Logging.Teal);
                    return false;
                }

                if (Cache.Instance.InStation)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsLootHangar", "if (Cache.Instance.InStation)", Logging.Teal);
                    if (Cache.Instance.LootHangar != null)
                    {
                        try
                        {
                            if (Cache.Instance.StackLootHangarAttempts <= 2)
                            {
                                Cache.Instance.StackLootHangarAttempts++;
                                Logging.Log(module, "Stacking Item Hangar", Logging.White);
                                Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(5);
                                Cache.Instance.LootHangar.StackAll();
                                Cache.Instance.StackLootHangarAttempts = 0; //this resets the counter every time the above stackall completes without an exception
                                Cache.Instance.LastStackLootHangar = DateTime.UtcNow;
                                Cache.Instance.LastStackItemHangar = DateTime.UtcNow;
                                return true;
                            }

                            Logging.Log(module, "Not Stacking LootHangar", Logging.White);
                            return true;
                        }
                        catch (Exception exception)
                        {
                            Logging.Log(module,"Stacking Item Hangar failed ["  + exception +  "]",Logging.Teal);
                            return true;
                        }
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsLootHangar", "if (!Cache.Instance.ReadyItemsHangarAsLootHangar(Cache.StackItemsHangar)) return false;", Logging.Teal);
                    if (!Cache.Instance.ReadyItemsHangarAsLootHangar("Cache.StackItemsHangar")) return false;
                    return false;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackItemsHangarAsLootHangar", "Unable to complete StackItemsHangarAsLootHangar [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public bool StackItemsHangarAsAmmoHangar(string module)
        {
            //Logging.Log("StackItemsHangarAsAmmoHangar", "test", Logging.Teal);

            if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackItemHangar).TotalMinutes < 10 || DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalMinutes < 10)
            {
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Teal);
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)", Logging.Teal);
                return false;
            }

            try
            {
                if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalSeconds < 30)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalSeconds < 15)]", Logging.Teal);

                    if (!Cache.Instance.DirectEve.GetLockedItems().Any())
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "if (!Cache.Instance.DirectEve.GetLockedItems().Any())", Logging.Teal);
                        return true;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "GetLockedItems(2) [" + Cache.Instance.DirectEve.GetLockedItems().Count() + "]", Logging.Teal);

                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalSeconds > 20)
                    {
                        Logging.Log(module, "Stacking Corp Ammo Hangar timed out, clearing item locks", Logging.Orange);
                        Cache.Instance.DirectEve.UnlockItems();
                        Cache.Instance.LastStackAmmoHangar = DateTime.UtcNow.AddSeconds(-60);
                        return false;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "return false", Logging.Teal);
                    return false;
                }

                if (Cache.Instance.InStation)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "if (Cache.Instance.InStation)", Logging.Teal);
                    if (Cache.Instance.AmmoHangar != null)
                    {
                        try
                        {
                            if (Cache.Instance.StackAmmoHangarAttempts <= 2)
                            {
                                Cache.Instance.StackAmmoHangarAttempts++;
                                Logging.Log(module, "Stacking Item Hangar", Logging.White);
                                Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(5);
                                Cache.Instance.AmmoHangar.StackAll();
                                Cache.Instance.StackAmmoHangarAttempts = 0; //this resets the counter every time the above stackall completes without an exception
                                Cache.Instance.LastStackAmmoHangar = DateTime.UtcNow;
                                Cache.Instance.LastStackItemHangar = DateTime.UtcNow;
                                return true;
                            }

                            Logging.Log(module, "Not Stacking AmmoHangar[" + "ItemHangar" + "]", Logging.White);
                            return true;
                        }
                        catch (Exception exception)
                        {
                            Logging.Log(module, "Stacking Item Hangar failed [" + exception + "]", Logging.Teal);
                            return true;
                        }
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackItemsHangarAsAmmoHangar", "if (!Cache.Instance.ReadyItemsHangarAsAmmoHangar(Cache.StackItemsHangar)) return false;", Logging.Teal);
                    if (!Cache.Instance.ReadyItemsHangarAsAmmoHangar("Cache.StackItemsHangar")) return false;
                    return false;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackItemsHangarAsAmmoHangar", "Unable to complete StackItemsHangarAsAmmoHangar [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public bool StackCargoHold(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return false;

            if (DateTime.UtcNow < Cache.Instance.LastStackCargohold.AddSeconds(90))
                return true;

            try
            {
                if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackCargohold).TotalSeconds < 25)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("StackCargoHold", "if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackCargohold).TotalSeconds < 15)", Logging.Debug);

                    if (!Cache.Instance.DirectEve.GetLockedItems().Any())
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackCargoHold", "if (!Cache.Instance.DirectEve.GetLockedItems().Any())", Logging.Debug);
                        return true;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackCargoHold", "GetLockedItems(2) [" + Cache.Instance.DirectEve.GetLockedItems().Count() + "]", Logging.Teal);

                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackCargohold).TotalSeconds > 20)
                    {
                        Logging.Log(module, "Stacking CargoHold timed out, clearing item locks", Logging.Orange);
                        Cache.Instance.DirectEve.UnlockItems();
                        Cache.Instance.LastStackAmmoHangar = DateTime.UtcNow.AddSeconds(-60);
                        return false;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackCargoHold", "return false", Logging.Teal);
                    return false;
                }

                Logging.Log(module, "Stacking CargoHold: waiting [" + Math.Round(Cache.Instance.NextOpenCargoAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                if (Cache.Instance.CurrentShipsCargo != null)
                {
                    try
                    {
                        Cache.Instance.LastStackCargohold = DateTime.UtcNow;
                        Cache.Instance.CurrentShipsCargo.StackAll();
                        return true;
                    }
                    catch (Exception exception)
                    {
                        Logging.Log(module, "Stacking Item Hangar failed [" + exception + "]", Logging.Teal);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackCargoHold", "Unable to complete StackCargoHold [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public bool CloseCargoHold(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return false;

            try
            {
                if (DateTime.UtcNow < Cache.Instance.NextOpenCargoAction)
                {
                    if (DateTime.UtcNow.Subtract(Cache.Instance.NextOpenCargoAction).TotalSeconds > 0)
                    {
                        Logging.Log(module, "Opening CargoHold: waiting [" + Math.Round(Cache.Instance.NextOpenCargoAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    }
                    return false;
                }

                if (Cache.Instance.InStation || Cache.Instance.InSpace) //do we need to special case pods here?
                {
                    if (Cache.Instance.CurrentShipsCargo.Window == null)
                    {
                        Logging.Log(module, "Cargohold is closed", Logging.White);
                        return true;
                    }

                    if (!Cache.Instance.CurrentShipsCargo.Window.IsReady)
                    {
                        //Logging.Log(module, "cargo window is not ready", Logging.White);
                        return false;
                    }

                    if (Cache.Instance.CurrentShipsCargo.Window.IsReady)
                    {
                        Cache.Instance.CurrentShipsCargo.Window.Close();
                        Cache.Instance.NextOpenCargoAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(1, 2));
                        return true;
                    }
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("CloseCargoHold", "Unable to complete CloseCargoHold [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public DirectContainer ShipHangar { get; set; }

        public bool OpenShipsHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Teal);
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "if (DateTime.UtcNow [" + DateTime.UtcNow + "] < Cache.Instance.NextOpenHangarAction [" + Cache.Instance.NextOpenHangarAction + "])", Logging.Teal);
                return false;
            }

            if (Cache.Instance.InStation)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "if (Cache.Instance.InStation)", Logging.Teal);

                Cache.Instance.ShipHangar = Cache.Instance.DirectEve.GetShipHangar();
                if (Cache.Instance.ShipHangar == null)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "if (Cache.Instance.ShipHangar == null)", Logging.Teal);
                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddMilliseconds(500);
                    return false;
                }

                // Is the ship hangar open?
                if (Cache.Instance.ShipHangar.Window == null)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "if (Cache.Instance.ShipHangar.Window == null)", Logging.Teal);
                    // No, command it to open
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenShipHangar);
                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                    Logging.Log(module, "Opening Ship Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    return false;
                }

                if (!Cache.Instance.ShipHangar.Window.IsReady)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "if (!Cache.Instance.ShipHangar.Window.IsReady)", Logging.Teal);
                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddMilliseconds(500);
                    return false;
                }

                if (Cache.Instance.ShipHangar.Window.IsReady)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "if (Cache.Instance.ShipHangar.Window.IsReady)", Logging.Teal);
                    return true;
                }
            }
            return false;
        }

        public bool StackShipsHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return false;

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
                return false;

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (Cache.Instance.ShipHangar != null && Cache.Instance.ShipHangar.IsValid)
                    {
                        Logging.Log(module, "Stacking Ship Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                        Cache.Instance.LastStackShipsHangar = DateTime.UtcNow;
                        Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 5));
                        Cache.Instance.ShipHangar.StackAll();
                        return true;
                    }
                    Logging.Log(module, "Stacking Ship Hangar: not yet ready: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    return false;
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackShipsHangar", "Unable to complete StackShipsHangar [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public bool CloseShipsHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return false;

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
                return false;

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "We are in Station", Logging.Teal);
                    Cache.Instance.ShipHangar = Cache.Instance.DirectEve.GetShipHangar();

                    if (Cache.Instance.ShipHangar == null)
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "ShipsHangar was null", Logging.Teal);
                        return false;
                    }
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "ShipsHangar exists", Logging.Teal);

                    // Is the items hangar open?
                    if (Cache.Instance.ShipHangar.Window == null)
                    {
                        Logging.Log(module, "Ship Hangar: is closed", Logging.White);
                        return true;
                    }

                    if (!Cache.Instance.ShipHangar.Window.IsReady)
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("OpenShipsHangar", "ShipsHangar.window is not yet ready", Logging.Teal);
                        return false;
                    }

                    if (Cache.Instance.ShipHangar.Window.IsReady)
                    {
                        Cache.Instance.ShipHangar.Window.Close();
                        return false;
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("CloseShipsHangar", "Unable to complete CloseShipsHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        //public DirectContainer CorpAmmoHangar { get; set; }

        public bool GetCorpAmmoHangarID()
        {
            try
            {
                if (Cache.Instance.InStation && DateTime.UtcNow > LastSessionChange.AddSeconds(10))
                {
                    string CorpHangarName;
                    if (Settings.Instance.AmmoHangarTabName != null)
                    {
                        CorpHangarName = Settings.Instance.AmmoHangarTabName;
                        if (Settings.Instance.DebugHangars) Logging.Log("GetCorpAmmoHangarID", "CorpHangarName we are looking for is [" + CorpHangarName + "][ AmmoHangarID was: " + Cache.Instance.AmmoHangarID + "]", Logging.White);
                    }
                    else
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("GetCorpAmmoHangarID", "AmmoHangar not configured: Questor will default to item hangar", Logging.White);
                        return true;
                    }

                    if (CorpHangarName != string.Empty) //&& Cache.Instance.AmmoHangarID == -99)
                    {
                        Cache.Instance.AmmoHangarID = -99;
                        Cache.Instance.AmmoHangarID = Cache.Instance.DirectEve.GetCorpHangarId(Settings.Instance.AmmoHangarTabName); //- 1;
                        if (Settings.Instance.DebugHangars) Logging.Log("GetCorpAmmoHangarID", "AmmoHangarID is [" + Cache.Instance.AmmoHangarID + "]", Logging.Teal);
                        
                        Cache.Instance.AmmoHangar = null;
                        Cache.Instance.AmmoHangar = Cache.Instance.DirectEve.GetCorporationHangar((int)Cache.Instance.AmmoHangarID);
                        if (Cache.Instance.AmmoHangar.IsValid)
                        {
                            if (Settings.Instance.DebugHangars) Logging.Log("GetCorpAmmoHangarID", "AmmoHangar contains [" + Cache.Instance.AmmoHangar.Items.Count() + "] Items", Logging.White);

                            //if (Settings.Instance.DebugHangars) Logging.Log("GetCorpAmmoHangarID", "AmmoHangar Description [" + Cache.Instance.AmmoHangar.Description + "]", Logging.White);
                            //if (Settings.Instance.DebugHangars) Logging.Log("GetCorpAmmoHangarID", "AmmoHangar UsedCapacity [" + Cache.Instance.AmmoHangar.UsedCapacity + "]", Logging.White);
                            //if (Settings.Instance.DebugHangars) Logging.Log("GetCorpAmmoHangarID", "AmmoHangar Volume [" + Cache.Instance.AmmoHangar.Volume + "]", Logging.White);
                        }

                        return true;
                    }
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("GetCorpAmmoHangarID", "Unable to complete GetCorpAmmoHangarID [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool GetCorpLootHangarID()
        {
            try
            {
                if (Cache.Instance.InStation && DateTime.UtcNow > LastSessionChange.AddSeconds(10))
                {
                    string CorpHangarName;
                    if (Settings.Instance.LootHangarTabName != null)
                    {
                        CorpHangarName = Settings.Instance.LootHangarTabName;
                        if (Settings.Instance.DebugHangars) Logging.Log("GetCorpLootHangarID", "CorpHangarName we are looking for is [" + CorpHangarName + "][ LootHangarID was: " + Cache.Instance.LootHangarID + "]", Logging.White);
                    }
                    else
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("GetCorpLootHangarID", "LootHangar not configured: Questor will default to item hangar", Logging.White);
                        return true;
                    }

                    if (CorpHangarName != string.Empty) //&& Cache.Instance.LootHangarID == -99)
                    {
                        Cache.Instance.LootHangarID = -99;
                        Cache.Instance.LootHangarID = Cache.Instance.DirectEve.GetCorpHangarId(Settings.Instance.LootHangarTabName);  //- 1;
                        if (Settings.Instance.DebugHangars) Logging.Log("GetCorpLootHangarID", "LootHangarID is [" + Cache.Instance.LootHangarID + "]", Logging.Teal);

                        Cache.Instance.LootHangar = null;
                        Cache.Instance.LootHangar = Cache.Instance.DirectEve.GetCorporationHangar((int)Cache.Instance.LootHangarID);
                        if (Cache.Instance.LootHangar.IsValid)
                        {
                            if (Settings.Instance.DebugHangars) Logging.Log("GetCorpLootHangarID", "LootHangar contains [" + Cache.Instance.LootHangar.Items.Count() + "] Items", Logging.White);

                            //if (Settings.Instance.DebugHangars) Logging.Log("GetCorpLootHangarID", "LootHangar Description [" + Cache.Instance.LootHangar.Description + "]", Logging.White);
                            //if (Settings.Instance.DebugHangars) Logging.Log("GetCorpLootHangarID", "LootHangar UsedCapacity [" + Cache.Instance.LootHangar.UsedCapacity + "]", Logging.White);
                            //if (Settings.Instance.DebugHangars) Logging.Log("GetCorpLootHangarID", "LootHangar Volume [" + Cache.Instance.LootHangar.Volume + "]", Logging.White);
                        }

                        return true;
                    }
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("GetCorpLootHangarID", "Unable to complete GetCorpLootHangarID [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackCorpAmmoHangar(string module)
        {
            if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalMinutes < 10)
            {
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackCorpAmmoHangar", "LastStackAmmoHangar: [" + Cache.Instance.LastStackAmmoHangar.AddSeconds(60) + "] DateTime.UtcNow: [" + DateTime.UtcNow + "]", Logging.Teal);

                if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalSeconds < 25)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("StackCorpAmmoHangar", "if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalSeconds < 60)]", Logging.Teal);

                    if (!Cache.Instance.DirectEve.GetLockedItems().Any())
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackCorpAmmoHangar", "if (!Cache.Instance.DirectEve.GetLockedItems().Any())", Logging.Teal);
                        return true;
                    }
                    if (Settings.Instance.DebugHangars) Logging.Log("StackCorpAmmoHangar", "GetLockedItems(2) [" + Cache.Instance.DirectEve.GetLockedItems().Count() + "]", Logging.Teal);

                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalSeconds > 30)
                    {
                        Logging.Log("Arm", "Stacking Corp Ammo Hangar timed out, clearing item locks", Logging.Orange);
                        Cache.Instance.DirectEve.UnlockItems();
                        Cache.Instance.LastStackAmmoHangar = DateTime.UtcNow.AddSeconds(-60);
                        return false;
                    }
                    if (Settings.Instance.DebugHangars) Logging.Log("StackCorpAmmoHangar", "return false", Logging.Teal);
                    return false;
                }

                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangarTabName))
                    {
                        if (AmmoHangar != null && AmmoHangar.IsValid)
                        {
                            try
                            {
                                if (Cache.Instance.StackAmmoHangarAttempts <= 2)
                                {
                                    Cache.Instance.StackAmmoHangarAttempts++;
                                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 5));
                                    Logging.Log(module, "Stacking Corporate Ammo Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                                    Cache.Instance.LastStackAmmoHangar = DateTime.UtcNow;
                                    Cache.Instance.AmmoHangar.StackAll();
                                    Cache.Instance.StackAmmoHangarAttempts = 0; //this resets the counter every time the above stackall completes without an exception
                                    return true;
                                }

                                Logging.Log(module, "Not Stacking AmmoHangar [" + Settings.Instance.AmmoHangarTabName + "]", Logging.White);
                                return true;
                            }
                            catch (Exception exception)
                            {
                                Logging.Log(module, "Stacking AmmoHangar failed [" + exception + "]", Logging.Teal);
                                return true;
                            }
                        }

                        return false;
                    }

                    Cache.Instance.AmmoHangar = null;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackCorpAmmoHangar", "Unable to complete StackCorpAmmoHangar [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        //public DirectContainer CorpLootHangar { get; set; }
        public DirectContainerWindow PrimaryInventoryWindow { get; set; }

        public DirectContainerWindow corpAmmoHangarSecondaryWindow { get; set; }

        public DirectContainerWindow corpLootHangarSecondaryWindow { get; set; }

        public bool OpenInventoryWindow(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            Cache.Instance.PrimaryInventoryWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Name.Contains("Inventory"));

            if (Cache.Instance.PrimaryInventoryWindow == null)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("debug", "Cache.Instance.InventoryWindow is null, opening InventoryWindow", Logging.Teal);

                // No, command it to open
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenInventory);
                Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(2, 3));
                Logging.Log(module, "Opening Inventory Window: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                return false;
            }

            if (Cache.Instance.PrimaryInventoryWindow != null)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("debug", "Cache.Instance.InventoryWindow exists", Logging.Teal);
                if (Cache.Instance.PrimaryInventoryWindow.IsReady)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("debug", "Cache.Instance.InventoryWindow exists and is ready", Logging.Teal);
                    return true;
                }

                //
                // if the InventoryWindow "hangs" and is never ready we will hang... it would be better if we set a timer
                // and closed the inventorywindow that is not ready after 10-20seconds. (can we close a window that is in a state if !window.isready?)
                //
                return false;
            }

            return false;
        }

        public bool StackCorpLootHangar(string module)
        {
            if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalSeconds < 30)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackCorpLootHangar", "if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalSeconds < 30)", Logging.Debug);
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackCorpLootHangar", "if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Debug);
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackCorpLootHangar", "if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)", Logging.Debug);
                return false;
            }

            try
            {
                if (Cache.Instance.LastStackLootHangar.AddSeconds(60) > DateTime.UtcNow)
                {
                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        return true;
                    }

                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalSeconds > 30)
                    {
                        Logging.Log("Arm", "Stacking Corp Loot Hangar timed out, clearing item locks", Logging.Orange);
                        Cache.Instance.DirectEve.UnlockItems();
                        Cache.Instance.LastStackLootHangar = DateTime.UtcNow.AddSeconds(-60);
                        return false;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackCorpLootHangar", "waiting for item locks: if (Cache.Instance.DirectEve.GetLockedItems().Count != 0)", Logging.Debug);
                    return false;
                }

                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangarTabName))
                    {
                        if (LootHangar != null && LootHangar.IsValid)
                        {
                            try
                            {
                                if (Cache.Instance.StackLootHangarAttempts <= 2)
                                {
                                    Cache.Instance.StackLootHangarAttempts++;
                                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 5));
                                    Logging.Log(module, "Stacking Corporate Loot Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                                    Cache.Instance.LastStackLootHangar = DateTime.UtcNow;
                                    Cache.Instance.LastStackLootContainer = DateTime.UtcNow;
                                    Cache.Instance.LootHangar.StackAll();
                                    Cache.Instance.StackLootHangarAttempts = 0; //this resets the counter every time the above stackall completes without an exception
                                    return true;
                                }

                                Logging.Log(module, "Not Stacking AmmoHangar [" + Settings.Instance.AmmoHangarTabName + "]", Logging.White);
                                return true;
                            }
                            catch (Exception exception)
                            {
                                Logging.Log(module, "Stacking LootHangar failed [" + exception + "]", Logging.Teal);
                                return true;
                            }
                        }

                        return false;
                    }

                    Cache.Instance.LootHangar = null;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackCorpLootHangar", "Unable to complete StackCorpLootHangar [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public bool SortCorpLootHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                if (!string.IsNullOrEmpty(Settings.Instance.LootHangarTabName))
                {
                    if (LootHangar != null && LootHangar.IsValid)
                    {
                        Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 5));
                        Logging.Log(module, "Stacking Corporate Loot Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                        Cache.Instance.LootHangar.StackAll();
                        return true;
                    }

                    return false;
                }

                Cache.Instance.LootHangar = null;
                return true;
            }
            return false;
        }

        public DirectContainer CorpBookmarkHangar { get; set; }

        //
        // why do we still have this in here? depreciated in favor of using the corporate bookmark system
        //
        public bool OpenCorpBookmarkHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenCorpBookmarkHangarAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.CorpBookmarkHangar = !string.IsNullOrEmpty(Settings.Instance.BookmarkHangar)
                                      ? Cache.Instance.DirectEve.GetCorporationHangar(Settings.Instance.BookmarkHangar)
                                      : null;

                // Is the corpHangar open?
                if (Cache.Instance.CorpBookmarkHangar != null)
                {
                    if (Cache.Instance.CorpBookmarkHangar.Window == null)
                    {
                        // No, command it to open
                        //Cache.Instance.DirectEve.OpenCorporationHangar();
                        Cache.Instance.NextOpenCorpBookmarkHangarAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                        Logging.Log(module, "Opening Corporate Bookmark Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenCorpBookmarkHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                        return false;
                    }

                    if (!Cache.Instance.CorpBookmarkHangar.Window.IsReady)
                    {
                        return false;
                    }

                    if (Cache.Instance.CorpBookmarkHangar.Window.IsReady)
                    {
                        if (Cache.Instance.CorpBookmarkHangar.Window.IsPrimary())
                        {
                            Cache.Instance.CorpBookmarkHangar.Window.OpenAsSecondary();
                            return false;
                        }

                        return true;
                    }
                }
                if (Cache.Instance.CorpBookmarkHangar == null)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.BookmarkHangar))
                    {
                        Logging.Log(module, "Opening Corporate Bookmark Hangar: failed! No Corporate Hangar in this station! lag?", Logging.Orange);
                    }

                    return false;
                }
            }

            return false;
        }

        public bool CloseCorpHangar(string module, string window)
        {
            try
            {
                if (Cache.Instance.InStation && !string.IsNullOrEmpty(window))
                {
                    DirectContainerWindow corpHangarWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption == window);

                    if (corpHangarWindow != null)
                    {
                        Logging.Log(module, "Closing Corp Window: " + window, Logging.Teal);
                        corpHangarWindow.Close();
                        return false;
                    }

                    return true;
                }

                return true;
            }
            catch (Exception exception)
            {
                Logging.Log("CloseCorpHangar", "Unable to complete CloseCorpHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool ClosePrimaryInventoryWindow(string module)
        {
            if (DateTime.UtcNow < NextOpenHangarAction)
                return false;

            //
            // go through *every* window
            //
            try
            {
                foreach (DirectWindow window in Cache.Instance.Windows)
                {
                    if (window.Type.Contains("form.Inventory"))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log(module, "ClosePrimaryInventoryWindow: Closing Primary Inventory Window Named [" + window.Name + "]", Logging.White);
                        window.Close();
                        NextOpenHangarAction = DateTime.UtcNow.AddMilliseconds(500);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                Logging.Log("ClosePrimaryInventoryWindow", "Unable to complete ClosePrimaryInventoryWindow [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        private DirectContainer _lootContainer;

        public DirectContainer LootContainer
        {
            get
            {
                if (Cache.Instance.InStation)
                {
                    if (_lootContainer == null)
                    {
                        if (!string.IsNullOrEmpty(Settings.Instance.LootContainerName))
                        {
                            //if (Settings.Instance.DebugHangars) Logging.Log("LootContainer", "Debug: if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);

                            DirectItem firstLootContainer = Cache.Instance.LootHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && (i.GroupId == (int)Group.FreightContainer || i.GroupId == (int)Group.AuditLogSecureContainer) && i.GivenName.ToLower() == Settings.Instance.LootContainerName.ToLower());
                            if (firstLootContainer == null && Cache.Instance.LootHangar.Items.Any(i => i.IsSingleton && (i.GroupId == (int)Group.FreightContainer || i.GroupId == (int)Group.AuditLogSecureContainer)))
                            {
                                if (Settings.Instance.DebugHangars) Logging.Log("LootContainer", "Debug: Unable to find a container named [" + Settings.Instance.LootContainerName + "], using the available unnamed container", Logging.Teal);
                                firstLootContainer = Cache.Instance.LootHangar.Items.FirstOrDefault(i => i.IsSingleton && (i.GroupId == (int)Group.FreightContainer || i.GroupId == (int)Group.AuditLogSecureContainer));
                            }

                            if (firstLootContainer != null)
                            {
                                _lootContainer = Cache.Instance.DirectEve.GetContainer(firstLootContainer.ItemId);

                                if (_lootContainer != null && _lootContainer.IsValid)
                                {
                                    //if (Settings.Instance.DebugHangars) Logging.Log("LootContainer", "LootContainer is defined (no window needed)", Logging.DebugHangars);
                                    return _lootContainer;
                                }

                                if (Settings.Instance.DebugHangars) Logging.Log("LootContainer", "LootContainer is still null", Logging.DebugHangars);
                                return null;
                            }

                            Logging.Log("LootContainer", "unable to find LootContainer named [ " + Settings.Instance.LootContainerName.ToLower() + " ]", Logging.Orange);
                            DirectItem firstOtherContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer);

                            if (firstOtherContainer != null)
                            {
                                Logging.Log("LootContainer", "we did however find a container named [ " + firstOtherContainer.GivenName + " ]", Logging.Orange);
                                return null;
                            }

                            return null;
                        }

                        return null;
                    }

                    return _lootContainer;
                }

                return null;
            }
            set
            {
                _lootContainer = value;
            }
        }

        public bool ReadyHighTierLootContainer(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenLootContainerAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                if (!string.IsNullOrEmpty(Settings.Instance.LootContainerName))
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenLootContainer", "Debug: if (!string.IsNullOrEmpty(Settings.Instance.HighTierLootContainer))", Logging.Teal);

                    DirectItem firstLootContainer = Cache.Instance.LootHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer && i.GivenName.ToLower() == Settings.Instance.HighTierLootContainer.ToLower());
                    if (firstLootContainer != null)
                    {
                        long highTierLootContainerID = firstLootContainer.ItemId;
                        Cache.Instance.HighTierLootContainer = Cache.Instance.DirectEve.GetContainer(highTierLootContainerID);

                        if (Cache.Instance.HighTierLootContainer != null && Cache.Instance.HighTierLootContainer.IsValid)
                        {
                            if (Settings.Instance.DebugHangars) Logging.Log(module, "HighTierLootContainer is defined (no window needed)", Logging.DebugHangars);
                            return true;
                        }

                        if (Cache.Instance.HighTierLootContainer == null)
                        {
                            if (!string.IsNullOrEmpty(Settings.Instance.LootHangarTabName))
                                Logging.Log(module, "Opening HighTierLootContainer: failed! lag?", Logging.Orange);
                            return false;
                        }

                        if (Settings.Instance.DebugHangars) Logging.Log(module, "HighTierLootContainer is not yet ready. waiting...", Logging.DebugHangars);
                        return false;
                    }

                    Logging.Log(module, "unable to find HighTierLootContainer named [ " + Settings.Instance.HighTierLootContainer.ToLower() + " ]", Logging.Orange);
                    DirectItem firstOtherContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer);

                    if (firstOtherContainer != null)
                    {
                        Logging.Log(module, "we did however find a container named [ " + firstOtherContainer.GivenName + " ]", Logging.Orange);
                        return false;
                    }

                    return false;
                }

                return true;
            }

            return false;
        }

        public bool StackHighTierLootContainer(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenLootContainerAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                if (!Cache.Instance.ReadyHighTierLootContainer("Cache.StackHighTierLootContainer")) return false;
                Cache.Instance.NextOpenLootContainerAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 5));
                if (HighTierLootContainer.Window == null)
                {
                    DirectItem firstLootContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer && i.GivenName.ToLower() == Settings.Instance.HighTierLootContainer.ToLower());
                    if (firstLootContainer != null)
                    {
                        long highTierLootContainerID = firstLootContainer.ItemId;
                        if (!OpenAndSelectInvItem(module, highTierLootContainerID))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                if (HighTierLootContainer.Window == null || !HighTierLootContainer.Window.IsReady) return false;

                Logging.Log(module, "Loot Container window named: [ " + HighTierLootContainer.Window.Name + " ] was found and its contents are being stacked", Logging.White);
                HighTierLootContainer.StackAll();
                Cache.Instance.LastStackLootContainer = DateTime.UtcNow;
                Cache.Instance.NextOpenLootContainerAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                return true;
            }

            return false;
        }

        public bool OpenAndSelectInvItem(string module, long id)
        {
            if (DateTime.UtcNow < Cache.Instance.LastSessionChange.AddSeconds(10))
            {
                if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Teal);
                return false;
            }

            if (DateTime.UtcNow < NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: if (DateTime.UtcNow < NextOpenHangarAction)", Logging.Teal);
                return false;
            }

            if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: about to: if (!Cache.Instance.OpenInventoryWindow", Logging.Teal);

            if (!Cache.Instance.OpenInventoryWindow(module)) return false;

            Cache.Instance.PrimaryInventoryWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Name.Contains("Inventory"));

            if (Cache.Instance.PrimaryInventoryWindow != null && Cache.Instance.PrimaryInventoryWindow.IsReady)
            {
                if (id < 0)
                {
                    //
                    // this also kicks in if we have no corp hangar at all in station... can we detect that some other way?
                    //
                    Logging.Log("OpenAndSelectInvItem", "Inventory item ID from tree cannot be less than 0, retrying", Logging.White);
                    return false;
                }

                List<long> idsInInvTreeView = Cache.Instance.PrimaryInventoryWindow.GetIdsFromTree(false);
                if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: IDs Found in the Inv Tree [" + idsInInvTreeView.Count() + "]", Logging.Teal);

                foreach (Int64 itemInTree in idsInInvTreeView)
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: itemInTree [" + itemInTree + "][looking for: " + id, Logging.Teal);
                    if (itemInTree == id)
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: Found a match! itemInTree [" + itemInTree + "] = id [" + id + "]", Logging.Teal);
                        if (Cache.Instance.PrimaryInventoryWindow.currInvIdItem != id)
                        {
                            if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: We do not have the right ID selected yet, select it now.", Logging.Teal);
                            Cache.Instance.PrimaryInventoryWindow.SelectTreeEntryByID(id);
                            Cache.Instance.NextOpenCargoAction = DateTime.UtcNow.AddMilliseconds(Cache.Instance.RandomNumber(2000, 4400));
                            return false;
                        }

                        if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: We already have the right ID selected.", Logging.Teal);
                        return true;
                    }

                    continue;
                }

                if (!idsInInvTreeView.Contains(id))
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: if (!Cache.Instance.InventoryWindow.GetIdsFromTree(false).Contains(ID))", Logging.Teal);

                    if (id >= 0 && id <= 6 && Cache.Instance.PrimaryInventoryWindow.ExpandCorpHangarView())
                    {
                        Logging.Log(module, "ExpandCorpHangar executed", Logging.Teal);
                        Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(4);
                        return false;
                    }

                    foreach (Int64 itemInTree in idsInInvTreeView)
                    {
                        Logging.Log(module, "ID: " + itemInTree, Logging.Red);
                    }

                    Logging.Log(module, "Was looking for: " + id, Logging.Red);
                    return false;
                }

                return false;
            }

            return false;
        }

        public bool ListInvTree(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastSessionChange.AddSeconds(10))
            {
                if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Teal);
                return false;
            }

            if (DateTime.UtcNow < NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: if (DateTime.UtcNow < NextOpenHangarAction)", Logging.Teal);
                return false;
            }

            if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: about to: if (!Cache.Instance.OpenInventoryWindow", Logging.Teal);

            if (!Cache.Instance.OpenInventoryWindow(module)) return false;

            Cache.Instance.PrimaryInventoryWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Name.Contains("Inventory"));

            if (Cache.Instance.PrimaryInventoryWindow != null && Cache.Instance.PrimaryInventoryWindow.IsReady)
            {
                List<long> idsInInvTreeView = Cache.Instance.PrimaryInventoryWindow.GetIdsFromTree(false);
                if (Settings.Instance.DebugHangars) Logging.Log("OpenAndSelectInvItem", "Debug: IDs Found in the Inv Tree [" + idsInInvTreeView.Count() + "]", Logging.Teal);

                if (Cache.Instance.PrimaryInventoryWindow.ExpandCorpHangarView())
                {
                    Logging.Log(module, "ExpandCorpHangar executed", Logging.Teal);
                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(4);
                    return false;
                }

                foreach (Int64 itemInTree in idsInInvTreeView)
                {
                    Logging.Log(module, "ID: " + itemInTree, Logging.Red);
                }
                return false;
            }

            return false;
        }

        public bool StackLootContainer(string module)
        {
            if (DateTime.UtcNow.AddMinutes(10) < Cache.Instance.LastStackLootContainer)
            {
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenLootContainerAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                if (LootContainer.Window == null)
                {
                    DirectItem firstLootContainer = Cache.Instance.LootHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer && i.GivenName.ToLower() == Settings.Instance.LootContainerName.ToLower());
                    if (firstLootContainer != null)
                    {
                        long lootContainerID = firstLootContainer.ItemId;
                        if (!OpenAndSelectInvItem(module, lootContainerID))
                            return false;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (LootContainer.Window == null || !LootContainer.Window.IsReady) return false;

                Logging.Log(module, "Loot Container window named: [ " + LootContainer.Window.Name + " ] was found and its contents are being stacked", Logging.White);
                LootContainer.StackAll();
                Cache.Instance.LastStackLootContainer = DateTime.UtcNow;
                Cache.Instance.LastStackLootHangar = DateTime.UtcNow;
                Cache.Instance.NextOpenLootContainerAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                return true;
            }

            return false;
        }

        public bool CloseLootContainer(string module)
        {
            if (!string.IsNullOrEmpty(Settings.Instance.LootContainerName))
            {
                if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpLootHangar", "Debug: else if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                DirectContainerWindow lootHangarWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Caption == Settings.Instance.LootContainerName);

                if (lootHangarWindow != null)
                {
                    lootHangarWindow.Close();
                    return false;
                }

                return true;
            }

            return true;
        }

        public DirectContainerWindow OreHoldWindow { get; set; }

        public bool OpenOreHold(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction) return false;

            if (!Cache.Instance.OpenInventoryWindow("OpenOreHold")) return false;

            //
            // does the current ship have an ore hold?
            //
            Cache.Instance.OreHoldWindow = Cache.Instance.PrimaryInventoryWindow;

            if (Cache.Instance.OreHoldWindow == null)
            {
                // No, command it to open
                Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                Logging.Log(module, "Opening Ore Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                long OreHoldID = 1;  //no idea how to get this value atm. this is not yet correct.
                if (!Cache.Instance.PrimaryInventoryWindow.SelectTreeEntry("Ore Hold", OreHoldID - 1))
                {
                    if (!Cache.Instance.PrimaryInventoryWindow.ExpandCorpHangarView())
                    {
                        Logging.Log(module, "Failed to expand corp hangar tree", Logging.Red);
                        return false;
                    }
                }
                return false;
            }
            if (!Cache.Instance.OreHoldWindow.IsReady)
                return false;

            return false;
        }

        public DirectContainer _lootHangar;

        public DirectContainer LootHangar
        {
            get
            {
                try
                {
                    if (Cache.Instance.InStation)
                    {
                        if (_lootHangar == null)
                        {
                            if (Settings.Instance.LootHangarTabName != string.Empty)
                            {
                                Cache.Instance.LootHangarID = -99;
                                Cache.Instance.LootHangarID = Cache.Instance.DirectEve.GetCorpHangarId(Settings.Instance.LootHangarTabName); //- 1;
                                if (Settings.Instance.DebugHangars) Logging.Log("LootHangar: GetCorpLootHangarID", "LootHangarID is [" + Cache.Instance.LootHangarID + "]", Logging.Teal);

                                _lootHangar = null;
                                _lootHangar = Cache.Instance.DirectEve.GetCorporationHangar((int)Cache.Instance.LootHangarID);

                                if (_lootHangar != null && _lootHangar.IsValid) //do we have a corp hangar tab setup with that name?
                                {
                                    if (Settings.Instance.DebugHangars)
                                    {
                                        Logging.Log("LootHangar", "LootHangar is defined (no window needed)", Logging.DebugHangars);
                                        try
                                        {
                                            if (_lootHangar.Items.Any())
                                            {
                                                int LootHangarItemCount = _lootHangar.Items.Count();
                                                if (Settings.Instance.DebugHangars) Logging.Log("LootHangar", "LootHangar [" + Settings.Instance.LootHangarTabName + "] has [" + LootHangarItemCount + "] items", Logging.DebugHangars);
                                            }
                                        }
                                        catch (Exception exception)
                                        {
                                            Logging.Log("ReadyCorpLootHangar", "Exception [" + exception + "]", Logging.Debug);
                                        }
                                    }

                                    return _lootHangar;
                                }

                                Logging.Log("LootHangar", "Opening Corporate LootHangar: failed! No Corporate Hangar in this station! lag?", Logging.Orange);
                                return Cache.Instance.ItemHangar;

                            }

                            //if (Settings.Instance.DebugHangars) Logging.Log("LootHangar", "LootHangar is not defined", Logging.DebugHangars);
                            _lootHangar = null;
                            return Cache.Instance.ItemHangar;
                        }

                        return _lootHangar;
                    }

                    return null;
                }
                catch (Exception exception)
                {
                    Logging.Log("LootHangar", "Unable to define LootHangar [" + exception + "]", Logging.Teal);
                    return null;
                }     
            }
            set
            {
                _lootHangar = value;
            }
        }

        public DirectContainer HighTierLootContainer { get; set; }

        public bool CloseLootHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangarTabName))
                    {
                        Cache.Instance.LootHangar = Cache.Instance.DirectEve.GetCorporationHangar(Settings.Instance.LootHangarTabName);

                        // Is the corp loot Hangar open?
                        if (Cache.Instance.LootHangar != null)
                        {
                            Cache.Instance.corpLootHangarSecondaryWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption.Contains(Settings.Instance.LootHangarTabName));
                            if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpLootHangar", "Debug: if (Cache.Instance.LootHangar != null)", Logging.Teal);

                            if (Cache.Instance.corpLootHangarSecondaryWindow != null)
                            {
                                // if open command it to close
                                Cache.Instance.corpLootHangarSecondaryWindow.Close();
                                Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                                Logging.Log(module, "Closing Corporate Loot Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                                return false;
                            }

                            return true;
                        }

                        if (Cache.Instance.LootHangar == null)
                        {
                            if (!string.IsNullOrEmpty(Settings.Instance.LootHangarTabName))
                            {
                                Logging.Log(module, "Closing Corporate Hangar: failed! No Corporate Hangar in this station! lag or setting misconfiguration?", Logging.Orange);
                                return true;
                            }
                            return false;
                        }
                    }
                    else if (!string.IsNullOrEmpty(Settings.Instance.LootContainerName))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpLootHangar", "Debug: else if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                        DirectContainerWindow lootHangarWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption.Contains(Settings.Instance.LootContainerName));

                        if (lootHangarWindow != null)
                        {
                            lootHangarWindow.Close();
                            return false;
                        }
                        return true;
                    }
                    else //use local items hangar
                    {
                        Cache.Instance.LootHangar = Cache.Instance.DirectEve.GetItemHangar();
                        if (Cache.Instance.LootHangar == null)
                            return false;

                        // Is the items hangar open?
                        if (Cache.Instance.LootHangar.Window != null)
                        {
                            // if open command it to close
                            Cache.Instance.LootHangar.Window.Close();
                            Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 4));
                            Logging.Log(module, "Closing Item Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                            return false;
                        }

                        return true;
                    }
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("CloseLootHangar", "Unable to complete CloseLootHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackLootHangar(string module)
        {
            StackLoothangarAttempts++;
            if (StackLoothangarAttempts > 10)
            {
                Logging.Log("StackLootHangar", "Stacking the lootHangar has failed too many times [" + StackLoothangarAttempts + "]", Logging.Teal);
                if (StackLoothangarAttempts > 30)
                {
                    Logging.Log("StackLootHangar", "Stacking the lootHangar routine has run [" + StackLoothangarAttempts + "] times without success, resetting counter", Logging.Teal);
                    StackLoothangarAttempts = 0;
                }

                return true;
            }

            if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalMinutes < 10)
            {
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Teal);
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "if (DateTime.UtcNow [" + DateTime.UtcNow + "] < Cache.Instance.NextOpenHangarAction [" + Cache.Instance.NextOpenHangarAction + "])", Logging.Teal);
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangarTabName))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "Starting [Cache.Instance.StackCorpLootHangar]", Logging.Teal);
                        if (!Cache.Instance.StackCorpLootHangar("Cache.StackCorpLootHangar")) return false;
                        if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "Finished [Cache.Instance.StackCorpLootHangar]", Logging.Teal);
                        StackLoothangarAttempts = 0;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(Settings.Instance.LootContainerName))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                        //if (!Cache.Instance.StackLootContainer("Cache.StackLootContainer")) return false;
                        Logging.Log("StackLootHangar", "We do not stack containers, you will need to do so manually. StackAll does not seem to work with Primary Inventory windows.", Logging.Teal);
                        StackLoothangarAttempts = 0; 
                        return true;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "!Cache.Instance.StackItemsHangarAsLootHangar(Cache.StackLootHangar))", Logging.Teal);
                    if (!Cache.Instance.StackItemsHangarAsLootHangar("Cache.StackItemsHangarAsLootHangar")) return false;
                    StackLoothangarAttempts = 0;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackLootHangar", "Unable to complete StackLootHangar [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public bool SortLootHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                if (LootHangar != null && LootHangar.IsValid)
                {
                    List<DirectItem> items = Cache.Instance.LootHangar.Items;
                    foreach (DirectItem item in items)
                    {
                        //if (item.FlagId)
                        Logging.Log(module, "Items: " + item.TypeName, Logging.White);

                        //
                        // add items with a high tier or faction to transferlist
                        //
                    }

                    //
                    // transfer items in transferlist to HighTierLootContainer
                    //
                    return true;
                }
            }

            return false;
        }

        //public DirectContainer _ammoHangar { get; set; }

        public DirectContainer _ammoHangar;

        public DirectContainer AmmoHangar
        {
            get
            {
                try
                {
                    if (Cache.Instance.InStation)
                    {
                        if (_ammoHangar == null)
                        {
                            if (Settings.Instance.AmmoHangarTabName != string.Empty)
                            {
                                Cache.Instance.AmmoHangarID = -99;
                                Cache.Instance.AmmoHangarID = Cache.Instance.DirectEve.GetCorpHangarId(Settings.Instance.AmmoHangarTabName); //- 1;
                                if (Settings.Instance.DebugHangars) Logging.Log("AmmoHangar: GetCorpAmmoHangarID", "AmmoHangarID is [" + Cache.Instance.AmmoHangarID + "]", Logging.Teal);

                                _ammoHangar = null;
                                _ammoHangar = Cache.Instance.DirectEve.GetCorporationHangar((int)Cache.Instance.AmmoHangarID);

                                if (_ammoHangar != null && _ammoHangar.IsValid) //do we have a corp hangar tab setup with that name?
                                {
                                    if (Settings.Instance.DebugHangars)
                                    {
                                        Logging.Log("AmmoHangar", "AmmoHangar is defined (no window needed)", Logging.DebugHangars);
                                        try
                                        {
                                            if (AmmoHangar.Items.Any())
                                            {
                                                int AmmoHangarItemCount = AmmoHangar.Items.Count();
                                                if (Settings.Instance.DebugHangars) Logging.Log("AmmoHangar", "AmmoHangar [" + Settings.Instance.AmmoHangarTabName + "] has [" + AmmoHangarItemCount + "] items", Logging.DebugHangars);
                                            }
                                        }
                                        catch (Exception exception)
                                        {
                                            Logging.Log("ReadyCorpAmmoHangar", "Exception [" + exception + "]", Logging.Debug);
                                        }
                                    }

                                    return _ammoHangar;
                                }

                                Logging.Log("AmmoHangar", "Opening Corporate Ammo Hangar: failed! No Corporate Hangar in this station! lag?", Logging.Orange);
                                return Cache.Instance.ItemHangar;

                            }

                            _ammoHangar = null;
                            return Cache.Instance.ItemHangar;
                        }

                        return _ammoHangar;
                    }

                    return null;
                }
                catch (Exception exception)
                {
                    Logging.Log("AmmoHangar", "Unable to define AmmoHangar [" + exception + "]", Logging.Teal);
                    return null;
                }     
            }
            set
            {
                _ammoHangar = value;
            }
        }

        public bool StackAmmoHangar(string module)
        {
            StackAmmohangarAttempts++;
            if (StackAmmohangarAttempts > 10)
            {
                Logging.Log("StackAmmoHangar", "Stacking the ammoHangar has failed too many times [" + StackAmmohangarAttempts + "]", Logging.Teal);
                if (StackAmmohangarAttempts > 30)
                {
                    Logging.Log("StackAmmoHangar", "Stacking the ammoHangar routine has run [" + StackAmmohangarAttempts + "] times without success, resetting counter", Logging.Teal);
                    StackAmmohangarAttempts = 0;
                }
                return true;
            }

            if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalMinutes < 10)
            {
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace)", Logging.Teal);
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "if (DateTime.UtcNow [" + DateTime.UtcNow + "] < Cache.Instance.NextOpenHangarAction [" + Cache.Instance.NextOpenHangarAction + "])", Logging.Teal);
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangarTabName))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Starting [Cache.Instance.StackCorpAmmoHangar]", Logging.Teal);
                        if (!Cache.Instance.StackCorpAmmoHangar(module)) return false;
                        if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Finished [Cache.Instance.StackCorpAmmoHangar]", Logging.Teal);
                        StackAmmohangarAttempts = 0;
                        return true;
                    }

                    //if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))
                    //{
                    //    if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                    //    if (!Cache.Instance.StackLootContainer("Cache.StackLootHangar")) return false;
                    //    StackLoothangarAttempts = 0;
                    //    return true;
                    //}

                    if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Starting [Cache.Instance.StackItemsHangarAsAmmoHangar]", Logging.Teal);
                    if (!Cache.Instance.StackItemsHangarAsAmmoHangar(module)) return false;
                    if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Finished [Cache.Instance.StackItemsHangarAsAmmoHangar]", Logging.Teal);
                    StackAmmohangarAttempts = 0;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("StackAmmoHangar", "Unable to complete StackAmmoHangar [" + exception + "]", Logging.Teal);
                return true;
            }
        }

        public bool CloseAmmoHangar(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangarTabName))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpAmmoHangar", "Debug: if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))", Logging.Teal);

                        if (Cache.Instance.AmmoHangar == null)
                        {
                            Cache.Instance.AmmoHangar = Cache.Instance.DirectEve.GetCorporationHangar(Settings.Instance.AmmoHangarTabName);
                        }

                        // Is the corp Ammo Hangar open?
                        if (Cache.Instance.AmmoHangar != null)
                        {
                            Cache.Instance.corpAmmoHangarSecondaryWindow = (DirectContainerWindow)Cache.Instance.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption.Contains(Settings.Instance.AmmoHangarTabName));
                            if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpAmmoHangar", "Debug: if (Cache.Instance.AmmoHangar != null)", Logging.Teal);

                            if (Cache.Instance.corpAmmoHangarSecondaryWindow != null)
                            {
                                if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpAmmoHangar", "Debug: if (ammoHangarWindow != null)", Logging.Teal);

                                // if open command it to close
                                Cache.Instance.corpAmmoHangarSecondaryWindow.Close();
                                Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                                Logging.Log(module, "Closing Corporate Ammo Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                                return false;
                            }

                            return true;
                        }

                        if (Cache.Instance.AmmoHangar == null)
                        {
                            if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangarTabName))
                            {
                                Logging.Log(module, "Closing Corporate Hangar: failed! No Corporate Hangar in this station! lag or setting misconfiguration?", Logging.Orange);
                            }

                            return false;
                        }
                    }
                    else //use local items hangar
                    {
                        if (Cache.Instance.AmmoHangar == null)
                        {
                            Cache.Instance.AmmoHangar = Cache.Instance.DirectEve.GetItemHangar();
                            return false;
                        }

                        // Is the items hangar open?
                        if (Cache.Instance.AmmoHangar.Window != null)
                        {
                            // if open command it to close
                            if (!Cache.Instance.CloseItemsHangar(module)) return false;
                            Logging.Log(module, "Closing AmmoHangar Hangar", Logging.White);
                            return true;
                        }

                        return true;
                    }
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("CloseAmmoHangar", "Unable to complete CloseAmmoHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public DirectContainer DroneBay { get; set; }

        //{
        //    get { return _dronebay ?? (_dronebay = Cache.Instance.DirectEve.GetShipsDroneBay()); }
        //}

        public bool OpenDroneBay(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextDroneBayAction)
            {
                //Logging.Log(module + ": Opening Drone Bay: waiting [" + Math.Round(Cache.Instance.NextOpenDroneBayAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]",Logging.White);
                return false;
            }

            try
            {
                if ((!Cache.Instance.InSpace && !Cache.Instance.InStation))
                {
                    Logging.Log(module, "Opening Drone Bay: We are not in station or space?!", Logging.Orange);
                    return false;
                }

                //if(Cache.Instance.ActiveShip.Entity == null || Cache.Instance.ActiveShip.GroupId == 31)
                //{
                //    Logging.Log(module + ": Opening Drone Bay: we are in a shuttle or not in a ship at all!");
                //    return false;
                //}

                if (Cache.Instance.InStation || Cache.Instance.InSpace)
                {
                    Cache.Instance.DroneBay = Cache.Instance.DirectEve.GetShipsDroneBay();
                }
                else
                {
                    return false;
                }

                if (GetShipsDroneBayAttempts > 10) //we her have not located a dronebay in over 10 attempts, we are not going to
                {
                    Logging.Log(module, "unable to find a dronebay after 11 attempts: continuing without defining one", Logging.DebugHangars);
                    return true;
                }

                if (Cache.Instance.DroneBay == null)
                {
                    Cache.Instance.NextDroneBayAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                    Logging.Log(module, "Opening Drone Bay: --- waiting [" + Math.Round(Cache.Instance.NextDroneBayAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    GetShipsDroneBayAttempts++;
                    return false;
                }

                if (Cache.Instance.DroneBay != null && Cache.Instance.DroneBay.IsValid)
                {
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenDroneBayOfActiveShip);
                    Cache.Instance.NextDroneBayAction = DateTime.UtcNow.AddSeconds(1 + Cache.Instance.RandomNumber(2, 3));
                    if (Settings.Instance.DebugHangars) Logging.Log(module, "DroneBay is ready. waiting [" + Math.Round(Cache.Instance.NextDroneBayAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    GetShipsDroneBayAttempts = 0;
                    return true;
                }

                if (Settings.Instance.DebugHangars) Logging.Log(module, "DroneBay is not ready...", Logging.White);
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyDroneBay", "Unable to complete ReadyDroneBay [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool CloseDroneBay(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextDroneBayAction)
            {
                //Logging.Log(module + ": Closing Drone Bay: waiting [" + Math.Round(Cache.Instance.NextOpenDroneBayAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]",Logging.White);
                return false;
            }

            try
            {
                if ((!Cache.Instance.InSpace && !Cache.Instance.InStation))
                {
                    Logging.Log(module, "Closing Drone Bay: We are not in station or space?!", Logging.Orange);
                    return false;
                }

                if (Cache.Instance.InStation || Cache.Instance.InSpace)
                {
                    Cache.Instance.DroneBay = Cache.Instance.DirectEve.GetShipsDroneBay();
                }
                else
                {
                    return false;
                }

                // Is the drone bay open? if so, close it
                if (Cache.Instance.DroneBay.Window != null)
                {
                    Cache.Instance.NextDroneBayAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                    Logging.Log(module, "Closing Drone Bay: waiting [" + Math.Round(Cache.Instance.NextDroneBayAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    Cache.Instance.DroneBay.Window.Close();
                    return true;
                }

                return true;
            }
            catch (Exception exception)
            {
                Logging.Log("CloseDroneBay", "Unable to complete CloseDroneBay [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public DirectLoyaltyPointStoreWindow _lpStore;
        public DirectLoyaltyPointStoreWindow LPStore
        {
            get
            {
                try
                {
                    if (Cache.Instance.InStation)
                    {
                        if (_lpStore == null)
                        {
                            if (!Cache.Instance.InStation)
                            {
                                Logging.Log("LPStore", "Opening LP Store: We are not in station?! There is no LP Store in space, waiting...", Logging.Orange);
                                return null;
                            }

                            if (Cache.Instance.InStation)
                            {
                                _lpStore = Cache.Instance.Windows.OfType<DirectLoyaltyPointStoreWindow>().FirstOrDefault();
                                
                                if (_lpStore == null)
                                {
                                    if (DateTime.UtcNow > Cache.Instance.NextLPStoreAction)
                                    {
                                        Logging.Log("LPStore", "Opening loyalty point store", Logging.White);
                                        Cache.Instance.NextLPStoreAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(30, 240));
                                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenLpstore);
                                        return null;    
                                    }

                                    return null;
                                }

                                return _lpStore;
                            }

                            return null;
                        }

                        return _lpStore;
                    }

                    return null;
                }
                catch (Exception exception)
                {
                    Logging.Log("LPStore", "Unable to define LPStore [" + exception + "]", Logging.Teal);
                    return null;
                }
            }
            private set
            {
                _lpStore = value;
            }
        }

        public bool CloseLPStore(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            if (!Cache.Instance.InStation)
            {
                Logging.Log(module, "Closing LP Store: We are not in station?!", Logging.Orange);
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.LPStore = Cache.Instance.Windows.OfType<DirectLoyaltyPointStoreWindow>().FirstOrDefault();
                if (Cache.Instance.LPStore != null)
                {
                    Logging.Log(module, "Closing loyalty point store", Logging.White);
                    Cache.Instance.LPStore.Close();
                    return false;
                }

                return true;
            }

            return true; //if we are not in station then the LP Store should have auto closed already.
        }

        private DirectFittingManagerWindow _fittingManagerWindow;
        public DirectFittingManagerWindow FittingManagerWindow
        {
            get
            {
                try
                {
                    if (Cache.Instance.InStation)
                    {
                        if (_fittingManagerWindow == null)
                        {
                            if (!Cache.Instance.InStation || Cache.Instance.InSpace)
                            {
                                Logging.Log("FittingManager", "Opening Fitting Manager: We are not in station?! There is no Fitting Manager in space, waiting...", Logging.Debug);
                                return null;
                            }

                            if (Cache.Instance.InStation)
                            {
                                _fittingManagerWindow = Cache.Instance.Windows.OfType<DirectFittingManagerWindow>().FirstOrDefault();

                                if (_fittingManagerWindow == null)
                                {
                                    if (DateTime.UtcNow > Cache.Instance.NextWindowAction)
                                    {
                                        Logging.Log("FittingManager", "Opening Fitting Manager Window", Logging.White);
                                        Cache.Instance.NextWindowAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(10, 24));
                                        Cache.Instance.DirectEve.OpenFitingManager();
                                        return null;
                                    }

                                    if (Settings.Instance.DebugFittingMgr) Logging.Log("FittingManager", "NextWindowAction is still in the future [" + Cache.Instance.NextWindowAction.Subtract(DateTime.UtcNow).Seconds + "] sec", Logging.Debug);
                                    return null;
                                }

                                return _fittingManagerWindow;
                            }

                            return null;
                        }

                        return _fittingManagerWindow;
                    }

                    Logging.Log("FittingManager", "Opening Fitting Manager: We are not in station?! There is no Fitting Manager in space, waiting...", Logging.Debug);
                    return null;
                }
                catch (Exception exception)
                {
                    Logging.Log("FittingManager", "Unable to define FittingManagerWindow [" + exception + "]", Logging.Teal);
                    return null;
                }
            }
            private set
            {
                _fittingManagerWindow = value;
            }
        }

        public bool CloseFittingManager(string module)
        {
            if (Settings.Instance.UseFittingManager)
            {
                if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
                {
                    return false;
                }

                if (Cache.Instance.Windows.OfType<DirectFittingManagerWindow>().FirstOrDefault() != null)
                {
                    Logging.Log(module, "Closing Fitting Manager Window", Logging.White);
                    Cache.Instance.FittingManagerWindow.Close();
                    Cache.Instance.FittingManagerWindow = null;
                    Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(2);
                    return true;
                }
                
                return true;    
            }

            return true;
        }
        
        public bool OpenAgentWindow(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextWindowAction)
            {
                if (Settings.Instance.DebugAgentInteractionReplyToAgent) Logging.Log(module, "if (DateTime.UtcNow < Cache.Instance.NextAgentWindowAction)", Logging.Yellow);
                return false;
            }

            if (AgentInteraction.Agent.Window == null)
            {
                if (Settings.Instance.DebugAgentInteractionReplyToAgent) Logging.Log(module, "Attempting to Interact with the agent named [" + AgentInteraction.Agent.Name + "] in [" + Cache.Instance.DirectEve.GetLocationName(AgentInteraction.Agent.SolarSystemId) + "]", Logging.Yellow);
                Cache.Instance.NextWindowAction = DateTime.UtcNow.AddSeconds(10);
                AgentInteraction.Agent.InteractWith();
                return false;
            }

            if (!AgentInteraction.Agent.Window.IsReady)
            {
                return false;
            }

            if (AgentInteraction.Agent.Window.IsReady && AgentInteraction.AgentId == AgentInteraction.Agent.AgentId)
            {
                if (Settings.Instance.DebugAgentInteractionReplyToAgent) Logging.Log(module, "AgentWindow is ready", Logging.Yellow);
                return true;
            }

            return false;
        }

        public DirectWindow JournalWindow { get; set; }

        public bool OpenJournalWindow(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextWindowAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.JournalWindow = Cache.Instance.GetWindowByName("journal");

                // Is the journal window open?
                if (Cache.Instance.JournalWindow == null)
                {
                    // No, command it to open
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenJournal);
                    Cache.Instance.NextWindowAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(2, 4));
                    Logging.Log(module, "Opening Journal Window: waiting [" +
                                Math.Round(Cache.Instance.NextWindowAction.Subtract(DateTime.UtcNow).TotalSeconds,
                                           0) + "sec]", Logging.White);
                    return false;
                }

                return true; //if JournalWindow is not null then the window must be open.
            }

            return false;
        }

        public DirectMarketWindow MarketWindow { get; set; }

        public bool OpenMarket(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextWindowAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.MarketWindow = Cache.Instance.Windows.OfType<DirectMarketWindow>().FirstOrDefault();
                
                // Is the Market window open?
                if (Cache.Instance.MarketWindow == null)
                {
                    // No, command it to open
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenMarket);
                    Cache.Instance.NextWindowAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(2, 4));
                    Logging.Log(module, "Opening Market Window: waiting [" + Math.Round(Cache.Instance.NextWindowAction.Subtract(DateTime.UtcNow).TotalSeconds,0) + "sec]", Logging.White);
                    return false;
                }

                return true; //if MarketWindow is not null then the window must be open.
            }

            return false;
        }

        public bool CloseMarket(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextWindowAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.MarketWindow = Cache.Instance.Windows.OfType<DirectMarketWindow>().FirstOrDefault();

                // Is the Market window open?
                if (Cache.Instance.MarketWindow == null)
                {
                    //already closed
                    return true;
                }

                //if MarketWindow is not null then the window must be open, so close it.
                Cache.Instance.MarketWindow.Close();
                return true; 
            }

            return true;
        }

        public bool OpenContainerInSpace(string module, EntityCache containerToOpen)
        {
            if (DateTime.UtcNow < Cache.Instance.NextLootAction)
            {
                return false;
            }

            if (Cache.Instance.InSpace && containerToOpen.Distance <= (int)Distances.ScoopRange)
            {
                Cache.Instance.ContainerInSpace = Cache.Instance.DirectEve.GetContainer(containerToOpen.Id);

                if (Cache.Instance.ContainerInSpace != null)
                {
                    if (Cache.Instance.ContainerInSpace.Window == null)
                    {
                        containerToOpen.OpenCargo();
                        Cache.Instance.NextLootAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.LootingDelay_milliseconds);
                        Logging.Log(module, "Opening Container: waiting [" + Math.Round(Cache.Instance.NextLootAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + " sec]", Logging.White);
                        return false;
                    }

                    if (!Cache.Instance.ContainerInSpace.Window.IsReady)
                    {
                        Logging.Log(module, "Container window is not ready", Logging.White);
                        return false;
                    }

                    if (Cache.Instance.ContainerInSpace.Window.IsPrimary())
                    {
                        Logging.Log(module, "Opening Container window as secondary", Logging.White);
                        Cache.Instance.ContainerInSpace.Window.OpenAsSecondary();
                        Cache.Instance.NextLootAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.LootingDelay_milliseconds);
                        return true;
                    }
                }

                return true;
            }
            Logging.Log(module, "Not in space or not in scoop range", Logging.Orange);
            return true;
        }

        internal static DirectBookmark _undockBookmark;
        public DirectBookmark UndockBookmark
        {
            get
            {
                try
                {
                    if (_undockBookmark == null)
                    {
                        IEnumerable<DirectBookmark> _undockBookmarkList = Cache.Instance.BookmarksByLabel(Settings.Instance.UndockPrefix);
                        if (_undockBookmarkList != null && _undockBookmarkList.Any())
                        {
                            _undockBookmarkList = _undockBookmarkList.Where(i => i.LocationId == Cache.Instance.DirectEve.Session.LocationId).ToList();
                            _undockBookmark = _undockBookmarkList.OrderBy(i => Cache.Instance.DistanceFromMe(i.X ?? 0, i.Y ?? 0, i.Z ?? 0)).FirstOrDefault(b => Cache.Instance.DistanceFromMe(b.X ?? 0, b.Y ?? 0, b.Z ?? 0) < (int)Distances.NextPocketDistance);
                            if (_undockBookmark != null)
                            {
                                return _undockBookmark;
                            }

                            return null;    
                        }

                        return null;
                    }

                    return _undockBookmark;
                }
                catch (Exception exception)
                {
                    Logging.Log("UndockBookmark", "[" + exception + "]", Logging.Teal);
                    return null;
                }
            }
            internal set
            {
                _undockBookmark = value;
            }

        }
        
        public IEnumerable<DirectBookmark> SafeSpotBookmarks
        {
            get
            {
                try
                {

                    if (_safeSpotBookmarks == null)
                    {
                        _safeSpotBookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.SafeSpotBookmarkPrefix).ToList();    
                    }

                    if (_safeSpotBookmarks != null && _safeSpotBookmarks.Any())
                    {
                        return _safeSpotBookmarks;
                    }

                    return new List<DirectBookmark>();
                }
                catch (Exception exception)
                {
                    Logging.Log("Cache.SafeSpotBookmarks", "Exception [" + exception + "]", Logging.Debug);    
                }

                return new List<DirectBookmark>();
            }
        }

        public IEnumerable<DirectBookmark> AfterMissionSalvageBookmarks
        {
            get
            {
                string _bookmarkprefix = Settings.Instance.BookmarkPrefix;
                
                if (_States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior)
                {
                    return Cache.Instance.BookmarksByLabel(_bookmarkprefix + " ").Where(e => e.CreatedOn != null && e.CreatedOn.Value.CompareTo(AgedDate) < 0).ToList();
                }

                if (Cache.Instance.BookmarksByLabel(_bookmarkprefix + " ") != null)
                {
                    return Cache.Instance.BookmarksByLabel(_bookmarkprefix + " ").ToList();
                }
                
                return new List<DirectBookmark>();
            }
        }

        //Represents date when bookmarks are eligible for salvage. This should not be confused with when the bookmarks are too old to salvage.
        public DateTime AgedDate
        {
            get
            {
                return DateTime.UtcNow.AddMinutes(-Settings.Instance.AgeofBookmarksForSalvageBehavior);
            }
        }

        public DirectBookmark GetSalvagingBookmark
        {
            get
            {
                if (Cache.Instance.AllBookmarks != null && Cache.Instance.AllBookmarks.Any())
                {
                    List<DirectBookmark> _SalvagingBookmarks = null;
                    DirectBookmark _SalvagingBookmark = null;
                    if (Settings.Instance.FirstSalvageBookmarksInSystem)
                    {
                        Logging.Log("CombatMissionsBehavior.BeginAftermissionSalvaging", "Salvaging at first bookmark from system", Logging.White);
                        _SalvagingBookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ");
                        if (_SalvagingBookmarks != null && _SalvagingBookmarks.Any())
                        {
                            _SalvagingBookmark = _SalvagingBookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault(c => c.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId);
                            return _SalvagingBookmark ?? null;
                        }

                        return null;
                    }

                    Logging.Log("CombatMissionsBehavior.BeginAftermissionSalvaging", "Salvaging at first oldest bookmarks", Logging.White);
                    _SalvagingBookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ");
                    if (_SalvagingBookmarks != null && _SalvagingBookmarks.Any())
                    {
                        _SalvagingBookmark = _SalvagingBookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault();
                        return _SalvagingBookmark ?? null;
                    }

                    return null;
                }

                return null;
            }
        }

        public DirectBookmark GetTravelBookmark
        {
            get
            {
                DirectBookmark bm = Cache.Instance.BookmarksByLabel(Settings.Instance.TravelToBookmarkPrefix).OrderByDescending(b => b.CreatedOn).FirstOrDefault(c => c.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId) ??
                                    Cache.Instance.BookmarksByLabel(Settings.Instance.TravelToBookmarkPrefix).OrderByDescending(b => b.CreatedOn).FirstOrDefault() ??
                                    Cache.Instance.BookmarksByLabel("Jita").OrderByDescending(b => b.CreatedOn).FirstOrDefault() ??
                                    Cache.Instance.BookmarksByLabel("Rens").OrderByDescending(b => b.CreatedOn).FirstOrDefault() ??
                                    Cache.Instance.BookmarksByLabel("Amarr").OrderByDescending(b => b.CreatedOn).FirstOrDefault() ??
                                    Cache.Instance.BookmarksByLabel("Dodixie").OrderByDescending(b => b.CreatedOn).FirstOrDefault();

                if (bm !=null)
                {
                    Logging.Log("CombatMissionsBehavior.BeginAftermissionSalvaging", "GetTravelBookmark ["  + bm.Title +  "][" + bm.LocationId  + "]", Logging.White);
                }
                return bm;    
            }
        }

        public bool GateInGrid()
        {
            if (Cache.Instance.AccelerationGates.FirstOrDefault() == null || !Cache.Instance.AccelerationGates.Any())
            {
                return false;
            }

            Cache.Instance.LastAccelerationGateDetected = DateTime.UtcNow;
            return true;
        }

        private int _bookmarkDeletionAttempt;
        public DateTime NextBookmarkDeletionAttempt = DateTime.UtcNow;

        public bool DeleteBookmarksOnGrid(string module)
        {
            if (DateTime.UtcNow < NextBookmarkDeletionAttempt)
            {
                return false;
            }

            NextBookmarkDeletionAttempt = DateTime.UtcNow.AddSeconds(5 + Settings.Instance.RandomNumber(1, 5));

            //
            // remove all salvage bookmarks over 48hrs old - they have long since been rendered useless
            //
            DeleteUselessSalvageBookmarks(module);

            List<DirectBookmark> bookmarksInLocal = new List<DirectBookmark>(AfterMissionSalvageBookmarks.Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId).
                                                                   OrderBy(b => b.CreatedOn));
            DirectBookmark onGridBookmark = bookmarksInLocal.FirstOrDefault(b => Cache.Instance.DistanceFromMe(b.X ?? 0, b.Y ?? 0, b.Z ?? 0) < (int)Distances.OnGridWithMe);
            if (onGridBookmark != null)
            {
                _bookmarkDeletionAttempt++;
                if (_bookmarkDeletionAttempt <= bookmarksInLocal.Count() + 60)
                {
                    Logging.Log(module, "removing salvage bookmark:" + onGridBookmark.Title, Logging.White);
                    onGridBookmark.Delete();
                    return false;
                }

                if (_bookmarkDeletionAttempt > bookmarksInLocal.Count() + 60)
                {
                    Logging.Log(module, "error removing bookmark!" + onGridBookmark.Title, Logging.White);
                    _States.CurrentQuestorState = QuestorState.Error;
                    return false;
                }

                return false;
            }

            _bookmarkDeletionAttempt = 0;
            Cache.Instance.NextSalvageTrip = DateTime.UtcNow;
            Statistics.Instance.FinishedSalvaging = DateTime.UtcNow;
            return true;
        }

        public bool DeleteUselessSalvageBookmarks(string module)
        {
            if (DateTime.UtcNow < NextBookmarkDeletionAttempt)
            {
                return false;
            }

            NextBookmarkDeletionAttempt = DateTime.UtcNow.AddSeconds(5 + Settings.Instance.RandomNumber(1, 5));

            try
            {
                //Delete bookmarks older than 2 hours.
                DateTime bmExpirationDate = DateTime.UtcNow.AddMinutes(-Settings.Instance.AgeofSalvageBookmarksToExpire);
                List<DirectBookmark> uselessSalvageBookmarks = new List<DirectBookmark>(AfterMissionSalvageBookmarks.Where(e => e.CreatedOn != null && e.CreatedOn.Value.CompareTo(bmExpirationDate) < 0).ToList());

                DirectBookmark uselessSalvageBookmark = uselessSalvageBookmarks.FirstOrDefault();
                if (uselessSalvageBookmark != null)
                {
                    _bookmarkDeletionAttempt++;
                    if (_bookmarkDeletionAttempt <= uselessSalvageBookmarks.Count(e => e.CreatedOn != null && e.CreatedOn.Value.CompareTo(bmExpirationDate) < 0) + 60)
                    {
                        Logging.Log(module, "removing a salvage bookmark that aged more than [" + Settings.Instance.AgeofSalvageBookmarksToExpire + "]" + uselessSalvageBookmark.Title, Logging.White);
                        uselessSalvageBookmark.Delete();
                        return false;
                    }

                    if (_bookmarkDeletionAttempt > uselessSalvageBookmarks.Count(e => e.CreatedOn != null && e.CreatedOn.Value.CompareTo(bmExpirationDate) < 0) + 60)
                    {
                        Logging.Log(module, "error removing bookmark!" + uselessSalvageBookmark.Title, Logging.White);
                        _States.CurrentQuestorState = QuestorState.Error;
                        return false;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                Logging.Log("Cache.DeleteBookmarksOnGrid", "Delete old unprocessed salvage bookmarks: exception generated:" + ex.Message, Logging.White);
            }

            return true;
        }

        public bool RepairItems(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(5) && !Cache.Instance.InSpace || DateTime.UtcNow < NextRepairItemsAction) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                //Logging.Log(module, "Waiting...", Logging.Orange);
                return false;
            }

            if (!Cache.Instance.Windows.Any())
            {
                return false;
            }

            NextRepairItemsAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(2, 4));

            if (Cache.Instance.InStation && !Cache.Instance.DirectEve.hasRepairFacility())
            {
                Logging.Log(module, "This station does not have repair facilities to use! aborting attempt to use non-existent repair facility.", Logging.Orange);
                return true;
            }

            if (Cache.Instance.InStation)
            {
                DirectRepairShopWindow repairWindow = Cache.Instance.Windows.OfType<DirectRepairShopWindow>().FirstOrDefault();

                DirectWindow repairQuote = Cache.Instance.GetWindowByName("Set Quantity");

                if (doneUsingRepairWindow)
                {
                    doneUsingRepairWindow = false;
                    if (repairWindow != null) repairWindow.Close();
                    return true;
                }

                foreach (DirectWindow window in Cache.Instance.Windows)
                {
                    if (window.Name == "modal")
                    {
                        if (!string.IsNullOrEmpty(window.Html))
                        {
                            if (window.Html.Contains("Repairing these items will cost"))
                            {
                                if (window.Html != null) Logging.Log("RepairItems", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White); 
                                Logging.Log(module, "Closing Quote for Repairing All with YES", Logging.White);
                                window.AnswerModal("Yes");
                                doneUsingRepairWindow = true;
                                return false;
                            }
                        }
                    }
                }

                if (repairQuote != null && repairQuote.IsModal && repairQuote.IsKillable)
                {
                    if (repairQuote.Html != null) Logging.Log("RepairItems", "Content of modal window (HTML): [" + (repairQuote.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                    Logging.Log(module, "Closing Quote for Repairing All with OK", Logging.White);
                    repairQuote.AnswerModal("OK");
                    doneUsingRepairWindow = true;
                    return false;
                }

                if (repairWindow == null)
                {
                    Logging.Log(module, "Opening repairshop window", Logging.White);
                    Cache.Instance.DirectEve.OpenRepairShop();
                    NextRepairItemsAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(1, 3));
                    return false;
                }

                if (!Cache.Instance.OpenShipsHangar(module)) return false;
                if (Cache.Instance.ItemHangar == null) return false;
                if (Settings.Instance.UseDrones)
                {
                    if (!Cache.Instance.OpenDroneBay(module)) {return false;}
                }

                //repair ships in ships hangar
                List<DirectItem> repairAllItems = Cache.Instance.ShipHangar.Items;

                //repair items in items hangar and drone bay of active ship also
                repairAllItems.AddRange(Cache.Instance.ItemHangar.Items);
                if (Settings.Instance.UseDrones)
                {
                    repairAllItems.AddRange(Cache.Instance.DroneBay.Items);
                }

                if (repairAllItems.Any())
                {
                    if (String.IsNullOrEmpty(repairWindow.AvgDamage()))
                    {
                        Logging.Log(module, "Add items to repair list", Logging.White);
                        repairWindow.RepairItems(repairAllItems);
                        NextRepairItemsAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(2, 4));
                        return false;
                    }

                    Logging.Log(module, "Repairing Items: repairWindow.AvgDamage: " + repairWindow.AvgDamage(), Logging.White);
                    if (repairWindow.AvgDamage() == "Avg: 0.0 % Damaged")
                    {
                        Logging.Log(module, "Repairing Items: Zero Damage: skipping repair.", Logging.White);
                        repairWindow.Close();
                        Cache.Instance.RepairAll = false;
                        return true;
                    }

                    repairWindow.RepairAll();
                    Cache.Instance.RepairAll = false;
                    NextRepairItemsAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(2, 4));
                    return false;
                }

                Logging.Log(module, "No items available, nothing to repair.", Logging.Orange);
                return true;
            }
            Logging.Log(module, "Not in station.", Logging.Orange);
            return false;
        }

        public bool RepairDrones(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(5) && !Cache.Instance.InSpace || DateTime.UtcNow < NextRepairDronesAction) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                //Logging.Log(module, "Waiting...", Logging.Orange);
                return false;
            }

            NextRepairDronesAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(2, 4));

            if (Cache.Instance.InStation && !Cache.Instance.DirectEve.hasRepairFacility())
            {
                Logging.Log(module, "This station does not have repair facilities to use! aborting attempt to use non-existent repair facility.", Logging.Orange);
                return true;
            }

            if (Cache.Instance.InStation)
            {
                DirectRepairShopWindow repairWindow = Cache.Instance.Windows.OfType<DirectRepairShopWindow>().FirstOrDefault();

                DirectWindow repairQuote = Cache.Instance.GetWindowByName("Set Quantity");

                if (GetShipsDroneBayAttempts > 10 && Cache.Instance.DroneBay == null)
                {
                    Logging.Log(module, "Your current ship does not have a drone bay, aborting repair of drones", Logging.Teal);
                    return true;
                }

                if (doneUsingRepairWindow)
                {
                    Logging.Log(module, "Done with RepairShop: closing", Logging.White);
                    doneUsingRepairWindow = false;
                    if (repairWindow != null) repairWindow.Close();
                    return true;
                }

                if (repairQuote != null && repairQuote.IsModal && repairQuote.IsKillable)
                {
                    if (repairQuote.Html != null) Logging.Log("RepairDrones", "Content of modal window (HTML): [" + (repairQuote.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                    Logging.Log(module, "Closing Quote for Repairing Drones with OK", Logging.White);
                    repairQuote.AnswerModal("OK");
                    doneUsingRepairWindow = true;
                    return false;
                }

                if (repairWindow == null)
                {
                    Logging.Log(module, "Opening repairshop window", Logging.White);
                    Cache.Instance.DirectEve.OpenRepairShop();
                    NextRepairDronesAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(1, 3));
                    return false;
                }

                if (!Cache.Instance.OpenDroneBay("Repair Drones")) return false;

                List<DirectItem> dronesToRepair;
                try
                {
                    dronesToRepair = Cache.Instance.DroneBay.Items;
                }
                catch (Exception exception)
                {
                    Logging.Log(module, "Dronebay.Items could not be listed, nothing to repair.[" + exception + "]", Logging.Orange);
                    return true;
                }

                if (dronesToRepair.Any())
                {
                    if (String.IsNullOrEmpty(repairWindow.AvgDamage()))
                    {
                        Logging.Log(module, "Get Quote for Repairing [" + dronesToRepair.Count() + "] Drones", Logging.White);
                        repairWindow.RepairItems(dronesToRepair);
                        return false;
                    }

                    Logging.Log(module, "Repairing Drones: repairWindow.AvgDamage: " + repairWindow.AvgDamage(), Logging.White);
                    if (repairWindow.AvgDamage() == "Avg: 0.0 % Damaged")
                    {
                        repairWindow.Close();
                        return true;
                    }

                    repairWindow.RepairAll();
                    NextRepairDronesAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(1, 2));
                    return false;
                }

                Logging.Log(module, "No drones available, nothing to repair.", Logging.Orange);
                return true;
            }

            Logging.Log(module, "Not in station.", Logging.Orange);
            return false;
        }
    }
}
