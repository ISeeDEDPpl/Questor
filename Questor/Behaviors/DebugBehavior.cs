// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DirectEve;
using Questor.Modules.Caching;
using Questor.Modules.Logging;
using Questor.Modules.Lookup;
using Questor.Modules.Activities;
using Questor.Modules.States;
using Questor.Modules.Combat;
using Questor.Modules.Actions;
using Questor.Modules.BackgroundTasks;

namespace Questor.Behaviors
{
    public class DebugBehavior
    {
        private readonly Arm _arm;
        //private readonly Combat _combat;
        //private readonly Drones _drones;

        private readonly Panic _panic;
        private readonly Salvage _salvage;
        private readonly UnloadLoot _unloadLoot;
        public DateTime LastAction;

        public static long AgentID;

        private readonly Stopwatch _watch;

        public bool PanicStateReset; //false;

        private bool ValidSettings { get; set; }

        public bool CloseQuestorFlag = true;

        public string CharacterName { get; set; }

        public DebugBehavior()
        {
            _salvage = new Salvage();
            //_combat = new Combat();
            //_drones = new Drones();
            _unloadLoot = new UnloadLoot();
            _arm = new Arm();
            _panic = new Panic();
            _watch = new Stopwatch();

            //
            // this is combat mission specific and needs to be generalized
            //
            Settings.Instance.SettingsLoaded += SettingsLoaded;

            _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;
            _States.CurrentArmState = ArmState.Idle;
            _States.CurrentDroneState = DroneState.Idle;
            _States.CurrentUnloadLootState = UnloadLootState.Idle;
            _States.CurrentTravelerState = TravelerState.Idle;
        }

        public void SettingsLoaded(object sender, EventArgs e)
        {
            ApplyDebugSettings();
            ValidateCombatMissionSettings();
        }

        public void DebugDebugBehaviorStates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("DebugBehavior.State is", _States.CurrentDebugBehaviorState.ToString(), Logging.White);
        }

