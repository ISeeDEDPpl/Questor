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
    public class CombatHelperBehavior
    {
        private readonly Arm _arm;
        private readonly Combat _combat;
        private readonly Drones _drones;

        private readonly Panic _panic;
        private readonly Salvage _salvage;
        //private readonly Slave _slave;
        private readonly UnloadLoot _unloadLoot;

        public DateTime LastAction;
        public static long AgentID;

        private readonly Stopwatch _watch;

        public bool PanicStateReset; //false;

        private bool ValidSettings { get; set; }

        public bool CloseQuestorFlag = true;

        public string CharacterName { get; set; }

        public CombatHelperBehavior()
        {
            _arm = new Arm();
            _combat = new Combat();
            _drones = new Drones();
            _panic = new Panic();
            _salvage = new Salvage();
            //_slave = new Slave();
            _unloadLoot = new UnloadLoot();
            _watch = new Stopwatch();

            //
            // this is combat mission specific and needs to be generalized
            //
            Settings.Instance.SettingsLoaded += SettingsLoaded;

            //Settings.Instance.UseFittingManager = false;

            // States.CurrentCombatHelperBehaviorState fixed on ExecuteMission
            _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.UnloadLoot;
            _States.CurrentArmState = ArmState.Idle;

            //_States.CurrentCombatState = CombatState.Idle;
            //_States.CurrentDroneState = DroneState.Idle;
            _States.CurrentUnloadLootState = UnloadLootState.Idle;
            _States.CurrentTravelerState = TravelerState.Idle;
        }

        public void SettingsLoaded(object sender, EventArgs e)
        {
            ApplyCombatHelperSettings();
            ValidateCombatMissionSettings();
        }

        public void DebugCombatHelperBehaviorStates()
        {
            if (Settings.Instance.DebugStates) Logging.Log("CombatHelperBehavior.State is", _States.CurrentCombatHelperBehaviorState.ToString(), Logging.White);
        }

        public void DebugPanicstates()
        {
            if (Settings.Instance.DebugStates) Logging.Log("Panic.State is ", _States.CurrentPanicState.ToString(), Logging.White);
        }

        public void DebugPerformanceClearandStartTimer()
        {
            _watch.Reset();
            _watch.Start();
        }

        public void DebugPerformanceStopandDisplayTimer(string whatWeAreTiming)
        {
            _watch.Stop();
            if (Settings.Instance.DebugPerformance) Logging.Log(whatWeAreTiming, " took " + _watch.ElapsedMilliseconds + "ms", Logging.White);
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

        public void ApplyCombatHelperSettings()
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

        public void ProcessState()
        {
            // Invalid settings, quit while we're ahead
            if (!ValidSettings)
            {
                if (DateTime.UtcNow.Subtract(LastAction).TotalSeconds < Time.Instance.ValidateSettings_seconds) //default is a 15 second interval
                {
                    Logging.Log("CombatHelperBehavior", "Invalid Settings: Running ValidateCombatMissionSettings();", Logging.Orange);
                    ValidateCombatMissionSettings();
                    LastAction = DateTime.UtcNow;
                }
                return;
            }

            if (Settings.Instance.FinishWhenNotSafe && (_States.CurrentCombatHelperBehaviorState != CombatHelperBehaviorState.GotoNearestStation /*|| State!=QuestorState.GotoBase*/))
            {
                //need to remove spam
                if (Cache.Instance.InSpace && !Cache.Instance.LocalSafe(Settings.Instance.LocalBadStandingPilotsToTolerate, Settings.Instance.LocalBadStandingLevelToConsiderBad))
                {
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        Logging.Log("Local not safe", "Station found. Going to nearest station", Logging.White);
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.GotoNearestStation;
                    }
                    else
                    {
                        Logging.Log("Local not safe", "Station not found. Going back to base", Logging.White);
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.GotoBase;
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
                _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.GotoBase;
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
                // If Panic is in panic state, questor is in panic States.CurrentCombatHelperBehaviorState :)
                _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Panic;

                DebugCombatHelperBehaviorStates();
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
                _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;

                DebugCombatHelperBehaviorStates();
            }
            DebugPanicstates();

            //
            // the slave processstate is meant to override any Combathelper behavior (it is afterall meant to help the master kill things)
            //
            //_slave.ProcessState();
            //
            // done with slave process state
            //

            //Logging.Log("test");
            switch (_States.CurrentCombatHelperBehaviorState)
            {
                case CombatHelperBehaviorState.Idle:

                    if (Cache.Instance.StopBot)
                    {
                        if (Settings.Instance.DebugIdle) Logging.Log("CombatHelperBehavior", "if (Cache.Instance.StopBot)", Logging.White);
                        return;
                    }

                    if (Settings.Instance.DebugIdle) Logging.Log("CombatHelperBehavior", "if (Cache.Instance.InSpace) else", Logging.White);
                    _States.CurrentArmState = ArmState.Idle;
                    _States.CurrentDroneState = DroneState.Idle;
                    _States.CurrentSalvageState = SalvageState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;
                    _States.CurrentUnloadLootState = UnloadLootState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;

                    Logging.Log("CombatHelperBehavior", "Started questor in Combat Helper mode", Logging.White);
                    LastAction = DateTime.UtcNow;
                    _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.CombatHelper;
                    break;

                case CombatHelperBehaviorState.DelayedGotoBase:
                    if (DateTime.UtcNow.Subtract(LastAction).TotalSeconds < Time.Instance.DelayedGotoBase_seconds)
                    {
                        break;
                    }

                    Logging.Log("CombatHelperBehavior", "Heading back to base", Logging.White);
                    _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.GotoBase;
                    break;

                case CombatHelperBehaviorState.Arm:
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
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Error;
                    }

                    if (_States.CurrentArmState == ArmState.NotEnoughDrones)
                    {
                        // we know we are connected if we were able to arm the ship - update the lastknownGoodConnectedTime
                        // we may be out of drones/ammo but disconnecting/reconnecting will not fix that so update the timestamp
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        Logging.Log("Arm", "Armstate.NotEnoughDrones", Logging.Orange);
                        _States.CurrentArmState = ArmState.Idle;
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Error;
                    }

                    if (_States.CurrentArmState == ArmState.Done)
                    {
                        //we know we are connected if we were able to arm the ship - update the lastknownGoodConnectedTime
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        _States.CurrentArmState = ArmState.Idle;
                        _States.CurrentDroneState = DroneState.WaitingForTargets;
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                    }
                    break;

                case CombatHelperBehaviorState.CombatHelper:
                    if (Cache.Instance.InSpace)
                    {
                        DebugPerformanceClearandStartTimer();
                        _combat.ProcessState();
                        DebugPerformanceStopandDisplayTimer("Combat.ProcessState");

                        if (Settings.Instance.DebugStates) Logging.Log("Combat.State is", _States.CurrentCombatState.ToString(), Logging.White);

                        DebugPerformanceClearandStartTimer();
                        _drones.ProcessState();
                        DebugPerformanceStopandDisplayTimer("Drones.ProcessState");

                        if (Settings.Instance.DebugStates) Logging.Log("Drones.State is", _States.CurrentDroneState.ToString(), Logging.White);

                        DebugPerformanceClearandStartTimer();
                        _salvage.ProcessState();
                        DebugPerformanceStopandDisplayTimer("Salvage.ProcessState");

                        if (Settings.Instance.DebugStates) Logging.Log("Salvage.State is", _States.CurrentSalvageState.ToString(), Logging.White);

                        // If we are out of ammo, return to base (do we want to do this with combat helper?!)
                        if (_States.CurrentCombatState == CombatState.OutOfAmmo)
                        {
                            Logging.Log("Combat", "Out of Ammo!", Logging.Orange);
                            _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.GotoBase;

                            // Clear looted containers
                            Cache.Instance.LootedContainers.Clear();
                        }
                    }
                    break;

                case CombatHelperBehaviorState.Salvage:
                    if (!Cache.Instance.InSpace) return;

                    if (!Cache.Instance.OpenCargoHold("CombatMissionsBehavior: Salvage")) return;
                    Cache.Instance.SalvageAll = true;
                    Cache.Instance.OpenWrecks = true;

                    if (Settings.Instance.UnloadLootAtStation && Cache.Instance.CargoHold.IsValid && (Cache.Instance.CargoHold.Capacity - Cache.Instance.CargoHold.UsedCapacity) < 100)
                    {
                        Logging.Log("CombatMissionsBehavior.Salvage", "We are full, go to base to unload", Logging.White);
                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.GotoBase;
                        break;
                    }

                    if (!Cache.Instance.UnlootedContainers.Any()) return;

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
                        ApplyCombatHelperSettings();
                    }
                    break;

                case CombatHelperBehaviorState.GotoBase:
                    if (Settings.Instance.DebugGotobase) Logging.Log("CombatHelperBehavior", "GotoBase: AvoidBumpingThings()", Logging.White);

                    if (Settings.Instance.AvoidBumpingThings) NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjects.FirstOrDefault(), "CombatHelperBehaviorState.GotoBase");

                    if (Settings.Instance.DebugGotobase) Logging.Log("CombatHelperBehavior", "GotoBase: Traveler.TravelHome()", Logging.White);

                    Traveler.TravelHome("CombatHelperBehavior.TravelHome");

                    if (_States.CurrentTravelerState == TravelerState.AtDestination && DateTime.UtcNow > Cache.Instance.LastInSpace.AddSeconds(5)) // || DateTime.UtcNow.Subtract(Cache.Instance.EnteredCloseQuestor_DateTime).TotalMinutes > 10)
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log("CombatHelperBehavior", "GotoBase: We are at destination", Logging.White);
                        Cache.Instance.GotoBaseNow = false; //we are there - turn off the 'forced' gotobase
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);

                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.UnloadLoot;

                        Traveler.Destination = null;
                    }
                    break;

                case CombatHelperBehaviorState.UnloadLoot:
                    if (_States.CurrentUnloadLootState == UnloadLootState.Idle)
                    {
                        Logging.Log("CombatHelperBehavior", "UnloadLoot: Begin", Logging.White);
                        _States.CurrentUnloadLootState = UnloadLootState.Begin;
                    }

                    _unloadLoot.ProcessState();

                    if (Settings.Instance.DebugStates) Logging.Log("CombatHelperBehavior", "UnloadLoot.State is " + _States.CurrentUnloadLootState, Logging.White);

                    if (_States.CurrentUnloadLootState == UnloadLootState.Done)
                    {
                        Cache.Instance.LootAlreadyUnloaded = true;
                        _States.CurrentUnloadLootState = UnloadLootState.Idle;
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);
                        if (_States.CurrentCombatState == CombatState.OutOfAmmo) // on mission
                        {
                            Logging.Log("CombatHelperBehavior.UnloadLoot", "We are out of ammo", Logging.Orange);
                            _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Arm;
                            return;
                        }

                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Arm;
                        Logging.Log("CombatHelperBehavior.Unloadloot", "CharacterMode: [" + Settings.Instance.CharacterMode + "], AfterMissionSalvaging: [" + Settings.Instance.AfterMissionSalvaging + "], CombatHelperBehaviorState: [" + _States.CurrentCombatHelperBehaviorState + "]", Logging.White);
                        Statistics.Instance.FinishedMission = DateTime.UtcNow;
                        return;
                    }
                    break;

                case CombatHelperBehaviorState.WarpOutStation:
                    DirectBookmark warpOutBookmark = Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkWarpOut ?? "").OrderByDescending(b => b.CreatedOn).FirstOrDefault(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId);

                    //DirectBookmark _bookmark = Cache.Instance.BookmarksByLabel(Settings.Instance.bookmarkWarpOut + "-" + Cache.Instance.CurrentAgent ?? "").OrderBy(b => b.CreatedOn).FirstOrDefault();
                    long solarid = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;

                    if (warpOutBookmark == null)
                    {
                        Logging.Log("BackgroundBehavior.WarpOut", "No Bookmark", Logging.White);
                        if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.WarpOutStation) _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.CombatHelper;
                    }
                    else if (warpOutBookmark.LocationId == solarid)
                    {
                        if (Traveler.Destination == null)
                        {
                            Logging.Log("BackgroundBehavior.WarpOut", "Warp at " + warpOutBookmark.Title, Logging.White);
                            Traveler.Destination = new BookmarkDestination(warpOutBookmark);
                            Cache.Instance.DoNotBreakInvul = true;
                        }

                        Traveler.ProcessState();
                        if (_States.CurrentTravelerState == TravelerState.AtDestination)
                        {
                            Logging.Log("BackgroundBehavior.WarpOut", "Safe!", Logging.White);
                            Cache.Instance.DoNotBreakInvul = false;
                            if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.WarpOutStation) _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.CombatHelper;
                            Traveler.Destination = null;
                        }
                    }
                    else
                    {
                        Logging.Log("BackgroundBehavior.WarpOut", "No Bookmark in System", Logging.Orange);
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.CombatHelper;
                    }

                    break;

                case CombatHelperBehaviorState.Traveler:
                    Cache.Instance.OpenWrecks = false;
                    List<int> destination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();
                    if (destination == null || destination.Count == 0)
                    {
                        // happens if autopilot is not set and this QuestorState is chosen manually
                        // this also happens when we get to destination (!?)
                        Logging.Log("CombatHelperBehavior.Traveler", "No destination?", Logging.White);
                        if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.Traveler) _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Error;
                    }
                    else if (destination.Count == 1 && destination.FirstOrDefault() == 0)
                    {
                        destination[0] = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;
                    }

                    if (destination != null && (Traveler.Destination == null || Traveler.Destination.SolarSystemId != destination.Last()))
                    {
                        IEnumerable<DirectBookmark> bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.LocationId == destination.Last()).ToList();
                        if (bookmarks.FirstOrDefault() != null && bookmarks.Any())
                        {
                            Traveler.Destination = new BookmarkDestination(bookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault());
                        }
                        else
                        {
                            Logging.Log("CombatHelperBehavior.Traveler", "Destination: [" + Cache.Instance.DirectEve.Navigation.GetLocation(destination.Last()).Name + "]", Logging.White);
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
                                Logging.Log("CombatHelperBehavior.Traveler", "an error has occurred", Logging.White);
                                _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Error;
                            }
                            else if (Cache.Instance.InSpace)
                            {
                                Logging.Log("CombatHelperBehavior.Traveler", "Arrived at destination (in space, Questor stopped)", Logging.White);
                                _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Error;
                            }
                            else
                            {
                                Logging.Log("CombatHelperBehavior.Traveler", "Arrived at destination", Logging.White);
                                _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                                return;
                            }
                        }
                    }
                    break;

                case CombatHelperBehaviorState.GotoNearestStation:
                    if (!Cache.Instance.InSpace || Cache.Instance.InWarp) return;
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        if (station.Distance > (int)Distance.WarptoDistance)
                        {
                            Logging.Log("CombatHelperBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                            station.WarpToAndDock();
                            _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                        }
                        else
                        {
                            if (station.Distance < 1900)
                            {
                                if (DateTime.UtcNow > Cache.Instance.NextDockAction)
                                {
                                    Logging.Log("CombatHelperBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                    station.Dock();
                                }
                            }
                            else
                            {
                                if (Cache.Instance.NextApproachAction < DateTime.UtcNow && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != station.Id))
                                {
                                    Logging.Log("CombatHelperBehavior.GotoNearestStation", "Approaching [" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                    station.Approach();
                                }
                            }
                        }
                    }
                    else
                    {
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Error; //should we goto idle here?
                    }
                    break;

                case CombatHelperBehaviorState.LogCombatTargets:
                    //combat targets
                    //List<EntityCache> combatentitiesInList =  Cache.Instance.Entities.Where(t => t.IsNpc && !t.IsBadIdea && t.CategoryId == (int)CategoryID.Entity && !t.IsContainer && t.Distance < Cache.Instance.MaxRange && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).ToList();
                    List<EntityCache> combatentitiesInList = Cache.Instance.Entities.Where(t => t.IsNpc && !t.IsBadIdea && t.CategoryId == (int)CategoryID.Entity && !t.IsContainer).ToList();
                    Statistics.EntityStatistics(combatentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case CombatHelperBehaviorState.LogDroneTargets:
                    //drone targets
                    List<EntityCache> droneentitiesInList = Cache.Instance.Entities.Where(e => e.IsNpc && !e.IsBadIdea && e.CategoryId == (int)CategoryID.Entity && !e.IsContainer && !e.IsSentry && e.GroupId != (int)Group.LargeColidableStructure).ToList();
                    Statistics.EntityStatistics(droneentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case CombatHelperBehaviorState.LogStationEntities:
                    //stations
                    List<EntityCache> stationsInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.Station).ToList();
                    Statistics.EntityStatistics(stationsInList);
                    Cache.Instance.Paused = true;
                    break;

                case CombatHelperBehaviorState.LogStargateEntities:
                    //stargates
                    List<EntityCache> stargatesInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.Stargate).ToList();
                    Statistics.EntityStatistics(stargatesInList);
                    Cache.Instance.Paused = true;
                    break;

                case CombatHelperBehaviorState.LogAsteroidBelts:
                    //Asteroid Belts
                    List<EntityCache> asteroidbeltsInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.AsteroidBelt).ToList();
                    Statistics.EntityStatistics(asteroidbeltsInList);
                    Cache.Instance.Paused = true;
                    break;

                case CombatHelperBehaviorState.Default:
                    _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                    break;
            }
        }
    }
}
