// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

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
    using global::Questor.Modules.Actions;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.Logging;
    using DirectEve;
    using InnerSpaceAPI;

    public class Cache
    {
        /// <summary>
        ///   Singleton implementation
        /// </summary>
        private static Cache _instance = new Cache();

        /// <summary>
        ///   Active Drones
        /// </summary>
        private List<EntityCache> _activeDrones;

        private DirectAgent _agent;

        /// <summary>
        ///   Agent cache
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
        ///   Approaching cache
        /// </summary>
        //private int? _approachingId;
        private EntityCache _approaching;

        /// <summary>
        ///   BigObjects we are likely to bump into (mainly LCOs)
        /// </summary>
        private List<EntityCache> _bigObjects;

        /// <summary>
        ///   BigObjects we are likely to bump into (mainly LCOs)
        /// </summary>
        private List<EntityCache> _gates;

        /// <summary>
        ///   BigObjects we are likely to bump into (mainly LCOs)
        /// </summary>
        private List<EntityCache> _bigObjectsAndGates;

        /// <summary>
        ///   objects we are likely to bump into (Anything that is not an NPC a wreck or a can)
        /// </summary>
        private List<EntityCache> _objects;

        /// <summary>
        ///   Returns all non-empty wrecks and all containers
        /// </summary>
        private List<EntityCache> _containers;

        /// <summary>
        ///   Entities cache (all entities within 256km)
        /// </summary>
        private List<EntityCache> _entities;

        /// <summary>
        ///   Damaged drones
        /// </summary>
        public IEnumerable<EntityCache> DamagedDrones;

        /// <summary>
        ///   Entities by Id
        /// </summary>
        private readonly Dictionary<long, EntityCache> _entitiesById;

        /// <summary>
        ///   Module cache
        /// </summary>
        private List<ModuleCache> _modules;

        /// <summary>
        ///   Priority targets (e.g. warp scramblers or mission kill targets)
        /// </summary>
        public List<PriorityTarget> _primaryWeaponPriorityTargets;

        public List<PriorityTarget> _dronePriorityTargets;

        public String _priorityTargets_text;

        public String OrbitEntityNamed;

        public DirectLocation MissionSolarSystem;

        public string DungeonId;

        /// <summary>
        ///   Star cache
        /// </summary>
        private EntityCache _star;

        /// <summary>
        ///   Station cache
        /// </summary>
        private List<EntityCache> _stations;

        /// <summary>
        ///   Stargate cache
        /// </summary>
        private List<EntityCache> _stargates;

        /// <summary>
        ///   Stargate by name
        /// </summary>
        private EntityCache _stargate;

        /// <summary>
        ///   JumpBridges
        /// </summary>
        private IEnumerable<EntityCache> _jumpBridges;

        /// <summary>
        ///   Targeted by cache
        /// </summary>
        private List<EntityCache> _targetedBy;

        /// <summary>
        ///   Targeting cache
        /// </summary>
        private List<EntityCache> _targeting;

        /// <summary>
        ///   Targets cache
        /// </summary>
        private List<EntityCache> _targets;

        /// <summary>
        ///   Aggressed cache
        /// </summary>
        private List<EntityCache> _aggressed;

        /// <summary>
        ///   IDs in Inventory window tree (on left)
        /// </summary>
        public List<long> _IDsinInventoryTree;

        /// <summary>
        ///   Returns all unlooted wrecks & containers
        /// </summary>
        private List<EntityCache> _unlootedContainers;

        private List<EntityCache> _unlootedWrecksAndSecureCans;

        private List<DirectWindow> _windows;

        public void DirecteveDispose()
        {
            Logging.Log("Questor", "started calling DirectEve.Dispose()", Logging.White);
            Cache.Instance.DirectEve.Dispose(); //could this hang?
            Logging.Log("Questor", "finished calling DirectEve.Dispose()", Logging.White);
            Process.GetCurrentProcess().Kill();
            Environment.Exit(0);
        }

        public void IterateInvTypes()
        {
            string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (path != null)
            {
                string invtypesXmlFile = System.IO.Path.Combine(path, "InvTypes.xml");
                InvTypesById = new Dictionary<int, InvType>();
                Cache.Instance.InvTypes = XDocument.Load(invtypesXmlFile);
                if (InvTypes.Root != null)
                {
                    foreach (XElement element in InvTypes.Root.Elements("invtype"))
                    {
                        InvTypesById.Add((int)element.Attribute("id"), new InvType(element));
                    }
                }
            }
            else
            {
                Logging.Log("LoadInvTypesXML", "unable to find [" + System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "]", Logging.White);
            }
        }

        public void LoadInvTypesXML()
        {
            string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (path != null)
            {
                string invtypesXmlFile = System.IO.Path.Combine(path, "InvTypes.xml");
                InvTypesById = new Dictionary<int, InvType>();
                InvTypes = XDocument.Load(invtypesXmlFile);
                if (InvTypes.Root != null)
                {
                    foreach (XElement element in InvTypes.Root.Elements("invtype"))
                    {
                        InvTypesById.Add((int)element.Attribute("id"), new InvType(element));

                        //_item = new ListItems();
                        //_item.Id = (int)element.Attribute("id");
                        //_item.Name = (string)element.Attribute("name");
                        //List.Add(_item);
                    }
                }
            }
            else
            {
                Logging.Log("LoadInvTypesXML", "unable to find [" + System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "]", Logging.White);
            }
        }

        public Cache()
        {
            //string line = "Cache: new cache instance being instantiated";
            //InnerSpace.Echo(string.Format("{0:HH:mm:ss} {1}", DateTime.UtcNow, line));
            //line = string.Empty;

            string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (path != null)
            {
                ShipTargetValues = new List<ShipTargetValue>();
                XDocument values = XDocument.Load(System.IO.Path.Combine(path, "ShipTargetValues.xml"));
                if (values.Root != null)
                {
                    foreach (XElement value in values.Root.Elements("ship"))
                    {
                        ShipTargetValues.Add(new ShipTargetValue(value));
                    }
                }

                InvTypesById = new Dictionary<int, InvType>();
                XDocument invTypes = XDocument.Load(System.IO.Path.Combine(path, "InvTypes.xml"));
                if (invTypes.Root != null)
                {
                    foreach (XElement element in invTypes.Root.Elements("invtype"))
                    {
                        InvTypesById.Add((int)element.Attribute("id"), new InvType(element));
                    }
                }
            }

            _primaryWeaponPriorityTargets = new List<PriorityTarget>();
            _dronePriorityTargets = new List<PriorityTarget>();
            LastModuleTargetIDs = new Dictionary<long, long>();
            TargetingIDs = new Dictionary<long, DateTime>();
            _entitiesById = new Dictionary<long, EntityCache>();

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
                        if (Settings.Instance.DebugWatchForActiveWars) Logging.Log("IsCorpInWar", "Your corp is involved in a war, be carefull", Logging.Orange);
                    }

                    return _isCorpInWar;
                }
                
                return _isCorpInWar; 
            }
        }

        public bool LocalSafe(int maxBad, double stand)
        {
            int number = 0;
            var local = (DirectChatWindow)GetWindowByName("Local");
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

            return true;
        }

        public DirectEve DirectEve { get; set; }

        public Dictionary<int, InvType> InvTypesById { get; private set; }

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
        ///   Current OptimalRange during the mission (effected by ewar)
        /// </summary>
        public int OptimalRange { get; set; }

        /// <summary>
        ///   Force Salvaging after mission
        /// </summary>
        public bool AfterMissionSalvaging { get; set; }

        public double MaxRange
        {
            get { return Math.Min(Cache.Instance.WeaponRange, Cache.Instance.DirectEve.ActiveShip.MaxTargetRange); }
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
                    if ((Cache.Instance.CargoHold != null) && (Cache.Instance.CargoHold.IsValid))
                    {
                        ammo = ammo.Where(a => Cache.Instance.CargoHold.Items.Any(i => a.TypeId == i.TypeId && i.Quantity >= Settings.Instance.MinimumAmmoCharges));
                    }
                    else
                    {
                        return System.Convert.ToInt32(Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);
                    }

                    // Return ship range if there's no ammo left
                    if (!ammo.Any())
                    {
                        return System.Convert.ToInt32(Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);
                    }

                    return ammo.Max(a => a.Range);
                }
                catch (Exception ex)
                {
                    if (Settings.Instance.DebugExceptions) Logging.Log("Cache.WeaponRange", "exception was:" + ex.Message, Logging.Teal);

                    // Return max range
                    if (Cache.Instance.DirectEve.ActiveShip != null)
                    {
                        return System.Convert.ToInt32(Cache.Instance.DirectEve.ActiveShip.MaxTargetRange);
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

        private string _agentName = "";

        private DateTime _nextAgentWindowAction;

        public DateTime NextAgentWindowAction
        {
            get
            {
                return _nextAgentWindowAction;
            }
            set
            {
                _nextAgentWindowAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextGetAgentMissionAction;

        public DateTime NextGetAgentMissionAction
        {
            get
            {
                return _nextGetAgentMissionAction;
            }
            set
            {
                _nextGetAgentMissionAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOpenContainerInSpaceAction;

        public DateTime NextOpenContainerInSpaceAction
        {
            get
            {
                return _nextOpenContainerInSpaceAction;
            }
            set
            {
                _nextOpenContainerInSpaceAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOpenJournalWindowAction;

        public DateTime NextOpenJournalWindowAction
        {
            get
            {
                return _nextOpenJournalWindowAction;
            }
            set
            {
                _nextOpenJournalWindowAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOpenMarketAction;

        public DateTime NextOpenMarketAction
        {
            get
            {
                return _nextOpenMarketAction;
            }
            set
            {
                _nextOpenMarketAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOpenLootContainerAction;

        public DateTime NextOpenLootContainerAction
        {
            get
            {
                return _nextOpenLootContainerAction;
            }
            set
            {
                _nextOpenLootContainerAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOpenCorpBookmarkHangarAction;

        public DateTime NextOpenCorpBookmarkHangarAction
        {
            get
            {
                return _nextOpenCorpBookmarkHangarAction;
            }
            set
            {
                _nextOpenCorpBookmarkHangarAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextDroneBayAction;

        public DateTime NextDroneBayAction
        {
            get
            {
                return _nextDroneBayAction;
            }
            set
            {
                _nextDroneBayAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOpenHangarAction;

        public DateTime NextOpenHangarAction
        {
            get { return _nextOpenHangarAction; }
            set
            {
                _nextOpenHangarAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOpenCargoAction;

        public DateTime NextOpenCargoAction
        {
            get
            {
                return _nextOpenCargoAction;
            }
            set
            {
                _nextOpenCargoAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _lastAction = DateTime.UtcNow;

        public DateTime LastAction
        {
            get
            {
                return _lastAction;
            }
            set
            {
                _lastAction = value;
            }
        }

        private DateTime _nextArmAction = DateTime.UtcNow;

        public DateTime NextArmAction
        {
            get
            {
                return _nextArmAction;
            }
            set
            {
                _nextArmAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextSalvageAction = DateTime.UtcNow;

        public DateTime NextSalvageAction
        {
            get
            {
                return _nextSalvageAction;
            }
            set
            {
                _nextSalvageAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextTractorBeamAction = DateTime.UtcNow;

        public DateTime NextTractorBeamAction
        {
            get
            {
                return _nextTractorBeamAction;
            }
            set
            {
                _nextTractorBeamAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }
        private DateTime _nextLootAction = DateTime.UtcNow;

        public DateTime NextLootAction
        {
            get
            {
                return _nextLootAction;
            }
            set
            {
                _nextLootAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _lastJettison = DateTime.UtcNow;

        public DateTime LastJettison
        {
            get
            {
                return _lastJettison;
            }
            set
            {
                _lastJettison = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextDefenseModuleAction = DateTime.UtcNow;

        public DateTime NextDefenseModuleAction
        {
            get
            {
                return _nextDefenseModuleAction;
            }
            set
            {
                _nextDefenseModuleAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextAfterburnerAction = DateTime.UtcNow;

        public DateTime NextAfterburnerAction
        {
            get { return _nextAfterburnerAction; }
            set
            {
                _nextAfterburnerAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextRepModuleAction = DateTime.UtcNow;

        public DateTime NextRepModuleAction
        {
            get { return _nextRepModuleAction; }
            set
            {
                _nextRepModuleAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextActivateSupportModules = DateTime.UtcNow;

        public DateTime NextActivateSupportModules
        {
            get { return _nextActivateSupportModules; }
            set
            {
                _nextActivateSupportModules = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextRemoveBookmarkAction = DateTime.UtcNow;

        public DateTime NextRemoveBookmarkAction
        {
            get { return _nextRemoveBookmarkAction; }
            set
            {
                _nextRemoveBookmarkAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextApproachAction = DateTime.UtcNow;

        public DateTime NextApproachAction
        {
            get { return _nextApproachAction; }
            set
            {
                _nextApproachAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextOrbit;

        public DateTime NextOrbit
        {
            get { return _nextOrbit; }
            set
            {
                _nextOrbit = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextWarpTo;

        public DateTime NextWarpTo
        {
            get { return _nextWarpTo; }
            set
            {
                _nextWarpTo = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextTravelerAction = DateTime.UtcNow;

        public DateTime NextTravelerAction
        {
            get { return _nextTravelerAction; }
            set
            {
                _nextTravelerAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextTargetAction = DateTime.UtcNow;

        public DateTime NextTargetAction
        {
            get { return _nextTargetAction; }
            set
            {
                _nextTargetAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextWeaponAction = DateTime.UtcNow;
        private DateTime _nextReload = DateTime.UtcNow;

        public DateTime NextReload
        {
            get { return _nextReload; }
            set
            {
                _nextReload = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        public DateTime NextWeaponAction
        {
            get { return _nextWeaponAction; }
            set
            {
                _nextWeaponAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextWebAction = DateTime.UtcNow;

        public DateTime NextWebAction
        {
            get { return _nextWebAction; }
            set
            {
                _nextWebAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextNosAction = DateTime.UtcNow;

        public DateTime NextNosAction
        {
            get { return _nextNosAction; }
            set
            {
                _nextNosAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextPainterAction = DateTime.UtcNow;

        public DateTime NextPainterAction
        {
            get { return _nextPainterAction; }
            set
            {
                _nextPainterAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextActivateAction = DateTime.UtcNow;

        public DateTime NextActivateAction
        {
            get { return _nextActivateAction; }
            set
            {
                _nextActivateAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextBookmarkPocketAttempt = DateTime.UtcNow;

        public DateTime NextBookmarkPocketAttempt
        {
            get { return _nextBookmarkPocketAttempt; }
            set
            {
                _nextBookmarkPocketAttempt = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextAlign = DateTime.UtcNow;

        public DateTime NextAlign
        {
            get { return _nextAlign; }
            set
            {
                _nextAlign = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextUndockAction = DateTime.UtcNow;

        public DateTime NextUndockAction
        {
            get { return _nextUndockAction; }
            set
            {
                _nextUndockAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextDockAction = DateTime.UtcNow; //unused

        public DateTime NextDockAction
        {
            get { return _nextDockAction; }
            set
            {
                _nextDockAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextDroneRecall;

        public DateTime NextDroneRecall
        {
            get { return _nextDroneRecall; }
            set
            {
                _nextDroneRecall = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextStartupAction;

        public DateTime NextStartupAction
        {
            get { return _nextStartupAction; }
            set
            {
                _nextStartupAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextRepairItemsAction;

        public DateTime NextRepairItemsAction
        {
            get { return _nextRepairItemsAction; }
            set
            {
                _nextRepairItemsAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextRepairDronesAction;

        public DateTime NextRepairDronesAction
        {
            get { return _nextRepairDronesAction; }
            set
            {
                _nextRepairDronesAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }

        private DateTime _nextEVEMemoryManagerAction;

        public DateTime NextEVEMemoryManagerAction
        {
            get { return _nextEVEMemoryManagerAction; }
            set
            {
                _nextEVEMemoryManagerAction = value;
                _lastAction = DateTime.UtcNow;
            }
        }
        public DateTime LastLocalWatchAction = DateTime.UtcNow;
        public DateTime LastWalletCheck = DateTime.UtcNow;
        public DateTime LastScheduleCheck = DateTime.UtcNow;

        public DateTime LastUpdateOfSessionRunningTime;
        public DateTime NextInSpaceorInStation;
        public DateTime NextTimeCheckAction = DateTime.UtcNow;
        public DateTime NextSkillsCheckAction = DateTime.UtcNow;

        public DateTime LastFrame = DateTime.UtcNow;
        public DateTime LastSessionIsReady = DateTime.UtcNow;
        public DateTime LastLogMessage = DateTime.UtcNow;

        public int WrecksThisPocket;
        public int WrecksThisMission;
        public DateTime LastLoggingAction = DateTime.MinValue;

        public DateTime LastSessionChange = DateTime.UtcNow;

        public bool Paused { get; set; }

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
                            _agentName = SwitchAgent;
                            Logging.Log("Cache.CurrentAgent", "[ " + CurrentAgent + " ] AgentID [ " + AgentId + " ]", Logging.White);
                            Cache.Instance.CurrentAgentText = CurrentAgent;
                        }
                        catch (Exception ex)
                        {
                            Logging.Log("Cache", "AgentId", "Unable to get agent details: trying again in a moment [" + ex.Message + "]");
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

        public string SwitchAgent
        {
            get
            {
                AgentsList agent = Settings.Instance.AgentsList.OrderBy(j => j.Priorit).FirstOrDefault(i => DateTime.UtcNow >= i.DeclineTimer);
                if (agent == null)
                {
                    try
                    {
                        agent = Settings.Instance.AgentsList.OrderBy(j => j.Priorit).FirstOrDefault();
                    }
                    catch (Exception ex)
                    {
                        Logging.Log("Cache", "SwitchAgent", "Unable to process agent section of [" + Settings.Instance.SettingsPath + "] make sure you have a valid agent listed! Pausing so you can fix it. [" + ex.Message + "]");
                        Cache.Instance.Paused = true;
                    }
                    AllAgentsStillInDeclineCoolDown = true; //this literally means we have no agents available at the moment (decline timer likely)
                }
                else
                    AllAgentsStillInDeclineCoolDown = false; //this literally means we DO have agents available (at least one agents decline timer has expired and is clear to use)

                if (agent != null) return agent.Name;
                return null;
            }
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
                        Logging.Log("Cache", "AgentId", "Unable to get agent details: trying again in a moment [" + ex.Message + "]");
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
                        Logging.Log("Cache", "Agent", "Unable to process agent section of [" + Settings.Instance.SettingsPath + "] make sure you have a valid agent listed! Pausing so you can fix it. [" + ex.Message + "]");
                        Cache.Instance.Paused = true;
                    }
                    if (_agentId != null) return _agent ?? (_agent = DirectEve.GetAgentById(_agentId.Value));
                }
                return null;
            }
        }

        public IEnumerable<ModuleCache> Modules
        {
            get { return _modules ?? (_modules = DirectEve.Modules.Select(m => new ModuleCache(m)).ToList()); }
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

        public IEnumerable<EntityCache> Containers
        {
            get
            {
                return _containers ?? (_containers = Entities.Where(e =>
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
                return _containers ?? (_containers = Entities.Where(e =>
                           e.IsContainer &&
                          (e.GroupId != (int)Group.Wreck || !e.IsWreckEmpty) &&
                          (e.Name != "Abandoned Container")).ToList());
            }
        }

        public IEnumerable<EntityCache> Wrecks
        {
            get { return _containers ?? (_containers = Entities.Where(e => (e.GroupId == (int)Group.Wreck)).ToList()); }
        }

        public IEnumerable<EntityCache> UnlootedContainers
        {
            get
            {
                return _unlootedContainers ?? (_unlootedContainers = Entities.Where(e =>
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
                return _unlootedWrecksAndSecureCans ?? (_unlootedWrecksAndSecureCans = Entities.Where(e =>
                          (e.GroupId == (int)Group.Wreck || e.GroupId == (int)Group.SecureContainer ||
                           e.GroupId == (int)Group.AuditLogSecureContainer ||
                           e.GroupId == (int)Group.FreightContainer) && !e.IsWreckEmpty).OrderBy(e => e.Distance).
                          ToList());
            }
        }

        public IEnumerable<EntityCache> Targets
        {
            get
            {
                if (_targets == null)
                {
                    _targets = Entities.Where(e => e.IsTarget).ToList();
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
            get { return _targeting ?? (_targeting = Entities.Where(e => e.IsTargeting).ToList()); }
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
            get { return _targetedBy ?? (_targetedBy = Entities.Where(e => e.IsTargetedBy && !e.IsBadIdea).ToList()); }
        }

        public IEnumerable<EntityCache> Aggressed
        {
            get { return _aggressed ?? (_aggressed = Entities.Where(e => e.IsTargetedBy && e.IsAttacking).ToList()); }
        }

        public IEnumerable<EntityCache> Entities
        {
            get
            {
                if (!InSpace)
                {
                    return new List<EntityCache>();
                }

                return _entities ?? (_entities = DirectEve.Entities.Select(e => new EntityCache(e)).Where(e => e.IsValid).ToList());
            }
        }

        public IEnumerable<EntityCache> EntitiesNotSelf
        {
            get
            {
                if (!InSpace)
                    return new List<EntityCache>();

                return Cache.Instance.Entities.Where(e => e.IsValid && e.Id != DirectEve.ActiveShip.ItemId).ToList();
            }
        }

        public EntityCache MyShip
        {
            get
            {
                if (!InSpace)
                    return null;

                return DirectEve.Entities.Select(e => new EntityCache(e)).FirstOrDefault(e => e.IsValid && e.Id == DirectEve.ActiveShip.ItemId);
            }
        }

        public bool InSpace
        {
            get
            {
                try
                {
                    if (DirectEve.Session.IsInSpace && !DirectEve.Session.IsInStation && DirectEve.Session.IsReady && DirectEve.ActiveShip.Entity != null)
                    {
                        Cache.Instance.LastInSpace = DateTime.UtcNow;
                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    if (Settings.Instance.DebugExceptions) Logging.Log("Cache.InSpace", "if (DirectEve.Session.IsInSpace && !DirectEve.Session.IsInStation && DirectEve.Session.IsReady && DirectEve.ActiveShip.Entity != null) <---must have failed exception was [" + ex.Message + "]", Logging.Teal);
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
                    if (DirectEve.Session.IsInStation && !DirectEve.Session.IsInSpace && DirectEve.Session.IsReady)
                    {
                        Cache.Instance.LastInStation = DateTime.UtcNow;
                        return true;
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
            get { return DirectEve.ActiveShip != null && (DirectEve.ActiveShip.Entity != null && DirectEve.ActiveShip.Entity.Mode == 3); }
        }

        public bool IsOrbiting
        {
            get { return DirectEve.ActiveShip.Entity != null && DirectEve.ActiveShip.Entity.Mode == 4; }
        }

        public bool IsApproaching
        {
            get
            {
                //Logging.Log("Cache.IsApproaching: " + DirectEve.ActiveShip.Entity.Mode.ToString(CultureInfo.InvariantCulture));
                return DirectEve.ActiveShip.Entity != null && DirectEve.ActiveShip.Entity.Mode == 1;
            }
        }

        public bool IsApproachingOrOrbiting
        {
            get { return DirectEve.ActiveShip.Entity != null && (DirectEve.ActiveShip.Entity.Mode == 1 || DirectEve.ActiveShip.Entity.Mode == 4); }
        }

        public IEnumerable<EntityCache> ActiveDrones
        {
            get { return _activeDrones ?? (_activeDrones = DirectEve.ActiveDrones.Select(d => new EntityCache(d)).ToList()); }
        }

        public IEnumerable<EntityCache> Stations
        {
            get { return _stations ?? (_stations = Entities.Where(e => e.CategoryId == (int)CategoryID.Station).ToList()); }
        }

        public EntityCache ClosestStation
        {
            get { return Stations.OrderBy(s => s.Distance).FirstOrDefault() ?? Entities.OrderByDescending(s => s.Distance).FirstOrDefault(); }
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
            get { return _jumpBridges ?? (_jumpBridges = Entities.Where(e => e.GroupId == (int)Group.JumpBridge).ToList()); }
        }

        public IEnumerable<EntityCache> Stargates
        {
            get { return _stargates ?? (_stargates = Entities.Where(e => e.GroupId == (int)Group.Stargate).ToList()); }
        }

        public EntityCache ClosestStargate
        {
            get { return Stargates.OrderBy(s => s.Distance).FirstOrDefault() ?? Entities.OrderByDescending(s => s.Distance).FirstOrDefault(); }
        }

        public EntityCache StargateByName(string locationName)
        {
            {
                return _stargate ?? (_stargate =
                        Cache.Instance.EntitiesByName(locationName).FirstOrDefault(
                            e => e.GroupId == (int)Group.Stargate));
            }
        }

        public IEnumerable<EntityCache> BigObjects
        {
            get
            {
                return _bigObjects ?? (_bigObjects = Entities.Where(e =>
                       e.GroupId == (int)Group.LargeCollidableStructure ||
                       e.GroupId == (int)Group.LargeCollidableObject ||
                       e.GroupId == (int)Group.LargeCollidableShip ||
                       e.CategoryId == (int)CategoryID.Asteroid ||
                       e.GroupId == (int)Group.SpawnContainer &&
                       e.Distance < (double)Distance.DirectionalScannerCloseRange).OrderBy(t => t.Distance).ToList());
            }
        }

        public IEnumerable<EntityCache> AccelerationGates
        {
            get
            {
                return _gates ?? (_gates = Entities.Where(e =>
                       e.GroupId == (int)Group.AccelerationGate &&
                       e.Distance < (double)Distance.OnGridWithMe).OrderBy(t => t.Distance).ToList());
            }
        }

        public IEnumerable<EntityCache> BigObjectsandGates
        {
            get
            {
                return _bigObjectsAndGates ?? (_bigObjectsAndGates = Entities.Where(e =>
                       e.GroupId == (int)Group.LargeCollidableStructure ||
                       e.GroupId == (int)Group.LargeCollidableObject ||
                       e.GroupId == (int)Group.LargeCollidableShip ||
                       e.CategoryId == (int)CategoryID.Asteroid ||
                       e.GroupId == (int)Group.AccelerationGate ||
                       e.GroupId == (int)Group.SpawnContainer &&
                       e.Distance < (double)Distance.DirectionalScannerCloseRange).OrderBy(t => t.Distance).ToList());
            }
        }

        public IEnumerable<EntityCache> Objects
        {
            get
            {
                return _objects ?? (_objects = Entities.Where(e =>
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

        public IEnumerable<EntityCache> PrimaryWeaponPriorityTargets
        {
            get
            {
                _primaryWeaponPriorityTargets.RemoveAll(pt => pt.Entity == null);
                return _primaryWeaponPriorityTargets.OrderBy(pt => pt.PrimaryWeaponPriority).ThenBy(pt => (pt.Entity.ShieldPct + pt.Entity.ArmorPct + pt.Entity.StructurePct)).ThenBy(pt => pt.Entity.Distance).Select(pt => pt.Entity);
            }
        }

        public IEnumerable<EntityCache> DronePriorityTargets
        {
            get
            {
                _dronePriorityTargets.RemoveAll(pt => pt.Entity == null);
                return _dronePriorityTargets.OrderBy(pt => pt.DronePriority).ThenBy(pt => (pt.Entity.ShieldPct + pt.Entity.ArmorPct + pt.Entity.StructurePct)).ThenBy(pt => pt.Entity.Distance).Select(pt => pt.Entity);
            }
        }

        public EntityCache Approaching
        {
            get
            {
                if (_approaching == null)
                {
                    DirectEntity ship = DirectEve.ActiveShip.Entity;
                    if (ship != null && ship.IsValid)
                    {
                        _approaching = EntityById(ship.FollowId);
                    }
                }

                return _approaching != null && _approaching.IsValid ? _approaching : null;
            }
            set { _approaching = value; }
        }

        public List<DirectWindow> Windows
        {
            get
            {
                if (Cache.Instance.InSpace || Cache.Instance.InStation)
                {
                    return _windows ?? (_windows = DirectEve.Windows);
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

        public int BringOptionalMissionItemQuantity { get; private set; }

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

        public DateTime LastInSpace { get; set; }

        public DateTime LastInWarp = DateTime.UtcNow.AddMinutes(5);

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

        public DateTime EnteredCloseQuestor_DateTime;

        public bool DropMode { get; set; }

        public DirectWindow GetWindowByCaption(string caption)
        {
            return Windows.FirstOrDefault(w => w.Caption.Contains(caption));
        }

        public DirectWindow GetWindowByName(string name)
        {
            // Special cases
            if (name == "Local")
            {
                return Windows.FirstOrDefault(w => w.Name.StartsWith("chatchannel_solarsystemid"));
            }

            return Windows.FirstOrDefault(w => w.Name == name);
        }

        /// <summary>
        ///   Return entities by name
        /// </summary>
        /// <param name = "name"></param>
        /// <returns></returns>
        public IEnumerable<EntityCache> EntitiesByName(string name)
        {
            return Entities.Where(e => e.Name.ToLower() == name.ToLower()).ToList();
        }

        /// <summary>
        ///   Return entity by name
        /// </summary>
        /// <param name = "name"></param>
        /// <returns></returns>
        public EntityCache EntityByName(string name)
        {
            return Entities.FirstOrDefault(e => System.String.Compare(e.Name, name, System.StringComparison.OrdinalIgnoreCase) == 0);
        }

        public IEnumerable<EntityCache> EntitiesByNamePart(string name)
        {
            return Entities.Where(e => e.Name.ToLower().Contains(name.ToLower())).ToList();
        }

        /// <summary>
        ///   Return entities that contain the name
        /// </summary>
        /// <returns></returns>
        public IEnumerable<EntityCache> EntitiesThatContainTheName(string label)
        {
            return Entities.Where(e => !string.IsNullOrEmpty(e.Name) && e.Name.ToLower().Contains(label.ToLower())).ToList();
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

            EntityCache entity = Entities.FirstOrDefault(e => e.Id == id);
            _entitiesById[id] = entity;
            return entity;
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
            return DirectEve.Bookmarks.FirstOrDefault(b => b.BookmarkId == bookmarkId);
        }

        /// <summary>
        ///   Returns bookmarks that start with the supplied label
        /// </summary>
        /// <param name = "label"></param>
        /// <returns></returns>
        public List<DirectBookmark> BookmarksByLabel(string label)
        {
            // Does not seems to refresh the Corporate Bookmark list so it's having troubles to find Corporate Bookmarks
            return DirectEve.Bookmarks.Where(b => !string.IsNullOrEmpty(b.Title) && b.Title.ToLower().StartsWith(label.ToLower())).OrderBy(f => f.LocationId).ToList();
        }

        /// <summary>
        ///   Returns bookmarks that contain the supplied label anywhere in the title
        /// </summary>
        /// <param name = "label"></param>
        /// <returns></returns>
        public List<DirectBookmark> BookmarksThatContain(string label)
        {
            return DirectEve.Bookmarks.Where(b => !string.IsNullOrEmpty(b.Title) && b.Title.ToLower().Contains(label.ToLower())).ToList();
        }

        /// <summary>
        ///   Invalidate the cached items
        /// </summary>
        public void InvalidateCache()
        {
            //
            // this list of variables is cleared every pulse.
            //
            _activeDrones = null;
            _agent = null;
            _aggressed = null;
            _approaching = null;
            _activeDrones = null;
            _bigObjects = null;
            _bigObjectsAndGates = null;
            _containers = null;
            _entities = null;
            _entitiesById.Clear();
            _gates = null;
            _IDsinInventoryTree = null;
            _modules = null;
            _objects = null;
            _primaryWeaponPriorityTargets.ForEach(pt => pt.ClearCache());
            _dronePriorityTargets.ForEach(pt => pt.ClearCache());
            _star = null;
            _stations = null;
            _stargates = null;
            _targets = null;
            _targeting = null;
            _targetedBy = null;
            _unlootedContainers = null;
            _windows = null;
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

                                var actions = new List<Actions.Action>();
                                XElement elements = pocket.Element("actions");
                                if (elements != null)
                                {
                                    foreach (XElement element in elements.Elements("action"))
                                    {
                                        var action = new Actions.Action
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
                    // This will always fail for courier missions, can we detect those and supress these log messages?
                    //
                    Logging.Log("Cache.SetmissionXmlPath","[" + Cache.Instance.MissionXmlPath +"] not found.", Logging.White);
                    Cache.Instance.MissionXmlPath = System.IO.Path.Combine(Settings.Instance.MissionsPath, FilterPath(missionName) + ".xml");
                    if (!File.Exists(Cache.Instance.MissionXmlPath))
                    {
                        Logging.Log("Cache.SetmissionXmlPath", "[" + Cache.Instance.MissionXmlPath + "] not found", Logging.White);
                    }

                    if (File.Exists(Cache.Instance.MissionXmlPath))
                    {
                        Logging.Log("Cache.SetmissionXmlPath", "[" + Cache.Instance.MissionXmlPath + "] found!", Logging.White);
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
                        var missionFit = missionFitting.Fitting;
                        var missionShip = missionFitting.Ship;
                        if (!(missionFit == "" && missionShip != "")) // if we've both specified a mission specific ship and a fitting, then apply that fitting to the ship
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
                    BringMissionItemQuantity = (int?)xdoc.Root.Element("bringquantity") ?? 1;
                    BringMissionItemQuantity = BringMissionItemQuantity;
                    BringOptionalMissionItem = (string)xdoc.Root.Element("trytobring") ?? string.Empty;
                    BringOptionalMissionItem = BringOptionalMissionItem.ToLower();
                    BringOptionalMissionItemQuantity = (int?)xdoc.Root.Element("trytobringquantity") ?? 1;
                    BringOptionalMissionItemQuantity = BringOptionalMissionItemQuantity;
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
            int removed = 0;
            if (targets.Any())
            {
                removed = _primaryWeaponPriorityTargets.RemoveAll(pt => targets.Any(t => t.Id == pt.EntityID));
            }
            return removed > 0;
        }

        /// <summary>
        ///   Remove target from priority list
        /// </summary>
        /// <param name = "targetToRemove"></param>
        public bool RemovePrimaryWeaponPriorityTarget(PriorityTarget targetToRemove)
        {
            try
            {
                _primaryWeaponPriorityTargets.Remove(targetToRemove);
            }
            catch (Exception)
            {
                Logging.Log("Cache.RemovePrimaryWeaponPriorityTargets","Unable to remove [" + targetToRemove.Entity.Name + "] from the _primaryWeaponPriorityTargets list, was it already removed?",Logging.Teal);
                return true;  //(should we return false here?!) - if we did questor would hang...
            }

            return true;
        }

        /// <summary>
        ///   Remove targets from priority list
        /// </summary>
        /// <param name = "targets"></param>
        public bool RemoveDronePriorityTargets(IEnumerable<EntityCache> targets)
        {
            int removed = 0;
            if (targets.Any())
            {
                removed = _dronePriorityTargets.RemoveAll(pt => targets.Any(t => t.Id == pt.EntityID));
            }
            return removed > 0;
        }

        /// <summary>
        ///   Add PrimaryWeapon priority targets
        /// </summary>
        /// <param name = "targets"></param>
        /// <param name = "priority"></param>
        public void AddPrimaryWeaponPriorityTargets(IEnumerable<EntityCache> targets, PrimaryWeaponPriority priority, string module)
        {
            foreach (EntityCache target in targets)
            {
                if (Cache.Instance.IgnoreTargets.Contains(target.Name.Trim()) || _primaryWeaponPriorityTargets.Any(pwpt => pwpt.EntityID == target.Id || (_dronePriorityTargets.Any(dpt => dpt.EntityID == target.Id && Statistics.Instance.OutOfDronesCount == 0))))
                {
                    continue;
                }      
                        
                Logging.Log(module, "Adding [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "] as a primary weapon priority target", Logging.Teal);
                _primaryWeaponPriorityTargets.Add(new PriorityTarget { EntityID = target.Id, PrimaryWeaponPriority = priority });
                if (Statistics.Instance.OutOfDronesCount == 0 && Cache.Instance.UseDrones)
                {
                    Logging.Log(module, "Adding [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "] as a drone priority target", Logging.Teal);
                    _dronePriorityTargets.Add(new PriorityTarget { EntityID = target.Id, DronePriority = DronePriority.PriorityKillTarget });
                }

                continue;
            }

            return;
        }

        /// <summary>
        ///   Add Drone priority targets
        /// </summary>
        /// <param name = "targets"></param>
        /// <param name = "priority"></param>
        /// <param name = "module"></param>
        public void AddDronePriorityTargets(IEnumerable<EntityCache> targets, DronePriority priority, string module)
        {
            foreach (EntityCache target in targets)
            {
                if (_dronePriorityTargets.Any(pt => pt.EntityID == target.Id))
                {
                    continue;
                }

                Logging.Log(module, "Adding [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "] as a drone priority target", Logging.Teal);
                _dronePriorityTargets.Add(new PriorityTarget { EntityID = target.Id, DronePriority = priority });
            }

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
            if (DirectEve.ActiveShip.Entity == null)
            {
                return double.MaxValue;
            }

            double curX = DirectEve.ActiveShip.Entity.X;
            double curY = DirectEve.ActiveShip.Entity.Y;
            double curZ = DirectEve.ActiveShip.Entity.Z;

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
        ///   Create a bookmark
        /// </summary>
        /// <param name = "label"></param>
        public void CreateBookmark(string label)
        {
            if (Cache.Instance.AfterMissionSalvageBookmarks.Count() < 100)
            {
                if (Settings.Instance.CreateSalvageBookmarksIn.ToLower() == "corp".ToLower())
                {
                    DirectEve.CorpBookmarkCurrentLocation(label, "", null);
                }
                else
                {
                    DirectEve.BookmarkCurrentLocation(label, "", null);
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
            DirectContainer cargo = Cache.Instance.DirectEve.GetShipsCargo();
            DirectItem item = cargo.Items.FirstOrDefault(i => i.TypeId == typeIdToFind && i.Quantity >= quantityToFind);
            return item;
        }

        public bool CheckifRouteIsAllHighSec()
        {
            Cache.Instance.RouteIsAllHighSecBool = false;
            // Find the first waypoint
            List<long> currentPath = DirectEve.Navigation.GetDestinationPath();
            if (currentPath == null || !currentPath.Any()) return false;
            if (currentPath[0] == 0) return false; //No destination set - prevents exception if somehow we have got an invalid destination

            for (int i = currentPath.Count - 1; i >= 0; i--)
            {
                DirectSolarSystem solarSystemInRoute = Cache.Instance.DirectEve.SolarSystems[currentPath[i]];
                if (solarSystemInRoute.Security < 0.45)
                {
                    //Bad bad bad
                    Cache.Instance.RouteIsAllHighSecBool = false;
                    return true;
                }
            }
            Cache.Instance.RouteIsAllHighSecBool = true;
            return true;
        }

        public string MaskedID(long ID)
        {
            int numofCharacters = ID.ToString(CultureInfo.InvariantCulture).Length;
            string maskedID = ID.ToString(CultureInfo.InvariantCulture).Substring(numofCharacters - 5);
            maskedID = "[truncatedID]" + maskedID;
            return maskedID;
        }

        /// <summary>
        ///   Return the best possible target (based on current target, distance and low value first)
        /// </summary>
        /// <param name="currentTarget"></param>
        /// <param name="distance"></param>
        /// <param name="lowValueFirst"></param>
        /// <param name="callingroutine"> </param>
        /// <returns></returns>
        public EntityCache GetBestTarget(EntityCache currentTarget, double distance, bool lowValueFirst, string callingroutine)
        {
            // Do we have a 'current target' and if so, is it an actual target?
            // If not, clear current target
            if (currentTarget != null && !currentTarget.IsTarget)
            {
                currentTarget = null;
            }

            //
            // Is our current target too close and too fast to hit with our main weapons? If so, skip it and shoot something we CAN hit please.
            // we purposely do not check for ANY priority targets here, including warp scramblers, if their are any let the drones handle it
            //
            
            foreach (PriorityTarget PrimaryWeaponPriorityTarget in Cache.Instance._primaryWeaponPriorityTargets)
            {
                if (PrimaryWeaponPriorityTarget.Entity == null || PrimaryWeaponPriorityTarget.Entity.Distance > 250000)
                {
                    continue;
                }

                //Logging.Log("ListPriorityTargets", "[" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away] WARP[" + target.IsWarpScramblingMe + "] ECM[" + target.IsJammingMe + "] Damp[" + target.IsSensorDampeningMe + "] TP[" + target.IsTargetPaintingMe + "] NEUT[" + target.IsNeutralizingMe + "]", Logging.Teal);
                if (PrimaryWeaponPriorityTarget.Entity.Distance < Settings.Instance.DistanceNPCFrigatesShouldBeIgnoredByPrimaryWeapons && 
                    PrimaryWeaponPriorityTarget.Entity.Velocity > Settings.Instance.SpeedNPCFrigatesShouldBeIgnoredByPrimaryWeapons && 
                    PrimaryWeaponPriorityTarget.Entity.IsNPCFrigate && 
                    Cache.Instance.UseDrones &&
                    Statistics.Instance.OutOfDronesCount == 0)
                {
                    Logging.Log("Cache.GetBesttarget", "[" + PrimaryWeaponPriorityTarget.Entity.Name + "] was removed from the PrimaryWeaponsPriorityTarget list because it was inside [" + Math.Round(PrimaryWeaponPriorityTarget.Entity.Distance, 0) + "] meters and going [" + Math.Round(PrimaryWeaponPriorityTarget.Entity.Velocity, 0 ) + "] meters per second", Logging.Red);
                    Cache.Instance.RemovePrimaryWeaponPriorityTarget(PrimaryWeaponPriorityTarget);
                    break;
                }
                
                continue;
            }


            if (currentTarget != null && (callingroutine == "Drones" || Settings.Instance.ShootWarpScramblersWithPrimaryWeapons && callingroutine == "Combat" ))
            {
                // Is our current target a warp scrambling priority target?
                if (PrimaryWeaponPriorityTargets.Any(pt => pt.Id == currentTarget.Id && pt.IsWarpScramblingMe && pt.IsTarget) ||
                    DronePriorityTargets.Any(pt => pt.Id == currentTarget.Id && pt.IsWarpScramblingMe && pt.IsTarget)
                    )
                {
                    return currentTarget;
                }

                // Get the closest warp scrambling priority target
                EntityCache warpscramblingtarget = DronePriorityTargets.OrderBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault(pt => pt.Distance < distance && pt.IsWarpScramblingMe && pt.IsTarget);
                if (warpscramblingtarget != null)
                {
                    return warpscramblingtarget;
                }    
            }

            if (Settings.Instance.SpeedTank) //all webbers have to be relatively close so processing them all is ok
            {
                // Is our current target a webbing priority target?
                if (currentTarget != null && !Cache.Instance.IgnoreTargets.Contains(currentTarget.Name.Trim()) && DronePriorityTargets.Any(pt => pt.Id == currentTarget.Id && pt.IsWebbingMe && pt.IsTarget))
                {
                    return currentTarget;
                }

                // Get the closest webbing priority target frigate
                EntityCache webbingtarget = DronePriorityTargets.OrderBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault(pt => pt.Distance < distance && pt.IsWebbingMe && pt.IsNPCFrigate && pt.IsTarget); //frigates
                if (webbingtarget != null && !Cache.Instance.IgnoreTargets.Contains(webbingtarget.Name.Trim()))
                {
                    return webbingtarget;
                }

                // Get the closest webbing priority target cruiser
                webbingtarget = DronePriorityTargets.OrderBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault(pt => pt.Distance < distance && pt.IsWebbingMe && pt.IsNPCCruiser && pt.IsTarget); //cruisers
                if (webbingtarget != null && !Cache.Instance.IgnoreTargets.Contains(webbingtarget.Name.Trim()))
                {
                    return webbingtarget;
                }

                // Get the closest webbing priority target (anything else)
                webbingtarget = DronePriorityTargets.OrderBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault(pt => pt.Distance < distance && pt.IsWebbingMe && pt.IsTarget); //everything else
                if (webbingtarget != null && !Cache.Instance.IgnoreTargets.Contains(webbingtarget.Name.Trim()))
                {
                    return webbingtarget;
                }
            }

            // Is our current target any other primary weapon priority target?
            if (currentTarget != null && callingroutine == "Combat" && !Cache.Instance.IgnoreTargets.Contains(currentTarget.Name.Trim()) && PrimaryWeaponPriorityTargets.Any(pt => pt.Id == currentTarget.Id))
            {
                return currentTarget;
            }

            // Is our current target any other drone priority target?
            if (currentTarget != null && callingroutine == "Drones" && !Cache.Instance.IgnoreTargets.Contains(currentTarget.Name.Trim()) && DronePriorityTargets.Any(pt => pt.Id == currentTarget.Id))
            {
                return currentTarget;
            }

            bool currentTargetHealthLogNow = true;
            if (Settings.Instance.DetailedCurrentTargetHealthLogging)
            {
                if (currentTarget != null && (int)currentTarget.Id != (int)TargetingCache.CurrentTargetID)
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
                            Logging.Log(callingroutine, ".GetBestTarget: CurrentTarget is [" + currentTarget.Name +                              //name
                                        "][" + (Math.Round(currentTarget.Distance / 1000, 0)).ToString(CultureInfo.InvariantCulture) +           //distance
                                        "k][Shield%:[" + Math.Round(currentTarget.ShieldPct * 100, 0).ToString(CultureInfo.InvariantCulture) +   //shields
                                        "][Armor%:[" + Math.Round(currentTarget.ArmorPct * 100, 0).ToString(CultureInfo.InvariantCulture) + "]" //armor
                                        , Logging.White);
                        }
                    }
                }
            }

            // Is our current target already in armor? keep shooting the same target if so...
            if (currentTarget != null && currentTarget.ArmorPct * 100 < Settings.Instance.DoNotSwitchTargetsIfTargetHasMoreThanThisArmorDamagePercentage && !Cache.Instance.IgnoreTargets.Contains(currentTarget.Name.Trim()))
            {
                //Logging.Log(callingroutine + ".GetBestTarget: CurrentTarget has less than 60% armor, keep killing this target");
                return currentTarget;
            }

            // Get the closest primary weapon priority target
            EntityCache primaryWeaponPriorityTarget = PrimaryWeaponPriorityTargets.OrderBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault(pt => pt.Distance < distance && pt.IsTarget);
            if (primaryWeaponPriorityTarget != null && callingroutine == "Combat" && !Cache.Instance.IgnoreTargets.Contains(primaryWeaponPriorityTarget.Name.Trim()))
            {
                return primaryWeaponPriorityTarget;
            }

            // Get the closest drone priority target
            EntityCache dronePriorityTarget = DronePriorityTargets.OrderBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault(pt => pt.Distance < distance && pt.IsTarget);
            if (dronePriorityTarget != null && callingroutine == "Drones" && !Cache.Instance.IgnoreTargets.Contains(dronePriorityTarget.Name.Trim()))
            {
                return dronePriorityTarget;
            }

            // Do we have a target?
            if (currentTarget != null)
            {
                return currentTarget;
            }

            // Get all entity targets
            IEnumerable<EntityCache> targets = Targets.Where(e => e.CategoryId == (int)CategoryID.Entity && e.IsNpc && !e.IsContainer && !e.IsFactionWarfareNPC && !e.IsEntityIShouldLeaveAlone && !e.IsBadIdea && e.GroupId != (int)Group.LargeCollidableStructure).ToList();

            EWarEffectsOnMe(); //updates data that is displayed in the Questor GUI (and possibly used elsewhere later)

            // Get the closest high value target
            EntityCache highValueTarget = targets.Where(t => t.TargetValue.HasValue && t.Distance < distance).OrderByDescending(t => t.TargetValue != null ? t.TargetValue.Value : 0).ThenBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault();

            // Get the closest low value target
            EntityCache lowValueTarget = targets.Where(t => !t.TargetValue.HasValue && t.Distance < distance).OrderBy(OrderByLowestHealth()).ThenBy(t => t.Distance).FirstOrDefault();

            if (lowValueFirst && lowValueTarget != null)
            {
                return lowValueTarget;
            }

            if (!lowValueFirst && highValueTarget != null)
            {
                return highValueTarget;
            }

            // Return either one or the other
            return lowValueTarget ?? highValueTarget;
        }

        private void EWarEffectsOnMe()
        {
            // Get all entity targets
            IEnumerable<EntityCache> targets = Targets.Where(e => e.CategoryId == (int)CategoryID.Entity && e.IsNpc && !e.IsContainer && e.GroupId != (int)Group.LargeCollidableStructure).ToList();

            //
            //Start of Current EWar Effects On Me (below)
            //
            //Dampening
            TargetingCache.EntitiesDampeningMe = targets.Where(e => e.IsSensorDampeningMe).ToList();
            TargetingCache.EntitiesDampeningMeText = String.Empty;
            foreach (EntityCache entityDampeningMe in TargetingCache.EntitiesDampeningMe)
            {
                TargetingCache.EntitiesDampeningMeText = TargetingCache.EntitiesDampeningMeText + " [" +
                                                          entityDampeningMe.Name + "][" +
                                                          Math.Round(entityDampeningMe.Distance / 1000, 0) +
                                                          "k] , ";
            }

            //Neutralizing
            TargetingCache.EntitiesNeutralizingMe = targets.Where(e => e.IsNeutralizingMe).ToList();
            TargetingCache.EntitiesNeutralizingMeText = String.Empty;
            foreach (EntityCache entityNeutralizingMe in TargetingCache.EntitiesNeutralizingMe)
            {
                TargetingCache.EntitiesNeutralizingMeText = TargetingCache.EntitiesNeutralizingMeText + " [" +
                                                             entityNeutralizingMe.Name + "][" +
                                                             Math.Round(entityNeutralizingMe.Distance / 1000, 0) +
                                                             "k] , ";
            }

            //TargetPainting
            TargetingCache.EntitiesTargetPatingingMe = targets.Where(e => e.IsTargetPaintingMe).ToList();
            TargetingCache.EntitiesTargetPaintingMeText = String.Empty;
            foreach (EntityCache entityTargetpaintingMe in TargetingCache.EntitiesTargetPatingingMe)
            {
                TargetingCache.EntitiesTargetPaintingMeText = TargetingCache.EntitiesTargetPaintingMeText + " [" +
                                                               entityTargetpaintingMe.Name + "][" +
                                                               Math.Round(entityTargetpaintingMe.Distance / 1000, 0) +
                                                               "k] , ";
            }

            //TrackingDisrupting
            TargetingCache.EntitiesTrackingDisruptingMe = targets.Where(e => e.IsTrackingDisruptingMe).ToList();
            TargetingCache.EntitiesTrackingDisruptingMeText = String.Empty;
            foreach (EntityCache entityTrackingDisruptingMe in TargetingCache.EntitiesTrackingDisruptingMe)
            {
                TargetingCache.EntitiesTrackingDisruptingMeText = TargetingCache.EntitiesTrackingDisruptingMeText +
                                                                   " [" + entityTrackingDisruptingMe.Name + "][" +
                                                                   Math.Round(entityTrackingDisruptingMe.Distance / 1000, 0) +
                                                                   "k] , ";
            }

            //Jamming (ECM)
            TargetingCache.EntitiesJammingMe = targets.Where(e => e.IsJammingMe).ToList();
            TargetingCache.EntitiesJammingMeText = String.Empty;
            foreach (EntityCache entityJammingMe in TargetingCache.EntitiesJammingMe)
            {
                TargetingCache.EntitiesJammingMeText = TargetingCache.EntitiesJammingMeText + " [" +
                                                        entityJammingMe.Name + "][" +
                                                        Math.Round(entityJammingMe.Distance / 1000, 0) +
                                                        "k] , ";
            }

            //Warp Disrupting (and warp scrambling)
            TargetingCache.EntitiesWarpDisruptingMe = targets.Where(e => e.IsWarpScramblingMe).ToList();
            TargetingCache.EntitiesWarpDisruptingMeText = String.Empty;
            foreach (EntityCache entityWarpDisruptingMe in TargetingCache.EntitiesWarpDisruptingMe)
            {
                TargetingCache.EntitiesWarpDisruptingMeText = TargetingCache.EntitiesWarpDisruptingMeText + " [" +
                                                               entityWarpDisruptingMe.Name + "][" +
                                                               Math.Round(entityWarpDisruptingMe.Distance / 1000, 0) +
                                                               "k] , ";
            }

            //Webbing
            TargetingCache.EntitiesWebbingMe = targets.Where(e => e.IsWebbingMe).ToList();
            TargetingCache.EntitiesWebbingMeText = String.Empty;
            foreach (EntityCache entityWebbingMe in TargetingCache.EntitiesWebbingMe)
            {
                TargetingCache.EntitiesWebbingMeText = TargetingCache.EntitiesWebbingMeText + " [" +
                                                        entityWebbingMe.Name + "][" +
                                                        Math.Round(entityWebbingMe.Distance / 1000, 0) +
                                                        "k] , ";
            }

            //
            //End of Current EWar Effects On Me (above)
            //
        }

        public int RandomNumber(int min, int max)
        {
            var random = new Random();
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

        public DirectContainer ItemHangar { get; set; }

        public bool ReadyItemsHangarSingleInstance(String module)
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
                DirectContainerWindow lootHangarWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.StationItems") && w.Caption.Contains("Item hangar"));

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

        public bool OpenItemsHangar(String module)
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
                    if (Settings.Instance.DebugHangars) Logging.Log("ReadyItemsHangar", "We are in Station", Logging.Teal);
                    Cache.Instance.ItemHangar = Cache.Instance.DirectEve.GetItemHangar();
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenInventory);
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("OpenItemsHangar", "Unable to complete OpenItemsHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool CloseItemsHangar(String module)
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

        public bool ReadyItemsHangarAsLootHangar(String module)
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
                    Cache.Instance.LootHangar = Cache.Instance.DirectEve.GetItemHangar();
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

        public bool ReadyItemsHangarAsAmmoHangar(String module)
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
                    Cache.Instance.AmmoHangar = Cache.Instance.DirectEve.GetItemHangar();
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

        public bool StackItemsHangarAsLootHangar(String module)
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

        public bool StackItemsHangarAsAmmoHangar(String module)
        {
            Logging.Log("StackItemsHangarAsAmmoHangar", "test", Logging.Teal);

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

        public DirectContainer CargoHold { get; set; }

        public bool OpenCargoHold(String module)
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

                Cache.Instance.CargoHold = null;
                Cache.Instance.CargoHold = Cache.Instance.DirectEve.GetShipsCargo();

                if (Cache.Instance.InStation || Cache.Instance.InSpace) //do we need to special case pods here?
                {
                    if (Cache.Instance.CargoHold.Window == null)
                    {
                        // No, command it to open
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenCargoHoldOfActiveShip);
                        Cache.Instance.NextOpenCargoAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 4));
                        Logging.Log(module, "Opening Cargohold of active ship: waiting [" + Math.Round(Cache.Instance.NextOpenCargoAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                        return false;
                    }

                    if (!Cache.Instance.CargoHold.Window.IsReady)
                    {
                        //Logging.Log(module, "cargo window is not ready", Logging.White);
                        return false;
                    }

                    if (!Cache.Instance.CargoHold.Window.IsPrimary())
                    {
                        if (Settings.Instance.DebugCargoHold) Logging.Log(module, "DebugHangars: cargoHold window is ready and is a secondary inventory window", Logging.DebugHangars);
                        return true;
                    }

                    if (Cache.Instance.CargoHold.Window.IsPrimary())
                    {
                        if (Settings.Instance.DebugCargoHold) Logging.Log(module, "DebugHangars:Opening cargoHold window as secondary", Logging.DebugHangars);
                        Cache.Instance.CargoHold.Window.OpenAsSecondary();
                        Cache.Instance.NextOpenCargoAction = DateTime.UtcNow.AddMilliseconds(1000 + Cache.Instance.RandomNumber(0, 2000));
                        return false;
                    }

                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyCargoHold", "Unable to complete ReadyCargoHold [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool ReadyCargoHold(String module)
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

                Cache.Instance.CargoHold = null;
                Cache.Instance.CargoHold = Cache.Instance.DirectEve.GetShipsCargo();
                return true;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyCargoHold", "Unable to complete ReadyCargoHold [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackCargoHold(String module)
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
                if (Cache.Instance.CargoHold != null)
                {
                    try
                    {
                        Cache.Instance.LastStackCargohold = DateTime.UtcNow;
                        Cache.Instance.CargoHold.StackAll();
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

        public bool CloseCargoHold(String module)
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

                Cache.Instance.CargoHold = Cache.Instance.DirectEve.GetShipsCargo();
                if (Cache.Instance.InStation || Cache.Instance.InSpace) //do we need to special case pods here?
                {
                    if (Cache.Instance.CargoHold.Window == null)
                    {
                        Logging.Log(module, "Cargohold is closed", Logging.White);
                        return true;
                    }

                    if (!Cache.Instance.CargoHold.Window.IsReady)
                    {
                        //Logging.Log(module, "cargo window is not ready", Logging.White);
                        return false;
                    }

                    if (Cache.Instance.CargoHold.Window.IsReady)
                    {
                        Cache.Instance.CargoHold.Window.Close();
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

        public bool OpenShipsHangar(String module)
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

        public bool ReadyShipsHangar(String module)
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
                    Cache.Instance.ShipHangar = Cache.Instance.DirectEve.GetShipHangar();
                    if (Cache.Instance.ShipHangar == null)
                    {
                        Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddMilliseconds(500);
                        return false;
                    }

                    // Is the ShipHangar ready to be used?
                    if (Cache.Instance.ShipHangar != null && Cache.Instance.ShipHangar.IsValid)
                    {
                        //if (Cache.Instance.ShipHangar.Items.Any())
                        //{
                            //if (!OpenInventoryWindow("Cache.ReadyShipsHangar")) return false;

                            //Logging.Log("ReadyShipHangar","Ship Hangar is ready to be used (no window needed)",Logging.White);
                            return true;
                        //}
                        //return false;
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyShipsHangar", "Unable to complete ReadyShipsHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackShipsHangar(String module)
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

        public bool CloseShipsHangar(String module)
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
                    if (Settings.Instance.AmmoHangar != null)
                    {
                        CorpHangarName = Settings.Instance.AmmoHangar;
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
                        Cache.Instance.AmmoHangarID = Cache.Instance.DirectEve.GetCorpHangarId(Settings.Instance.AmmoHangar); //- 1;
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
                    if (Settings.Instance.LootHangar != null)
                    {
                        CorpHangarName = Settings.Instance.LootHangar;
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
                        Cache.Instance.LootHangarID = Cache.Instance.DirectEve.GetCorpHangarId(Settings.Instance.LootHangar);  //- 1;
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

        public bool ReadyCorpAmmoHangar(String module)
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
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar)) //do we have the corp hangar setting setup?
                    {
                        if (!Cache.Instance.CloseLootHangar("OpenCorpAmmoHangar")) return false;
                        if (!Cache.Instance.GetCorpAmmoHangarID()) return false;

                        if (Cache.Instance.AmmoHangar != null && Cache.Instance.AmmoHangar.IsValid) //do we have a corp hangar tab setup with that name?
                        {
                            if (Settings.Instance.DebugHangars) Logging.Log(module, "AmmoHangar is defined (no window needed)", Logging.DebugHangars);
                            int AmmoHangarItemCount = -1;
                            try
                            {
                                if (AmmoHangar.Items.Any())
                                {
                                    AmmoHangarItemCount = AmmoHangar.Items.Count();
                                    if (Settings.Instance.DebugHangars) Logging.Log(module, "AmmoHangar [" + Settings.Instance.AmmoHangar.ToString() + "] has [" + AmmoHangarItemCount + "] items", Logging.DebugHangars);
                                }
                            }
                            catch (Exception exception)
                            {
                                Logging.Log("ReadyCorpAmmoHangar", "Exception [" + exception + "]", Logging.Debug);
                            }
                            
                            return true;
                        }

                        if (Cache.Instance.AmmoHangar == null)
                        {
                            if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
                                Logging.Log(module, "Opening Corporate Ammo Hangar: failed! No Corporate Hangar in this station! lag?", Logging.Orange);
                            return false;
                        }

                        if (Settings.Instance.DebugHangars) Logging.Log(module, "LootHangar is not yet ready. waiting...", Logging.DebugHangars);
                        return false;
                    }

                    Cache.Instance.AmmoHangar = null;
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyCorpAmmoHangar", "Unable to complete ReadyCorpAmmoHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackCorpAmmoHangar(String module)
        {
            if (DateTime.UtcNow.AddMinutes(10) < Cache.Instance.LastStackAmmoHangar)
            {
                return true;
            }

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return false;

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
                return false;

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

                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackAmmoHangar).TotalSeconds > 20)
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
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
                    {
                        if (!Cache.Instance.ReadyCorpAmmoHangar(module)) return false;

                        if (AmmoHangar != null && AmmoHangar.IsValid)
                        {
                            try
                            {
                                if (Cache.Instance.StackAmmoHangarAttempts <= 2)
                                {
                                    Cache.Instance.StackAmmoHangarAttempts++;
                                    Logging.Log(module, "Stacking Corporate Ammo Hangar", Logging.White);
                                    Cache.Instance.AmmoHangar.StackAll();
                                    Cache.Instance.LastStackAmmoHangar = DateTime.UtcNow;
                                    Cache.Instance.StackAmmoHangarAttempts = 0; //this resets the counter every time the above stackall completes without an exception
                                    Cache.Instance.LastStackAmmoHangar = DateTime.UtcNow;
                                    return true;
                                }

                                Logging.Log(module, "Not Stacking AmmoHangar [" + Settings.Instance.AmmoHangar + "]", Logging.White);
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

        public bool OpenInventoryWindow(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            Cache.Instance.PrimaryInventoryWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Name.Contains("Inventory"));

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

        public bool ReadyCorpLootHangar(String module)
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
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangar)) //do we have the corp hangar setting setup?
                    {
                        if (!Cache.Instance.CloseAmmoHangar("OpenCorpLootHangar")) return false;
                        if (!Cache.Instance.GetCorpLootHangarID()) return false;

                        if (Cache.Instance.LootHangar != null && Cache.Instance.LootHangar.IsValid) //do we have a corp hangar tab setup with that name?
                        {
                            if (Settings.Instance.DebugHangars) Logging.Log(module, "LootHangar is defined (no window needed)", Logging.DebugHangars);
                            return true;
                        }

                        if (Cache.Instance.LootHangar == null)
                        {
                            if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                            {
                                Logging.Log(module, "Opening Corporate Loot Hangar: failed! No Corporate Hangar in this station! lag?", Logging.Orange);
                            }

                            return false;
                        }

                        if (Settings.Instance.DebugHangars) Logging.Log(module, "LootHangar is not yet ready. waiting...", Logging.DebugHangars);
                        return false;
                    }

                    Cache.Instance.LootHangar = null;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyCorpLootHangar", "Unable to complete ReadyCorpLootHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackCorpLootHangar(String module)
        {
            if (DateTime.UtcNow.Subtract(Cache.Instance.LastStackLootHangar).TotalMinutes < 10)
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

                    return false;
                }

                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                    {
                        if (!Cache.Instance.ReadyCorpLootHangar("Cache.StackCorpLootHangar")) return false;

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

                                Logging.Log(module, "Not Stacking AmmoHangar [" + Settings.Instance.AmmoHangar + "]", Logging.White);
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

        public bool SortCorpLootHangar(String module)
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
                if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                {
                    if (!Cache.Instance.ReadyCorpLootHangar("Cache.StackCorpLootHangar")) return false;

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
        public bool OpenCorpBookmarkHangar(String module)
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

        public bool CloseCorpHangar(String module, String window)
        {
            try
            {
                if (Cache.Instance.InStation && !String.IsNullOrEmpty(window))
                {
                    DirectContainerWindow corpHangarWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption == window);

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

        public bool ClosePrimaryInventoryWindow(String module)
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

        //public DirectContainer LootContainer { get; set; }

        public bool ReadyLootContainer(String module)
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
                if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenLootContainer", "Debug: if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                    if (!Cache.Instance.OpenItemsHangar(module)) return false;

                    DirectItem firstLootContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer && i.GivenName.ToLower() == Settings.Instance.LootContainer.ToLower());
                    if (firstLootContainer != null)
                    {
                        long lootContainerID = firstLootContainer.ItemId;
                        Cache.Instance.LootHangar = Cache.Instance.DirectEve.GetContainer(lootContainerID);

                        if (Cache.Instance.LootHangar != null && Cache.Instance.LootHangar.IsValid)
                        {
                            if (Settings.Instance.DebugHangars) Logging.Log(module, "LootHangar is defined (no window needed)", Logging.DebugHangars);
                            return true;
                        }

                        if (Cache.Instance.LootHangar == null)
                        {
                            if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                            {
                                Logging.Log(module, "Opening Corporate Loot Hangar: failed! No Corporate Hangar in this station! lag?", Logging.Orange);
                            }

                            return false;
                        }

                        if (Settings.Instance.DebugHangars) Logging.Log(module, "AmmoHangar is not yet ready. waiting...", Logging.DebugHangars);
                        return false;
                    }

                    Logging.Log(module, "unable to find LootContainer named [ " + Settings.Instance.LootContainer.ToLower() + " ]", Logging.Orange);
                    var firstOtherContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer);

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

        public bool ReadyHighTierLootContainer(String module)
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
                if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))
                {
                    if (Settings.Instance.DebugHangars) Logging.Log("OpenLootContainer", "Debug: if (!string.IsNullOrEmpty(Settings.Instance.HighTierLootContainer))", Logging.Teal);
                    if (!Cache.Instance.ReadyLootHangar(module)) return false;

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
                            if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                                Logging.Log(module, "Opening HighTierLootContainer: failed! lag?", Logging.Orange);
                            return false;
                        }

                        if (Settings.Instance.DebugHangars) Logging.Log(module, "HighTierLootContainer is not yet ready. waiting...", Logging.DebugHangars);
                        return false;
                    }

                    Logging.Log(module, "unable to find HighTierLootContainer named [ " + Settings.Instance.HighTierLootContainer.ToLower() + " ]", Logging.Orange);
                    var firstOtherContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer);

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

        public bool StackHighTierLootContainer(String module)
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
                    var firstLootContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer && i.GivenName.ToLower() == Settings.Instance.HighTierLootContainer.ToLower());
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

            Cache.Instance.PrimaryInventoryWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Name.Contains("Inventory"));

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

            Cache.Instance.PrimaryInventoryWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Name.Contains("Inventory"));

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

        public bool StackLootContainer(String module)
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
                if (!Cache.Instance.ReadyLootContainer("Cache.StackLootContainer")) return false;
                Cache.Instance.NextOpenLootContainerAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(3, 5));
                if (LootHangar.Window == null)
                {
                    var firstLootContainer = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.GivenName != null && i.IsSingleton && i.GroupId == (int)Group.FreightContainer && i.GivenName.ToLower() == Settings.Instance.LootContainer.ToLower());
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

                if (LootHangar.Window == null || !LootHangar.Window.IsReady) return false;

                Logging.Log(module, "Loot Container window named: [ " + LootHangar.Window.Name + " ] was found and its contents are being stacked", Logging.White);
                LootHangar.StackAll();
                Cache.Instance.LastStackLootContainer = DateTime.UtcNow;
                Cache.Instance.LastStackLootHangar = DateTime.UtcNow;
                Cache.Instance.NextOpenLootContainerAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                return true;
            }

            return false;
        }

        public bool CloseLootContainer(String module)
        {
            if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))
            {
                if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpLootHangar", "Debug: else if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                DirectContainerWindow lootHangarWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.Inventory") && w.Caption == Settings.Instance.LootContainer);

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

        public bool OpenOreHold(String module)
        {
            if (DateTime.Now < Cache.Instance.NextOpenHangarAction) return false;

            if (!Cache.Instance.OpenInventoryWindow("OpenOreHold")) return false;

            //
            // does the current ship have an ore hold?
            //
            Cache.Instance.OreHoldWindow = Cache.Instance.PrimaryInventoryWindow;

            if (Cache.Instance.OreHoldWindow == null)
            {
                // No, command it to open
                Cache.Instance.NextOpenHangarAction = DateTime.UtcNow.AddSeconds(2 + Cache.Instance.RandomNumber(1, 3));
                Logging.Log(module, "Opening Ore Hangar: waiting [" + Math.Round(Cache.Instance.NextOpenHangarAction.Subtract(DateTime.Now).TotalSeconds, 0) + "sec]", Logging.White);
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

        public DirectContainer LootHangar { get; set; }

        public DirectContainer HighTierLootContainer { get; set; }

        public bool CloseLootHangar(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                    {
                        Cache.Instance.LootHangar = Cache.Instance.DirectEve.GetCorporationHangar(Settings.Instance.LootHangar);

                        // Is the corp loot Hangar open?
                        if (Cache.Instance.LootHangar != null)
                        {
                            Cache.Instance.corpLootHangarSecondaryWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption.Contains(Settings.Instance.LootHangar));
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
                            if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                            {
                                Logging.Log(module, "Closing Corporate Hangar: failed! No Corporate Hangar in this station! lag or setting misconfiguration?", Logging.Orange);
                                return true;
                            }
                            return false;
                        }
                    }
                    else if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpLootHangar", "Debug: else if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                        DirectContainerWindow lootHangarWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption.Contains(Settings.Instance.LootContainer));

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

        public bool ReadyLootHangar(String module)
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
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangar)) // Corporate hangar = LootHangar
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log(module, "using Corporate hangar as Loot hangar", Logging.White);
                        if (!Cache.Instance.ReadyCorpLootHangar(module)) return false;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(Settings.Instance.LootContainer)) // Freight Container in my local items hangar = LootHangar
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log(module, "using Loot Container in Local Items hangar as Loot hangar", Logging.White);
                        if (!Cache.Instance.ReadyItemsHangarAsLootHangar(module)) return false;
                        if (!Cache.Instance.ReadyLootContainer(module)) return false;
                        return true;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log(module, "using Local items hangar as Loot hangar", Logging.White);
                    if (!Cache.Instance.ReadyItemsHangarAsLootHangar(module)) return false;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyLootHangar", "Unable to complete ReadyLootHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackLootHangar(String module)
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
                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))", Logging.Teal);
                        if (!Cache.Instance.StackCorpLootHangar("Cache.StackLootHangar")) return false;
                        StackLoothangarAttempts = 0;
                        return true;
                    }

                    if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "if (!string.IsNullOrEmpty(Settings.Instance.LootContainer))", Logging.Teal);
                        if (!Cache.Instance.StackLootContainer("Cache.StackLootHangar")) return false;
                        StackLoothangarAttempts = 0;
                        return true;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackLootHangar", "!Cache.Instance.StackItemsHangarAsLootHangar(Cache.StackLootHangar))", Logging.Teal);
                    if (!Cache.Instance.StackItemsHangarAsLootHangar("Cache.StackLootHangar")) return false;
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

        public bool SortLootHangar(String module)
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
                if (!Cache.Instance.ReadyLootHangar("Cache.SortLootHangar")) return false;

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

        public DirectContainer AmmoHangar { get; set; }

        public bool ReadyAmmoHangar(String module)
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
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log(module, "using Corporate hangar as Ammo hangar", Logging.White);
                        if (!Cache.Instance.ReadyCorpAmmoHangar(module)) return false;
                        return true;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log(module, "using Local items hangar as Ammo hangar", Logging.White);
                    if (!Cache.Instance.ReadyItemsHangarAsAmmoHangar(module)) return false;
                    return true;
                }

                return false;
            }
            catch (Exception exception)
            {
                Logging.Log("ReadyAmmoHangar", "Unable to complete ReadyAmmoHangar [" + exception + "]", Logging.Teal);
                return false;
            }
        }

        public bool StackAmmoHangar(String module)
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
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Starting [Cache.Instance.StackCorpAmmoHangar]", Logging.Teal);
                        if (!Cache.Instance.StackCorpAmmoHangar(module)) return false;
                        if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Finished [Cache.Instance.StackCorpAmmoHangar]", Logging.Teal);
                        return true;
                    }

                    if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Starting [Cache.Instance.StackItemsHangarAsAmmoHangar]", Logging.Teal);
                    if (!Cache.Instance.StackItemsHangarAsAmmoHangar(module)) return false;
                    if (Settings.Instance.DebugHangars) Logging.Log("StackAmmoHangar", "Finished [Cache.Instance.StackItemsHangarAsAmmoHangar]", Logging.Teal);
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

        public bool CloseAmmoHangar(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            try
            {
                if (Cache.Instance.InStation)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
                    {
                        if (Settings.Instance.DebugHangars) Logging.Log("CloseCorpAmmoHangar", "Debug: if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))", Logging.Teal);

                        if (Cache.Instance.AmmoHangar == null)
                        {
                            Cache.Instance.AmmoHangar = Cache.Instance.DirectEve.GetCorporationHangar(Settings.Instance.AmmoHangar);
                        }

                        // Is the corp Ammo Hangar open?
                        if (Cache.Instance.AmmoHangar != null)
                        {
                            Cache.Instance.corpAmmoHangarSecondaryWindow = (DirectContainerWindow)Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.Type.Contains("form.InventorySecondary") && w.Caption.Contains(Settings.Instance.AmmoHangar));
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
                            if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
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

        public bool OpenDroneBay(String module)
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

                //if(Cache.Instance.DirectEve.ActiveShip.Entity == null || Cache.Instance.DirectEve.ActiveShip.GroupId == 31)
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

                if (GetShipsDroneBayAttempts > 10) //we her havent located a dronebay in over 10 attempts, we are not going to
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

        public bool CloseDroneBay(String module)
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

        public DirectLoyaltyPointStoreWindow LPStore { get; set; }

        public bool OpenLPStore(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                //Logging.Log(module + ": Opening Drone Bay: waiting [" + Math.Round(Cache.Instance.NextOpenDroneBayAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]",Logging.White);
                return false;
            }

            if (!Cache.Instance.InStation)
            {
                Logging.Log(module, "Opening LP Store: We are not in station?! There is no LP Store in space, waiting...", Logging.Orange);
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.LPStore = Cache.Instance.DirectEve.Windows.OfType<DirectLoyaltyPointStoreWindow>().FirstOrDefault();
                if (Cache.Instance.LPStore == null)
                {
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenLpstore);
                    Logging.Log(module, "Opening loyalty point store", Logging.White);
                    return false;
                }

                return true;
            }

            return false;
        }

        public bool CloseLPStore(String module)
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
                Cache.Instance.LPStore = Cache.Instance.DirectEve.Windows.OfType<DirectLoyaltyPointStoreWindow>().FirstOrDefault();
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

        public DirectFittingManagerWindow FittingManagerWindow { get; set; }

        public bool OpenFittingManagerWindow(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                //Logging.Log(module + ": Opening Drone Bay: waiting [" + Math.Round(Cache.Instance.NextOpenDroneBayAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]",Logging.White);
                return false;
            }

            if (!Cache.Instance.InStation)
            {
                Logging.Log(module, "Opening Fitting Window: We are not in station?! We can't refit with the fitting window in space, waiting...", Logging.Orange);
                Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(10);
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.FittingManagerWindow = Cache.Instance.DirectEve.Windows.OfType<DirectFittingManagerWindow>().FirstOrDefault();

                //open it again ?
                if (Cache.Instance.FittingManagerWindow == null)
                {
                    //Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenFitting);
                    Cache.Instance.DirectEve.OpenFitingManager(); //you should only have to issue this command once
                    Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(1, 3));
                    Logging.Log(module, "Opening fitting manager: waiting [" + Math.Round(Cache.Instance.NextArmAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);
                    return false;
                }

                if (Cache.Instance.FittingManagerWindow != null && (Cache.Instance.FittingManagerWindow.IsReady)) //check if it's ready
                {
                    Logging.Log(module, "Fitting Manager is ready.", Logging.White);
                    return true;
                }

                return false;
            }

            return false;
        }

        public bool CloseFittingManager(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.NextOpenHangarAction)
            {
                return false;
            }

            Cache.Instance.FittingManagerWindow = Cache.Instance.DirectEve.Windows.OfType<DirectFittingManagerWindow>().FirstOrDefault();
            if (Cache.Instance.FittingManagerWindow != null)
            {
                Logging.Log(module, "Closing Fitting Manager Window", Logging.White);
                Cache.Instance.FittingManagerWindow.Close();
                return false;
            }

            return true;
        }

        public DirectWindow AgentWindow { get; set; }

        public bool OpenAgentWindow(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextAgentWindowAction)
            {
                if (Settings.Instance.DebugAgentInteractionReplyToAgent) Logging.Log(module, "if (DateTime.UtcNow < Cache.Instance.NextAgentWindowAction)", Logging.Yellow);
                return false;
            }

            if (AgentInteraction.Agent.Window == null)
            {
                if (Settings.Instance.DebugAgentInteractionReplyToAgent) Logging.Log(module, "Attempting to Interact with the agent named [" + AgentInteraction.Agent.Name + "] in [" + Cache.Instance.DirectEve.GetLocationName(AgentInteraction.Agent.SolarSystemId) + "]", Logging.Yellow);
                Cache.Instance.NextAgentWindowAction = DateTime.UtcNow.AddSeconds(10);
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

        public bool OpenJournalWindow(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenJournalWindowAction)
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
                    Cache.Instance.NextOpenJournalWindowAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(2, 4));
                    Logging.Log(module, "Opening Journal Window: waiting [" +
                                Math.Round(Cache.Instance.NextOpenJournalWindowAction.Subtract(DateTime.UtcNow).TotalSeconds,
                                           0) + "sec]", Logging.White);
                    return false;
                }

                return true; //if JournalWindow is not null then the window must be open.
            }

            return false;
        }

        public DirectMarketWindow MarketWindow { get; set; }

        public bool OpenMarket(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenMarketAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.MarketWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketWindow>().FirstOrDefault();
                
                // Is the Market window open?
                if (Cache.Instance.MarketWindow == null)
                {
                    // No, command it to open
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenMarket);
                    Cache.Instance.NextOpenMarketAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(2, 4));
                    Logging.Log(module, "Opening Market Window: waiting [" +
                                Math.Round(Cache.Instance.NextOpenJournalWindowAction.Subtract(DateTime.UtcNow).TotalSeconds,
                                           0) + "sec]", Logging.White);
                    return false;
                }

                return true; //if MarketWindow is not null then the window must be open.
            }

            return false;
        }

        public bool CloseMarket(String module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20) && !Cache.Instance.InSpace) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextOpenMarketAction)
            {
                return false;
            }

            if (Cache.Instance.InStation)
            {
                Cache.Instance.MarketWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketWindow>().FirstOrDefault();

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

        public DirectContainer ContainerInSpace { get; set; }

        public bool OpenContainerInSpace(String module, EntityCache containerToOpen)
        {
            if (DateTime.UtcNow < Cache.Instance.NextLootAction)
            {
                return false;
            }

            if (Cache.Instance.InSpace && containerToOpen.Distance <= (int)Distance.ScoopRange)
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

        public List<DirectBookmark> AfterMissionSalvageBookmarks
        {
            get
            {
                if (_States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior)
                {
                    return Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ").Where(e => e.CreatedOn != null && e.CreatedOn.Value.CompareTo(AgedDate) < 0).ToList();
                }

                return Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ").ToList();
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
                if (Settings.Instance.FirstSalvageBookmarksInSystem)
                {
                    Logging.Log("CombatMissionsBehavior.BeginAftermissionSalvaging", "Salvaging at first bookmark from system", Logging.White);
                    return Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ").OrderBy(b => b.CreatedOn).FirstOrDefault(c => c.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId);
                }

                Logging.Log("CombatMissionsBehavior.BeginAftermissionSalvaging", "Salvaging at first oldest bookmarks", Logging.White);
                return Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ").OrderBy(b => b.CreatedOn).FirstOrDefault();
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
            try
            {
                //Delete bookmarks older than 2 hours.
                DateTime bmExpirationDate = DateTime.UtcNow.AddMinutes(-Settings.Instance.AgeofSalvageBookmarksToExpire);
                var uselessSalvageBookmarks = new List<DirectBookmark>(AfterMissionSalvageBookmarks.Where(e => e.CreatedOn != null && e.CreatedOn.Value.CompareTo(bmExpirationDate) < 0).ToList());

                DirectBookmark uselessSalvageBookmark = uselessSalvageBookmarks.FirstOrDefault();
                if (uselessSalvageBookmark != null)
                {
                    _bookmarkDeletionAttempt++;
                    if (_bookmarkDeletionAttempt <= 5)
                    {
                        Logging.Log(module, "removing salvage bookmark that aged more than [" + Settings.Instance.AgeofSalvageBookmarksToExpire + "]" + uselessSalvageBookmark.Title, Logging.White);
                        uselessSalvageBookmark.Delete();
                        return false;
                    }

                    if (_bookmarkDeletionAttempt > 5)
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

            var bookmarksInLocal = new List<DirectBookmark>(AfterMissionSalvageBookmarks.Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId).
                                                                   OrderBy(b => b.CreatedOn));
            DirectBookmark onGridBookmark = bookmarksInLocal.FirstOrDefault(b => Cache.Instance.DistanceFromMe(b.X ?? 0, b.Y ?? 0, b.Z ?? 0) < (int)Distance.OnGridWithMe);
            if (onGridBookmark != null)
            {
                _bookmarkDeletionAttempt++;
                if (_bookmarkDeletionAttempt <= 5)
                {
                    Logging.Log(module, "removing salvage bookmark:" + onGridBookmark.Title, Logging.White);
                    onGridBookmark.Delete();
                    return false;
                }

                if (_bookmarkDeletionAttempt > 5)
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

        public bool RepairItems(string module)
        {
            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(5) && !Cache.Instance.InSpace || DateTime.UtcNow < NextRepairItemsAction) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
            {
                //Logging.Log(module, "Waiting...", Logging.Orange);
                return false;
            }

            NextRepairItemsAction = DateTime.UtcNow.AddSeconds(Settings.Instance.RandomNumber(2, 4));

            if (Cache.Instance.InStation && !Cache.Instance.DirectEve.hasRepairFacility())
            {
                Logging.Log(module, "This station does not have repair facilities to use! aborting attempt to use non-existant repair facility.", Logging.Orange);
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
                if (!Cache.Instance.OpenItemsHangar(module)) return false;
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
                Logging.Log(module, "This station does not have repair facilities to use! aborting attempt to use non-existant repair facility.", Logging.Orange);
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