        public void DebugPanicstates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("Panic.State is ", _States.CurrentPanicState.ToString(), Logging.White);
        }

        public void DebugPerformanceClearandStartTimer()
        {
            _watch.Reset();
            _watch.Start();
        }

        public void DebugPerformanceStopandDisplayTimer(string whatWeAreTiming)
        {
            _watch.Stop();
            if (Settings.Instance.DebugPerformance)
                Logging.Log(whatWeAreTiming, " took " + _watch.ElapsedMilliseconds + "ms", Logging.White);
        }

        public void ValidateCombatMissionSettings()
        {
            ValidSettings = true;
            if (Settings.Instance.Ammo.Select(a => a.DamageType).Distinct().Count() != 4)
            {
                if (Settings.Instance.Ammo.All(a => a.DamageType != DamageType.EM))
                    Logging.Log("Settings", ": Missing EM damage type!", Logging.Orange);
                if (Settings.Instance.Ammo.All(a => a.DamageType != DamageType.Thermal))
                    Logging.Log("Settings", "Missing Thermal damage type!", Logging.Orange);
                if (Settings.Instance.Ammo.All(a => a.DamageType != DamageType.Kinetic))
                    Logging.Log("Settings", "Missing Kinetic damage type!", Logging.Orange);
                if (Settings.Instance.Ammo.All(a => a.DamageType != DamageType.Explosive))
                    Logging.Log("Settings", "Missing Explosive damage type!", Logging.Orange);

                Logging.Log("Settings", "You are required to specify all 4 damage types in your settings xml file!", Logging.White);
                ValidSettings = false;
            }

            DirectAgent agent = Cache.Instance.DirectEve.GetAgentByName(Cache.Instance.CurrentAgent);

            if (agent == null || !agent.IsValid)
            {
                Logging.Log("Settings", "Unable to locate agent [" + Cache.Instance.CurrentAgent + "]", Logging.White);
                ValidSettings = false;
            }
            else
            {
                _arm.AgentId = agent.AgentId;
                AgentID = agent.AgentId;
            }
        }

        public void ApplyDebugSettings()
        {
            _salvage.Ammo = Settings.Instance.Ammo;
            _salvage.MaximumWreckTargets = Settings.Instance.MaximumWreckTargets;
            _salvage.ReserveCargoCapacity = Settings.Instance.ReserveCargoCapacity;
            _salvage.LootEverything = Settings.Instance.LootEverything;
        }

        private void BeginClosingQuestor()
        {
            Cache.Instance.EnteredCloseQuestor_DateTime = DateTime.UtcNow;
            _States.CurrentQuestorState = QuestorState.CloseQuestor;
        }

        private void AvoidBumpingThings()
        {
            //if It has not been at least 60 seconds since we last session changed do not do anything
            if (Cache.Instance.InStation || !Cache.Instance.InSpace || Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || (Cache.Instance.InSpace && Cache.Instance.LastSessionChange.AddSeconds(60) < DateTime.UtcNow))
                return;
            //
            // if we are "too close" to the bigObject move away... (is orbit the best thing to do here?)
            //
            if (Cache.Instance.ClosestStargate.Distance > 9000 || Cache.Instance.ClosestStation.Distance > 5000)
            {
                EntityCache thisBigObject = Cache.Instance.BigObjects.FirstOrDefault();
                if (thisBigObject != null)
                {
                    if (thisBigObject.Distance >= (int)Distance.TooCloseToStructure)
                    {
                        //we are no longer "too close" and can proceed.
                    }
                    else
                    {
                        if (DateTime.UtcNow > Cache.Instance.NextOrbit)
                        {
                            thisBigObject.Orbit((int)Distance.SafeDistancefromStructure);
                            Logging.Log("DebugBehavior", _States.CurrentDebugBehaviorState +
                                        ": initiating Orbit of [" + thisBigObject.Name +
                                        "] orbiting at [" + Distance.SafeDistancefromStructure + "]", Logging.White);
                            Cache.Instance.NextOrbit = DateTime.UtcNow.AddSeconds(Time.Instance.OrbitDelay_seconds);
                        }
                        return;
                        //we are still too close, do not continue through the rest until we are not "too close" anymore
                    }
                }
            }
        }

        public void ProcessState()
        {
            // Invalid settings, quit while we're ahead
            if (!ValidSettings)
            {
                if (DateTime.UtcNow.Subtract(LastAction).TotalSeconds < Time.Instance.ValidateSettings_seconds) //default is a 15 second interval
                {
                    ValidateCombatMissionSettings();
                    LastAction = DateTime.UtcNow;
                }
                return;
            }

            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            //this local is safe check is useless as their is no localwatch processstate running every tick...
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            //If local unsafe go to base and do not start mission again
            if (Settings.Instance.FinishWhenNotSafe && (_States.CurrentDebugBehaviorState != DebugBehaviorState.GotoNearestStation /*|| State!=QuestorState.GotoBase*/))
            {
                //need to remove spam
                if (Cache.Instance.InSpace && !Cache.Instance.LocalSafe(Settings.Instance.LocalBadStandingPilotsToTolerate, Settings.Instance.LocalBadStandingLevelToConsiderBad))
                {
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        Logging.Log("Local not safe", "Station found. Going to nearest station", Logging.White);
                        if (_States.CurrentDebugBehaviorState != DebugBehaviorState.GotoNearestStation)
                            _States.CurrentDebugBehaviorState = DebugBehaviorState.GotoNearestStation;
                    }
                    else
                    {
                        Logging.Log("Local not safe", "Station not found. Going back to base", Logging.White);
                        if (_States.CurrentDebugBehaviorState != DebugBehaviorState.GotoBase)
                            _States.CurrentDebugBehaviorState = DebugBehaviorState.GotoBase;
                    }
                    Cache.Instance.StopBot = true;
                }
            }

            if (Cache.Instance.SessionState == "Quitting")
            {
                BeginClosingQuestor();
            }

            if (Cache.Instance.GotoBaseNow)
            {
                if (_States.CurrentDebugBehaviorState != DebugBehaviorState.GotoBase)
                {
                    _States.CurrentDebugBehaviorState = DebugBehaviorState.GotoBase;
                }
            }
            if ((DateTime.UtcNow.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalSeconds > 10) && (DateTime.UtcNow.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalSeconds < 60))
            {
                if (Cache.Instance.QuestorJustStarted)
                {
                    Cache.Instance.QuestorJustStarted = false;
                    Cache.Instance.SessionState = "Starting Up";

                    // write session log
                    Statistics.WriteSessionLogStarting();
                }
            }

            //
            // Panic always runs, not just in space
            //
            DebugPerformanceClearandStartTimer();
            _panic.ProcessState();
            DebugPerformanceStopandDisplayTimer("Panic.ProcessState");
            if (_States.CurrentPanicState == PanicState.Panic || _States.CurrentPanicState == PanicState.Panicking)
            {
                // If Panic is in panic state, questor is in panic States.CurrentDebugBehaviorState :)
                _States.CurrentDebugBehaviorState = DebugBehaviorState.Panic;

                DebugDebugBehaviorStates();
                if (PanicStateReset)
                {
                    _States.CurrentPanicState = PanicState.Normal;
                    PanicStateReset = false;
                }
            }
            else if (_States.CurrentPanicState == PanicState.Resume)
            {
                // Reset panic state
                _States.CurrentPanicState = PanicState.Normal;

                // Sit Idle and wait for orders.
                _States.CurrentTravelerState = TravelerState.Idle;
                _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;

                DebugDebugBehaviorStates();
            }
            DebugPanicstates();

            //Logging.Log("test");
            switch (_States.CurrentDebugBehaviorState)
            {
                case DebugBehaviorState.Idle:

                    if (Cache.Instance.StopBot)
                    {
                        if (Settings.Instance.DebugIdle) Logging.Log("DebugBehavior", "if (Cache.Instance.StopBot)", Logging.White);
                        return;
                    }

                    if (Settings.Instance.DebugIdle) Logging.Log("DebugBehavior", "if (Cache.Instance.InSpace) else", Logging.White);
                    _States.CurrentArmState = ArmState.Idle;
                    _States.CurrentDroneState = DroneState.Idle;
                    _States.CurrentSalvageState = SalvageState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;
                    _States.CurrentUnloadLootState = UnloadLootState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;

                    Logging.Log("DebugBehavior", "Started questor in Debug mode", Logging.White);
                    LastAction = DateTime.UtcNow;
                    break;

                case DebugBehaviorState.DelayedGotoBase:
                    if (DateTime.UtcNow.Subtract(LastAction).TotalSeconds < Time.Instance.DelayedGotoBase_seconds)
                        break;

                    Logging.Log("DebugBehavior", "Heading back to base", Logging.White);
                    if (_States.CurrentDebugBehaviorState == DebugBehaviorState.DelayedGotoBase) _States.CurrentDebugBehaviorState = DebugBehaviorState.GotoBase;
                    break;

                case DebugBehaviorState.Arm:
                    //
                    // only used when someone manually selects the arm state.
                    //
                    if (_States.CurrentArmState == ArmState.Idle)
                    {
                        Logging.Log("Arm", "Begin", Logging.White);
                        _States.CurrentArmState = ArmState.Begin;

                        // Load right ammo based on mission
                        _arm.AmmoToLoad.Clear();
                        _arm.LoadSpecificAmmo(new[] { Cache.Instance.DamageType });
                    }

                    _arm.ProcessState();

                    if (Settings.Instance.DebugStates) Logging.Log("Arm.State", "is" + _States.CurrentArmState, Logging.White);

                    if (_States.CurrentArmState == ArmState.NotEnoughAmmo)
                    {
                        // we know we are connected if we were able to arm the ship - update the lastknownGoodConnectedTime
                        // we may be out of drones/ammo but disconnecting/reconnecting will not fix that so update the timestamp
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        Logging.Log("Arm", "Armstate.NotEnoughAmmo", Logging.Orange);
                        _States.CurrentArmState = ArmState.Idle;
                        if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Arm) _States.CurrentDebugBehaviorState = DebugBehaviorState.Error;
                    }

                    if (_States.CurrentArmState == ArmState.NotEnoughDrones)
                    {
                        // we know we are connected if we were able to arm the ship - update the lastknownGoodConnectedTime
                        // we may be out of drones/ammo but disconnecting/reconnecting will not fix that so update the timestamp
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        Logging.Log("Arm", "Armstate.NotEnoughDrones", Logging.Orange);
                        _States.CurrentArmState = ArmState.Idle;
                        if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Arm) _States.CurrentDebugBehaviorState = DebugBehaviorState.Error;
                    }

                    if (_States.CurrentArmState == ArmState.Done)
                    {
                        //we know we are connected if we were able to arm the ship - update the lastknownGoodConnectedTime
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        _States.CurrentArmState = ArmState.Idle;
                        _States.CurrentDroneState = DroneState.WaitingForTargets;

                        if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Arm) _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;
                    }
                    break;

                case DebugBehaviorState.Salvage:
                    if (!Cache.Instance.InSpace)
                        return;

                    DirectContainer salvageCargo = Cache.Instance.DirectEve.GetShipsCargo();
                    Cache.Instance.SalvageAll = true;
                    Cache.Instance.OpenWrecks = true;

                    if (!Cache.Instance.OpenCargoHold("CombatMissionsBehavior: Salvage")) break;

                    if (Settings.Instance.UnloadLootAtStation && salvageCargo.Window.IsReady && (salvageCargo.Capacity - salvageCargo.UsedCapacity) < 100)
                    {
                        Logging.Log("CombatMissionsBehavior.Salvage", "We are full, go to base to unload", Logging.White);
                        if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Salvage)
                        {
                            _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.GotoBase;
                        }
                        break;
                    }

                    if (!Cache.Instance.UnlootedContainers.Any())
                    {
                        break;
                    }
                    //we __cannot ever__ approach in salvage.cs so this section _is_ needed.
                    Salvage.MoveIntoRangeOfWrecks();
                    try
                    {
                        // Overwrite settings, as the 'normal' settings do not apply
                        _salvage.MaximumWreckTargets = Math.Min(Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets, Cache.Instance.DirectEve.Me.MaxLockedTargets);
                        _salvage.ReserveCargoCapacity = 80;
                        _salvage.LootEverything = true;
                        _salvage.ProcessState();
                        //Logging.Log("number of max cache ship: " + Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets);
                        //Logging.Log("number of max cache me: " + Cache.Instance.DirectEve.Me.MaxLockedTargets);
                        //Logging.Log("number of max math.min: " + _salvage.MaximumWreckTargets);
                    }
                    finally
                    {
                        ApplyDebugSettings();
                    }
                    break;

                case DebugBehaviorState.GotoBase:
                    if (Settings.Instance.DebugGotobase) Logging.Log("DebugBehavior", "GotoBase: AvoidBumpingThings()", Logging.White);

                    AvoidBumpingThings();

                    if (Settings.Instance.DebugGotobase) Logging.Log("DebugBehavior", "GotoBase: TravelToAgentsStation()", Logging.White);

                    Traveler.TravelHome("DebugBehavior.TravelHome");

                    if (_States.CurrentTravelerState == TravelerState.AtDestination) // || DateTime.UtcNow.Subtract(Cache.Instance.EnteredCloseQuestor_DateTime).TotalMinutes > 10)
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log("DebugBehavior", "GotoBase: We are at destination", Logging.White);
                        Cache.Instance.GotoBaseNow = false; //we are there - turn off the 'forced' GoToBase
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);

                        if (_States.CurrentDebugBehaviorState == DebugBehaviorState.GotoBase) _States.CurrentDebugBehaviorState = DebugBehaviorState.UnloadLoot;

                        Traveler.Destination = null;
                    }
                    break;

                case DebugBehaviorState.UnloadLoot:
                    if (_States.CurrentUnloadLootState == UnloadLootState.Idle)
                    {
                        Logging.Log("DebugBehavior", "UnloadLoot: Begin", Logging.White);
                        _States.CurrentUnloadLootState = UnloadLootState.Begin;
                    }

                    _unloadLoot.ProcessState();

                    if (Settings.Instance.DebugStates)
                        Logging.Log("DebugBehavior", "UnloadLoot.State is " + _States.CurrentUnloadLootState, Logging.White);

                    if (_States.CurrentUnloadLootState == UnloadLootState.Done)
                    {
                        Cache.Instance.LootAlreadyUnloaded = true;
                        _States.CurrentUnloadLootState = UnloadLootState.Idle;
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);
                        if (_States.CurrentCombatState == CombatState.OutOfAmmo) // on mission
                        {
                            Logging.Log("DebugBehavior.UnloadLoot", "We are out of ammo", Logging.Orange);
                            _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;
                            return;
                        }

                        _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;
                        Logging.Log("DebugBehavior.Unloadloot", "CharacterMode: [" + Settings.Instance.CharacterMode + "], AfterMissionSalvaging: [" + Settings.Instance.AfterMissionSalvaging + "], DebugBehaviorState: [" + _States.CurrentDebugBehaviorState + "]", Logging.White);
                        Statistics.Instance.FinishedMission = DateTime.UtcNow;
                        return;
                    }
                    break;

                case DebugBehaviorState.Traveler:
                    Cache.Instance.OpenWrecks = false;
                    List<int> destination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();
                    if (destination == null || destination.Count == 0)
                    {
                        // happens if autopilot is not set and this QuestorState is chosen manually
                        // this also happens when we get to destination (!?)
                        Logging.Log("DebugBehavior.Traveler", "No destination?", Logging.White);
                        if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Traveler) _States.CurrentDebugBehaviorState = DebugBehaviorState.Error;
                        return;
                    }

                    if (destination.Count == 1 && destination.FirstOrDefault() == 0)
                    {
                        destination[0] = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;
                    }
                    if (Traveler.Destination == null || Traveler.Destination.SolarSystemId != destination.Last())
                    {
                        IEnumerable<DirectBookmark> bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.LocationId == destination.Last()).ToList();
                        if (bookmarks != null && bookmarks.Any())
                            Traveler.Destination = new BookmarkDestination(bookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault());
                        else
                        {
                            Logging.Log("DebugBehavior.Traveler", "Destination: [" + Cache.Instance.DirectEve.Navigation.GetLocation(destination.Last()).Name + "]", Logging.White);
                            Traveler.Destination = new SolarSystemDestination(destination.Last());
                        }
                    }
                    else
                    {
                        Traveler.ProcessState();

                        //we also assume you are connected during a manual set of questor into travel mode (safe assumption considering someone is at the kb)
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;

                        if (_States.CurrentTravelerState == TravelerState.AtDestination)
                        {
                            if (_States.CurrentCombatMissionCtrlState == CombatMissionCtrlState.Error)
                            {
                                Logging.Log("DebugBehavior.Traveler", "an error has occurred", Logging.White);
                                if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Traveler)
                                {
                                    _States.CurrentDebugBehaviorState = DebugBehaviorState.Error;
                                }
                                return;
                            }
                            else if (Cache.Instance.InSpace)
                            {
                                Logging.Log("DebugBehavior.Traveler", "Arrived at destination (in space, Questor stopped)", Logging.White);
                                if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Traveler) _States.CurrentDebugBehaviorState = DebugBehaviorState.Error;
                                return;
                            }
                            else
                            {
                                Logging.Log("DebugBehavior.Traveler", "Arrived at destination", Logging.White);
                                if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Traveler) _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;
                                return;
                            }
                        }
                    }
                    break;

                case DebugBehaviorState.GotoNearestStation:
                    if (!Cache.Instance.InSpace || Cache.Instance.InWarp) return;
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        if (station.Distance > (int)Distance.WarptoDistance)
                        {
                            Logging.Log("DebugBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                            station.WarpToAndDock();
                            Cache.Instance.NextWarpTo = DateTime.UtcNow.AddSeconds(Time.Instance.WarptoDelay_seconds);
                            if (_States.CurrentDebugBehaviorState == DebugBehaviorState.GotoNearestStation) _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;
                            break;
                        }
                        else
                        {
                            if (station.Distance < 1900)
                            {
                                if (DateTime.UtcNow > Cache.Instance.NextDockAction)
                                {
                                    Logging.Log("DebugBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                    station.Dock();
                                    Cache.Instance.NextDockAction = DateTime.UtcNow.AddSeconds(Time.Instance.DockingDelay_seconds);
                                }
                            }
                            else
                            {
                                if (Cache.Instance.NextApproachAction < DateTime.UtcNow && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != station.Id))
                                {
                                    Cache.Instance.NextApproachAction = DateTime.UtcNow.AddSeconds(Time.Instance.ApproachDelay_seconds);
                                    Logging.Log("DebugBehavior.GotoNearestStation", "Approaching [" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                    station.Approach();
                                }
                            }
                        }
                    }
                    else
                    {
                        if (_States.CurrentDebugBehaviorState == DebugBehaviorState.GotoNearestStation) _States.CurrentDebugBehaviorState = DebugBehaviorState.Error; //should we goto idle here?
                    }
                    break;

                case DebugBehaviorState.LogCombatTargets:
                    //combat targets
                    //List<EntityCache> combatentitiesInList =  Cache.Instance.Entities.Where(t => t.IsNpc && !t.IsBadIdea && t.CategoryId == (int)CategoryID.Entity && !t.IsContainer && t.Distance < Cache.Instance.MaxRange && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).ToList();
                    List<EntityCache> combatentitiesInList = Cache.Instance.Entities.Where(t => t.IsNpc && !t.IsBadIdea && t.CategoryId == (int)CategoryID.Entity && !t.IsContainer).ToList();
                    Statistics.EntityStatistics(combatentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DebugBehaviorState.LogDroneTargets:
                    //drone targets
                    List<EntityCache> droneentitiesInList = Cache.Instance.Entities.Where(e => e.IsNpc && !e.IsBadIdea && e.CategoryId == (int)CategoryID.Entity && !e.IsContainer && !e.IsSentry && e.GroupId != (int)Group.LargeColidableStructure).ToList();
                    Statistics.EntityStatistics(droneentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DebugBehaviorState.LogStationEntities:
                    //stations
                    List<EntityCache> stationsInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.Station).ToList();
                    Statistics.EntityStatistics(stationsInList);
                    Cache.Instance.Paused = true;
                    break;

                case DebugBehaviorState.LogStargateEntities:
                    //stargates
                    List<EntityCache> stargatesInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.Stargate).ToList();
                    Statistics.EntityStatistics(stargatesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DebugBehaviorState.LogAsteroidBelts:
                    //Asteroid Belts
                    List<EntityCache> asteroidbeltsInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.AsteroidBelt).ToList();
                    Statistics.EntityStatistics(asteroidbeltsInList);
                    Cache.Instance.Paused = true;
                    break;

                case DebugBehaviorState.LogCansAndWrecks:
                    //Asteroid Belts
                    List<EntityCache> cansandWrecksInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.CargoContainer && e.GroupId == (int)Group.Wreck).ToList();
                    Statistics.EntityStatistics(cansandWrecksInList);
                    Cache.Instance.Paused = true;
                    break;

                case DebugBehaviorState.Default:
                    if (_States.CurrentDebugBehaviorState == DebugBehaviorState.Default) _States.CurrentDebugBehaviorState = DebugBehaviorState.Idle;
                    break;
            }
        }
    }
}