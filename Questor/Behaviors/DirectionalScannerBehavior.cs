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
    public class DirectionalScannerBehavior
    {
        private readonly Arm _arm;
        private readonly Combat _combat;
        private readonly Drones _drones;

        private readonly Panic _panic;
        private readonly Salvage _salvage;
        public DateTime LastAction;
        public static long AgentID;
        private readonly Stopwatch _watch;

        public bool PanicstateReset; //false;

        private bool ValidSettings { get; set; }

        public bool CloseQuestorFlag = true;

        public string CharacterName { get; set; }

        //DateTime _nextAction = DateTime.UtcNow;

        public DirectionalScannerBehavior()
        {
            _salvage = new Salvage();
            _combat = new Combat();
            _drones = new Drones();
            _arm = new Arm();
            _panic = new Panic();
            _watch = new Stopwatch();

            //
            // this is combat mission specific and needs to be generalized
            //
            Settings.Instance.SettingsLoaded += SettingsLoaded;
            _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;
            _States.CurrentArmState = ArmState.Idle;
            _States.CurrentUnloadLootState = UnloadLootState.Idle;
            _States.CurrentTravelerState = TravelerState.Idle;
        }

        public void SettingsLoaded(object sender, EventArgs e)
        {
            ApplyDirectionalScannerSettings();
            ValidateCombatMissionSettings();
        }

        public void DebugDirectionalScannerBehaviorStates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("DirectionalScannerBehavior.State is", _States.CurrentDirectionalScannerBehaviorState.ToString(), Logging.White);
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

        public void ApplyDirectionalScannerSettings()
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
                    ValidateCombatMissionSettings();
                    LastAction = DateTime.UtcNow;
                }
                return;
            }

            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            //this local is safe check is useless as their is no localwatch processstate running every tick...
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            //If local unsafe go to base and do not start mission again
            if (Settings.Instance.FinishWhenNotSafe && (_States.CurrentDirectionalScannerBehaviorState != DirectionalScannerBehaviorState.GotoNearestStation /*|| State!=QuestorState.GotoBase*/))
            {
                //need to remove spam
                if (Cache.Instance.InSpace && !Cache.Instance.LocalSafe(Settings.Instance.LocalBadStandingPilotsToTolerate, Settings.Instance.LocalBadStandingLevelToConsiderBad))
                {
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        Logging.Log("Local not safe", "Station found. Going to nearest station", Logging.White);
                        _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.GotoNearestStation;
                    }
                    else
                    {
                        Logging.Log("Local not safe", "Station not found. Going back to base", Logging.White);
                        _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.GotoBase;
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
                _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.GotoBase;
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
                // If Panic is in panic state, questor is in panic States.CurrentDirectionalScannerBehaviorState :)
                _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Panic;

                DebugDirectionalScannerBehaviorStates();
                if (PanicstateReset)
                {
                    _States.CurrentPanicState = PanicState.Normal;
                    PanicstateReset = false;
                }
            }
            else if (_States.CurrentPanicState == PanicState.Resume)
            {
                // Reset panic state
                _States.CurrentPanicState = PanicState.Normal;

                // Sit Idle and wait for orders.
                _States.CurrentTravelerState = TravelerState.Idle;
                _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;

                DebugDirectionalScannerBehaviorStates();
            }
            DebugPanicstates();

            //Logging.Log("test");
            switch (_States.CurrentDirectionalScannerBehaviorState)
            {
                case DirectionalScannerBehaviorState.Idle:

                    if (Cache.Instance.StopBot)
                    {
                        if (Settings.Instance.DebugIdle) Logging.Log("DirectionalScannerBehavior", "if (Cache.Instance.StopBot)", Logging.White);
                        return;
                    }

                    if (Settings.Instance.DebugIdle) Logging.Log("DirectionalScannerBehavior", "if (Cache.Instance.InSpace) else", Logging.White);
                    _States.CurrentArmState = ArmState.Idle;
                    _States.CurrentDroneState = DroneState.Idle;
                    _States.CurrentSalvageState = SalvageState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;
                    _States.CurrentUnloadLootState = UnloadLootState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;

                    Logging.Log("DirectionalScannerBehavior", "Started questor in Directional Scanner (test) mode", Logging.White);
                    LastAction = DateTime.UtcNow;
                    break;

                case DirectionalScannerBehaviorState.DelayedGotoBase:
                    if (DateTime.UtcNow.Subtract(LastAction).TotalSeconds < Time.Instance.DelayedGotoBase_seconds)
                        break;

                    Logging.Log("DirectionalScannerBehavior", "Heading back to base", Logging.White);
                    if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.DelayedGotoBase) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.GotoBase;
                    break;

                case DirectionalScannerBehaviorState.GotoBase:
                    if (Settings.Instance.DebugGotobase) Logging.Log("DirectionalScannerBehavior", "GotoBase: AvoidBumpingThings()", Logging.White);

                    NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjects.FirstOrDefault(), "DirectionalScannerBehaviorState.GotoBase");

                    if (Settings.Instance.DebugGotobase) Logging.Log("DirectionalScannerBehavior", "GotoBase: Traveler.TravelHome()", Logging.White);

                    Traveler.TravelHome("DirectionalScannerBehavior");

                    if (_States.CurrentTravelerState == TravelerState.AtDestination) // || DateTime.UtcNow.Subtract(Cache.Instance.EnteredCloseQuestor_DateTime).TotalMinutes > 10)
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log("DirectionalScannerBehavior", "GotoBase: We are at destination", Logging.White);
                        Cache.Instance.GotoBaseNow = false; //we are there - turn off the 'forced' gotobase
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);

                        if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.GotoBase) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;

                        Traveler.Destination = null;
                    }
                    break;

                case DirectionalScannerBehaviorState.Traveler:
                    Cache.Instance.OpenWrecks = false;
                    List<long> destination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();
                    if (destination == null || destination.Count == 0)
                    {
                        // happens if autopilot is not set and this QuestorState is chosen manually
                        // this also happens when we get to destination (!?)
                        Logging.Log("DirectionalScannerBehavior.Traveler", "No destination?", Logging.White);
                        if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.Traveler) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Error;
                        return;
                    }

                    if (destination.Count == 1 && destination.FirstOrDefault() == 0)
                        destination[0] = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;
                    if (Traveler.Destination == null || Traveler.Destination.SolarSystemId != destination.LastOrDefault())
                    {
                        IEnumerable<DirectBookmark> bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.LocationId == destination.LastOrDefault()).ToList();
                        if (bookmarks.FirstOrDefault() != null && bookmarks.Any())
                            Traveler.Destination = new BookmarkDestination(bookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault());
                        else
                        {
                            Logging.Log("DirectionalScannerBehavior.Traveler", "Destination: [" + Cache.Instance.DirectEve.Navigation.GetLocation(destination.Last()).Name + "]", Logging.White);
                            Traveler.Destination = new SolarSystemDestination(destination.LastOrDefault());
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
                                Logging.Log("DirectionalScannerBehavior.Traveler", "an error has occurred", Logging.White);
                                if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.Traveler)
                                {
                                    _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Error;
                                }
                                return;
                            }

                            if (Cache.Instance.InSpace)
                            {
                                Logging.Log("DirectionalScannerBehavior.Traveler", "Arrived at destination (in space, Questor stopped)", Logging.White);
                                if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.Traveler) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Error;
                                return;
                            }

                            Logging.Log("DirectionalScannerBehavior.Traveler", "Arrived at destination", Logging.White);
                            if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.Traveler) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;
                            return;
                        }
                    }
                    break;

                case DirectionalScannerBehaviorState.GotoNearestStation:
                    if (!Cache.Instance.InSpace || Cache.Instance.InWarp) return;
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        if (station.Distance > (int)Distance.WarptoDistance)
                        {
                            Logging.Log("DirectionalScannerBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                            station.WarpToAndDock();
                            if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.GotoNearestStation) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;
                            break;
                        }

                        if (station.Distance < 1900)
                        {
                            if (DateTime.UtcNow > Cache.Instance.NextDockAction)
                            {
                                Logging.Log("DirectionalScannerBehavior.GotoNearestStation", "[" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                station.Dock();
                            }
                        }
                        else
                        {
                            if (Cache.Instance.NextApproachAction < DateTime.UtcNow && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != station.Id))
                            {
                                Logging.Log("DirectionalScannerBehavior.GotoNearestStation", "Approaching [" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.White);
                                station.Approach();
                            }
                        }
                    }
                    else
                    {
                        if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.GotoNearestStation) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Error; //should we goto idle here?
                    }
                    break;

                case DirectionalScannerBehaviorState.PVPDirectionalScanHalfanAU:
                    Logging.Log("DirectionalScannerBehavior", "PVPDirectionalScanhalfanAU - Starting", Logging.White);
                    List<EntityCache> pvpDirectionalScanHalfanAUentitiesInList = Cache.Instance.Entities.Where(t => t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pvpDirectionalScanHalfanAUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVPDirectionalScan1AU:
                    Logging.Log("DirectionalScannerBehavior", "PVPDirectionalScan1AU - Starting", Logging.White);
                    List<EntityCache> pvpDirectionalScan1AUentitiesInList = Cache.Instance.Entities.Where(t => t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 1).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pvpDirectionalScan1AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVPDirectionalScan5AU:
                    Logging.Log("DirectionalScannerBehavior", "PVPDirectionalScan5AU - Starting", Logging.White);
                    List<EntityCache> pvpDirectionalScan5AUentitiesInList = Cache.Instance.Entities.Where(t => t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 5).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pvpDirectionalScan5AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVPDirectionalScan10AU:
                    Logging.Log("DirectionalScannerBehavior", "PVPDirectionalScan10AU - Starting", Logging.White);
                    List<EntityCache> pvpDirectionalScan10AUentitiesInList = Cache.Instance.Entities.Where(t => t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 10).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pvpDirectionalScan10AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVPDirectionalScan15AU:
                    Logging.Log("DirectionalScannerBehavior", "PVPDirectionalScan15AU - Starting", Logging.White);
                    List<EntityCache> pvpDirectionalScan15AUentitiesInList = Cache.Instance.Entities.Where(t => t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 15).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pvpDirectionalScan15AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVPDirectionalScan20AU:
                    Logging.Log("DirectionalScannerBehavior", "PVPDirectionalScan20AU - Starting", Logging.White);
                    List<EntityCache> pvpDirectionalScan20AUentitiesInList = Cache.Instance.Entities.Where(t => t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 20).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pvpDirectionalScan20AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVPDirectionalScan50AU:
                    Logging.Log("DirectionalScannerBehavior", "PVPDirectionalScan50AU - Starting", Logging.White);
                    List<EntityCache> pvpDirectionalScan50AUentitiesInList = Cache.Instance.Entities.Where(t => t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 50).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pvpDirectionalScan50AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVEDirectionalScanHalfanAU:
                    Logging.Log("DirectionalScannerBehavior", "PVEDirectionalScanhalfanAU - Starting", Logging.White);
                    List<EntityCache> pveDirectionalScanHalfanAUentitiesInList = Cache.Instance.Entities.Where(t => !t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pveDirectionalScanHalfanAUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVEDirectionalScan1AU:
                    Logging.Log("DirectionalScannerBehavior", "PVEDirectionalScan1AU - Starting", Logging.White);
                    List<EntityCache> pveDirectionalScan1AUentitiesInList = Cache.Instance.Entities.Where(t => !t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 1).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pveDirectionalScan1AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVEDirectionalScan5AU:
                    Logging.Log("DirectionalScannerBehavior", "PVEDirectionalScan5AU - Starting", Logging.White);
                    List<EntityCache> pveDirectionalScan5AUentitiesInList = Cache.Instance.Entities.Where(t => !t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 5).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pveDirectionalScan5AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVEDirectionalScan10AU:
                    Logging.Log("DirectionalScannerBehavior", "PVEDirectionalScan10AU - Starting", Logging.White);
                    List<EntityCache> pveDirectionalScan10AUentitiesInList = Cache.Instance.Entities.Where(t => !t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 10).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pveDirectionalScan10AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVEDirectionalScan15AU:
                    Logging.Log("DirectionalScannerBehavior", "PVEDirectionalScan15AU - Starting", Logging.White);
                    List<EntityCache> pveDirectionalScan15AUentitiesInList = Cache.Instance.Entities.Where(t => !t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 15).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pveDirectionalScan15AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVEDirectionalScan20AU:
                    Logging.Log("DirectionalScannerBehavior", "PVEDirectionalScan20AU - Starting", Logging.White);
                    List<EntityCache> pveDirectionalScan20AUentitiesInList = Cache.Instance.Entities.Where(t => !t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 20).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pveDirectionalScan20AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.PVEDirectionalScan50AU:
                    Logging.Log("DirectionalScannerBehavior", "PVEDirectionalScan50AU - Starting", Logging.White);
                    List<EntityCache> pveDirectionalScan50AUentitiesInList = Cache.Instance.Entities.Where(t => !t.IsPlayer && t.Distance < (double)Distance.DirectionalScannerCloseRange * 50).OrderBy(t => t.Distance).ToList();
                    Statistics.EntityStatistics(pveDirectionalScan50AUentitiesInList);
                    Cache.Instance.Paused = true;
                    break;



                case DirectionalScannerBehaviorState.LogCombatTargets:
                    //combat targets
                    //List<EntityCache> combatentitiesInList =  Cache.Instance.Entities.Where(t => t.IsNpc && !t.IsBadIdea && t.CategoryId == (int)CategoryID.Entity && !t.IsContainer && t.Distance < Cache.Instance.MaxRange && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).ToList();
                    List<EntityCache> combatentitiesInList = Cache.Instance.Entities.Where(t => t.IsNpc && !t.IsBadIdea && t.CategoryId == (int)CategoryID.Entity && !t.IsContainer).ToList();
                    Statistics.EntityStatistics(combatentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.LogDroneTargets:
                    //drone targets
                    List<EntityCache> droneentitiesInList = Cache.Instance.Entities.Where(e => e.IsNpc && !e.IsBadIdea && e.CategoryId == (int)CategoryID.Entity && !e.IsContainer && !e.IsSentry && e.GroupId != (int)Group.LargeCollidableStructure).ToList();
                    Statistics.EntityStatistics(droneentitiesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.LogStationEntities:
                    //stations
                    List<EntityCache> stationsInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.Station).ToList();
                    Statistics.EntityStatistics(stationsInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.LogStargateEntities:
                    //stargates
                    List<EntityCache> stargatesInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.Stargate).ToList();
                    Statistics.EntityStatistics(stargatesInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.LogAsteroidBelts:
                    //Asteroid Belts
                    List<EntityCache> asteroidbeltsInList = Cache.Instance.Entities.Where(e => !e.IsSentry && e.GroupId == (int)Group.AsteroidBelt).ToList();
                    Statistics.EntityStatistics(asteroidbeltsInList);
                    Cache.Instance.Paused = true;
                    break;

                case DirectionalScannerBehaviorState.Default:
                    if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.Default) _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;
                    break;
            }
        }
    }
}
