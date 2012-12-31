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
using Questor.Modules.Combat;
using Questor.Modules.Logging;
using Questor.Modules.Lookup;
using Questor.Modules.Activities;
using Questor.Modules.States;
using Questor.Modules.Actions;
using Questor.Modules.BackgroundTasks;

namespace Questor.Behaviors
{
    public class DebugHangarsBehavior
    {
        private readonly Arm _arm;
        private readonly Combat _combat;
        private readonly Drones _drones;

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

        public DebugHangarsBehavior()
        {
            _salvage = new Salvage();
            _combat = new Combat();
            _drones = new Drones();
            _unloadLoot = new UnloadLoot();
            _arm = new Arm();
            _panic = new Panic();
            _watch = new Stopwatch();

            //
            // this is combat mission specific and needs to be generalized
            //
            Settings.Instance.SettingsLoaded += SettingsLoaded;

            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Idle;
            _States.CurrentArmState = ArmState.Idle;
            _States.CurrentUnloadLootState = UnloadLootState.Idle;
            _States.CurrentTravelerState = TravelerState.Idle;
        }

        public void SettingsLoaded(object sender, EventArgs e)
        {
            ApplyDebugSettings();
            ValidateCombatMissionSettings();
        }

        public void DebugHangarsBehaviorStates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("DebugHangarsBehavior.State is", _States.CurrentDebugHangarBehaviorState.ToString(), Logging.White);
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

        private void TravelToAgentsStation()
        {
            try
            {
                var baseDestination = Traveler.Destination as StationDestination;
                if (baseDestination == null || baseDestination.StationId != Cache.Instance.Agent.StationId)
                    Traveler.Destination = new StationDestination(Cache.Instance.Agent.SolarSystemId,
                                                                   Cache.Instance.Agent.StationId,
                                                                   Cache.Instance.DirectEve.GetLocationName(
                                                                       Cache.Instance.Agent.StationId));
            }
            catch (Exception ex)
            {
                Logging.Log("DebugHangarsBehavior", "TravelToAgentsStation: Exception caught: [" + ex.Message + "]", Logging.Red);
                return;
            }
            if (Cache.Instance.InSpace)
            {
                if (!Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || (Cache.Instance.LastSessionChange.AddSeconds(60) > DateTime.UtcNow))
                {
                    _combat.ProcessState();
                    _drones.ProcessState(); //do we really want to use drones here?
                }
            }
            if (Cache.Instance.InSpace && !Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
            {
                Cache.Instance.IsMissionPocketDone = true; //tells drones.cs that we can pull drones
                //Logging.Log("CombatmissionBehavior","TravelToAgentStation: not pointed",Logging.White);
            }
            Traveler.ProcessState();
            if (Settings.Instance.DebugStates)
            {
                Logging.Log("Traveler.State", "is " + _States.CurrentTravelerState, Logging.White);
            }
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
                            Logging.Log("DebugHangarsBehavior", _States.CurrentDebugHangarBehaviorState +
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
            //Logging.Log("DebugHangarsBehavior","ProcessState - every tick",Logging.Teal);
            if (Cache.Instance.SessionState == "Quitting")
            {
                BeginClosingQuestor();
            }

            if (Cache.Instance.GotoBaseNow)
            {
                if (_States.CurrentDebugHangarBehaviorState != DebugHangarsBehaviorState.GotoBase)
                {
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.GotoBase;
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
                // If Panic is in panic state, questor is in panic States.CurrentDebugHangarBehaviorState :)
                _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Panic;

                DebugHangarsBehaviorStates();
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
                _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Idle;

                DebugHangarsBehaviorStates();
            }
            DebugPanicstates();

            //Logging.Log("test");
            switch (_States.CurrentDebugHangarBehaviorState)
            {
                case DebugHangarsBehaviorState.Idle:

                    if (Cache.Instance.StopBot)
                    {
                        if (Settings.Instance.DebugIdle) Logging.Log("DebugHangarsBehavior", "if (Cache.Instance.StopBot)", Logging.White);
                        return;
                    }

                    if (Settings.Instance.DebugIdle) Logging.Log("DebugHangarsBehavior", "if (Cache.Instance.InSpace) else", Logging.White);
                    _States.CurrentArmState = ArmState.Idle;
                    _States.CurrentDroneState = DroneState.Idle;
                    _States.CurrentSalvageState = SalvageState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;
                    _States.CurrentUnloadLootState = UnloadLootState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;

                    LastAction = DateTime.UtcNow;
                    break;

                case DebugHangarsBehaviorState.DelayedGotoBase:
                    if (DateTime.UtcNow.Subtract(LastAction).TotalSeconds < Time.Instance.DelayedGotoBase_seconds)
                        break;

                    Logging.Log("DebugHangarsBehavior", "Heading back to base", Logging.White);
                    if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.DelayedGotoBase) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.GotoBase;
                    break;

                case DebugHangarsBehaviorState.Arm:
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
                        if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Arm) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    }

                    if (_States.CurrentArmState == ArmState.NotEnoughDrones)
                    {
                        // we know we are connected if we were able to arm the ship - update the lastknownGoodConnectedTime
                        // we may be out of drones/ammo but disconnecting/reconnecting will not fix that so update the timestamp
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        Logging.Log("Arm", "Armstate.NotEnoughDrones", Logging.Orange);
                        _States.CurrentArmState = ArmState.Idle;
                        if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Arm) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    }

                    if (_States.CurrentArmState == ArmState.Done)
                    {
                        //we know we are connected if we were able to arm the ship - update the lastknownGoodConnectedTime
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        _States.CurrentArmState = ArmState.Idle;
                        _States.CurrentDroneState = DroneState.WaitingForTargets;

                        if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Arm) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Idle;
                    }
                    break;

                case DebugHangarsBehaviorState.Salvage:
                    if (!Cache.Instance.InSpace)
                        return;

                    DirectContainer salvageCargo = Cache.Instance.DirectEve.GetShipsCargo();
                    Cache.Instance.SalvageAll = true;
                    Cache.Instance.OpenWrecks = true;

                    if (!Cache.Instance.OpenCargoHold("CombatMissionsBehavior: Salvage")) break;

                    if (Settings.Instance.UnloadLootAtStation && salvageCargo.Window.IsReady && (salvageCargo.Capacity - salvageCargo.UsedCapacity) < 100)
                    {
                        Logging.Log("CombatMissionsBehavior.Salvage", "We are full, go to base to unload", Logging.White);
                        if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Salvage)
                        {
                            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.GotoBase;
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

                case DebugHangarsBehaviorState.GotoBase:
                    if (Settings.Instance.DebugGotobase) Logging.Log("DebugHangarsBehavior", "GotoBase: AvoidBumpingThings()", Logging.White);

                    AvoidBumpingThings();

                    if (Settings.Instance.DebugGotobase) Logging.Log("DebugHangarsBehavior", "GotoBase: TravelToAgentsStation()", Logging.White);

                    TravelToAgentsStation();

                    if (_States.CurrentTravelerState == TravelerState.AtDestination) // || DateTime.UtcNow.Subtract(Cache.Instance.EnteredCloseQuestor_DateTime).TotalMinutes > 10)
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log("DebugHangarsBehavior", "GotoBase: We are at destination", Logging.White);
                        Cache.Instance.GotoBaseNow = false; //we are there - turn off the 'forced' GoToBase
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);

                        if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.GotoBase) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.UnloadLoot;

                        Traveler.Destination = null;
                    }
                    break;

                case DebugHangarsBehaviorState.UnloadLoot:
                    if (_States.CurrentUnloadLootState == UnloadLootState.Idle)
                    {
                        Logging.Log("DebugHangarsBehavior", "UnloadLoot: Begin", Logging.White);
                        _States.CurrentUnloadLootState = UnloadLootState.Begin;
                    }

                    _unloadLoot.ProcessState();

                    if (Settings.Instance.DebugStates)
                        Logging.Log("DebugHangarsBehavior", "UnloadLoot.State is " + _States.CurrentUnloadLootState, Logging.White);

                    if (_States.CurrentUnloadLootState == UnloadLootState.Done)
                    {
                        Cache.Instance.LootAlreadyUnloaded = true;
                        _States.CurrentUnloadLootState = UnloadLootState.Idle;
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);
                        if (_States.CurrentCombatState == CombatState.OutOfAmmo) // on mission
                        {
                            Logging.Log("DebugHangarsBehavior.UnloadLoot", "We are out of ammo", Logging.Orange);
                            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Idle;
                            return;
                        }

                        _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Idle;
                        Logging.Log("DebugHangarsBehavior.Unloadloot", "CharacterMode: [" + Settings.Instance.CharacterMode + "], AfterMissionSalvaging: [" + Settings.Instance.AfterMissionSalvaging + "], DebugHangarsBehaviorState: [" + _States.CurrentDebugHangarBehaviorState + "]", Logging.White);
                        Statistics.Instance.FinishedMission = DateTime.UtcNow;
                        return;
                    }
                    break;

                case DebugHangarsBehaviorState.Traveler:
                    Cache.Instance.OpenWrecks = false;
                    List<long> destination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();
                    if (destination == null || destination.Count == 0)
                    {
                        // happens if autopilot is not set and this QuestorState is chosen manually
                        // this also happens when we get to destination (!?)
                        Logging.Log("DebugHangarsBehavior.Traveler", "No destination?", Logging.White);
                        if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Traveler) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                        return;
                    }
                    else if (destination.Count == 1 && destination.First() == 0)
                        destination[0] = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;

                    if (Traveler.Destination == null || Traveler.Destination.SolarSystemId != destination.Last())
                    {
                        IEnumerable<DirectBookmark> bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.LocationId == destination.Last()).ToList();
                        if (bookmarks != null && bookmarks.Any())
                            Traveler.Destination = new BookmarkDestination(bookmarks.OrderBy(b => b.CreatedOn).First());
                        else
                        {
                            Logging.Log("DebugHangarsBehavior.Traveler", "Destination: [" + Cache.Instance.DirectEve.Navigation.GetLocation(destination.Last()).Name + "]", Logging.White);
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
                                Logging.Log("DebugHangarsBehavior.Traveler", "an error has occurred", Logging.White);
                                if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Traveler)
                                {
                                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                                }
                                return;
                            }

                            if (Cache.Instance.InSpace)
                            {
                                Logging.Log("DebugHangarsBehavior.Traveler", "Arrived at destination (in space, Questor stopped)", Logging.White);
                                if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Traveler) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                                return;
                            }

                            Logging.Log("DebugHangarsBehavior.Traveler", "Arrived at destination", Logging.White);
                            if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.Traveler) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Idle;
                            return;
                        }
                    }
                    break;

                case DebugHangarsBehaviorState.GotoNearestStation:
                    if (!Cache.Instance.InSpace || Cache.Instance.InWarp) return;
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        if (station.Distance > (int)Distance.WarptoDistance)
                        {
                            Logging.Log("DebugHangarsBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                            station.WarpToAndDock();
                            Cache.Instance.NextWarpTo = DateTime.UtcNow.AddSeconds(Time.Instance.WarptoDelay_seconds);
                            if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.GotoNearestStation) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Idle;
                            break;
                        }

                        if (station.Distance < 1900)
                        {
                            if (DateTime.UtcNow > Cache.Instance.NextDockAction)
                            {
                                Logging.Log("DebugHangarsBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                station.Dock();
                                Cache.Instance.NextDockAction = DateTime.UtcNow.AddSeconds(Time.Instance.DockingDelay_seconds);
                            }
                        }
                        else
                        {
                            if (Cache.Instance.NextApproachAction < DateTime.UtcNow && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != station.Id))
                            {
                                Cache.Instance.NextApproachAction = DateTime.UtcNow.AddSeconds(Time.Instance.ApproachDelay_seconds);
                                Logging.Log("DebugHangarsBehavior.GotoNearestStation", "Approaching [" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                station.Approach();
                            }
                        }
                    }
                    else
                    {
                        if (_States.CurrentDebugHangarBehaviorState == DebugHangarsBehaviorState.GotoNearestStation) _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error; //should we goto idle here?
                    }
                    break;

                case DebugHangarsBehaviorState.ReadyItemsHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.ReadyItemsHangar:", Logging.White);
                    if (!Cache.Instance.OpenItemsHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackItemsHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackItemsHangar:", Logging.White);
                    if (!Cache.Instance.StackItemsHangarAsAmmoHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseItemsHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseItemsHangar:", Logging.White);
                    if (!Cache.Instance.CloseItemsHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenShipsHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenShipsHangar:", Logging.White);
                    if (!Cache.Instance.OpenShipsHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackShipsHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackShipsHangar:", Logging.White);
                    if (!Cache.Instance.StackShipsHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseShipsHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseShipsHangar:", Logging.White);
                    if (!Cache.Instance.CloseShipsHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenLootContainer:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenLootContainer:", Logging.White);
                    if (!Cache.Instance.ReadyLootContainer("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackLootContainer:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackLootContainer:", Logging.White);
                    if (!Cache.Instance.StackLootContainer("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseLootContainer:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseLootContainer:", Logging.White);
                    if (!Cache.Instance.CloseLootContainer("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    //Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenCorpAmmoHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenCorpAmmoHangar:", Logging.White);
                    if (!Cache.Instance.ReadyCorpAmmoHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackCorpAmmoHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackCorpAmmoHangar:", Logging.White);
                    if (!Cache.Instance.StackCorpAmmoHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseCorpAmmoHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseCorpAmmoHangar:", Logging.White);
                    if (!Cache.Instance.CloseCorpHangar("DebugHangars", "AMMO")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenCorpLootHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenCorpLootHangar:", Logging.White);
                    if (!Cache.Instance.ReadyCorpLootHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackCorpLootHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackCorpLootHangar:", Logging.White);
                    if (!Cache.Instance.StackCorpLootHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseCorpLootHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseCorpLootHangar:", Logging.White);
                    if (!Cache.Instance.CloseCorpHangar("DebugHangars", "LOOT")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenAmmoHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenAmmoHangar:", Logging.White);
                    if (!Cache.Instance.ReadyAmmoHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackAmmoHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackAmmoHangar:", Logging.White);
                    if (!Cache.Instance.StackAmmoHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseAmmoHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseAmmoHangar:", Logging.White);
                    if (!Cache.Instance.CloseAmmoHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenLootHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenLootHangar:", Logging.White);
                    if (!Cache.Instance.ReadyLootHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackLootHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackLootHangar:", Logging.White);
                    if (!Cache.Instance.StackLootHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseLootHangar:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseLootHangar:", Logging.White);
                    if (!Cache.Instance.CloseLootHangar("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseAllInventoryWindows:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseAllInventoryWindows:", Logging.White);
                    if (!Cleanup.CloseInventoryWindows()) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenCargoHold:
                    Logging.Log("DebugHangars", "DebugHangarsState.StackLootHangar:", Logging.White);
                    if (!Cache.Instance.OpenCargoHold("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.StackCargoHold:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseLootHangar:", Logging.White);
                    if (!Cache.Instance.StackCargoHold("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.CloseCargoHold:
                    Logging.Log("DebugHangars", "DebugHangarsState.CloseAllInventoryWindows:", Logging.White);
                    if (!Cache.Instance.CloseCargoHold("DebugHangars")) return;
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.GetAmmoHangarID:
                    Logging.Log("DebugHangars", "DebugHangarsState.GetAmmoHangarID:", Logging.White);
                    if (!Cache.Instance.GetCorpAmmoHangarID()) return;
                    Logging.Log("DebugHangars", "AmmoHangarId [" + Cache.Instance.AmmoHangarID + "]", Logging.White);
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.GetLootHangarID:
                    Logging.Log("DebugHangars", "DebugHangarsState.GetLootHangarID:", Logging.White);
                    if (!Cache.Instance.GetCorpLootHangarID()) return;
                    Logging.Log("DebugHangars", "LootHangarId [" + Cache.Instance.LootHangarID + "]", Logging.White);
                    Cache.Instance.DebugInventoryWindows("DebugHangars");
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenInventory:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenInventory:", Logging.White);
                    if (!Cache.Instance.OpenInventoryWindow("DebugHangarsState.OpenInventoryWindow")) return;
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.ListInvTree:
                    Logging.Log("DebugHangars", "DebugHangarsState.ListInvTree:", Logging.White);
                    if (!Cache.Instance.ListInvTree("DebugHangarsState.ListInvTree")) return;
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.OpenOreHold:
                    Logging.Log("DebugHangars", "DebugHangarsState.OpenOreHold:", Logging.White);
                    if (!Cache.Instance.OpenOreHold("DebugHangarsState.OpenOreHold")) return;
                    _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.Error;
                    Cache.Instance.Paused = true;
                    break;

                case DebugHangarsBehaviorState.Default:
                    break;
            }
        }
    }
}