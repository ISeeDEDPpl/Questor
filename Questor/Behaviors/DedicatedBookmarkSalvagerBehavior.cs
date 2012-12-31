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
using System.Globalization;
using System.Linq;
using DirectEve;
using Questor.Modules.Caching;
using Questor.Modules.Logging;
using Questor.Modules.Lookup;
using Questor.Modules.Activities;
using Questor.Modules.States;
using Questor.Modules.Actions;
using Questor.Modules.BackgroundTasks;

namespace Questor.Behaviors
{
    public class DedicatedBookmarkSalvagerBehavior
    {
        private readonly Arm _arm;
        private readonly Panic _panic;
        private readonly Statistics _statistics;
        private readonly Salvage _salvage;
        private readonly UnloadLoot _unloadLoot;
        public DateTime LastAction;
        private DateTime _nextBookmarksrefresh = DateTime.UtcNow;

        public static long AgentID;

        private readonly Stopwatch _watch;
        private DateTime _nextBookmarkRefreshCheck = DateTime.UtcNow;

        public bool PanicStateReset = false;

        private bool ValidSettings { get; set; }

        public bool CloseQuestorFlag = true;

        public string CharacterName { get; set; }

        public List<DirectBookmark> BookmarksThatAreNotReadyYet;

        public DedicatedBookmarkSalvagerBehavior()
        {
            _salvage = new Salvage();
            _unloadLoot = new UnloadLoot();
            _arm = new Arm();
            _panic = new Panic();
            _statistics = new Statistics();
            _watch = new Stopwatch();

            //
            // this is combat mission specific and needs to be generalized
            //
            Settings.Instance.SettingsLoaded += SettingsLoaded;

            _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
            _States.CurrentArmState = ArmState.Idle;
            _States.CurrentUnloadLootState = UnloadLootState.Idle;
            _States.CurrentTravelerState = TravelerState.Idle;
        }

        public void SettingsLoaded(object sender, EventArgs e)
        {
            ApplySalvageSettings();
            ValidateDedicatedSalvageSettings();
        }

        public void DebugDedicatedBookmarkSalvagerBehaviorStates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("DedicateSalvagerBehavior.State is", _States.CurrentDedicatedBookmarkSalvagerBehaviorState.ToString(), Logging.White);
        }

        public void DebugPanicstates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("Panic.State is", _States.CurrentPanicState.ToString(), Logging.White);
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

        public void ValidateDedicatedSalvageSettings()
        {
            ValidSettings = true;
            DirectAgent agent = Cache.Instance.DirectEve.GetAgentByName(Cache.Instance.CurrentAgent);

            if (agent == null || !agent.IsValid)
            {
                Logging.Log("Settings", "Unable to locate agent [" + Cache.Instance.CurrentAgent + "]", Logging.White);
                ValidSettings = false;
            }
            else
            {
                _arm.AgentId = agent.AgentId;
                _statistics.AgentID = agent.AgentId;
                AgentID = agent.AgentId;
                _salvage.Ammo = Settings.Instance.Ammo;
                _salvage.MaximumWreckTargets = Settings.Instance.MaximumWreckTargets;
                _salvage.ReserveCargoCapacity = Settings.Instance.ReserveCargoCapacity;
                _salvage.LootEverything = Settings.Instance.LootEverything;
            }
        }

        public void ApplySalvageSettings()
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
                    ValidateDedicatedSalvageSettings();
                    LastAction = DateTime.UtcNow;
                }
                return;
            }

            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            //this local is safe check is useless as their is no localwatch processstate running every tick...
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            //If local unsafe go to base and do not start mission again
            if (Settings.Instance.FinishWhenNotSafe && (_States.CurrentDedicatedBookmarkSalvagerBehaviorState != DedicatedBookmarkSalvagerBehaviorState.GotoNearestStation /*|| State!=QuestorState.GotoBase*/))
            {
                //need to remove spam
                if (Cache.Instance.InSpace && !Cache.Instance.LocalSafe(Settings.Instance.LocalBadStandingPilotsToTolerate, Settings.Instance.LocalBadStandingLevelToConsiderBad))
                {
                    var station = Cache.Instance.Stations.OrderBy(x => x.Distance).FirstOrDefault();
                    if (station != null)
                    {
                        Logging.Log("Local not safe", "Station found. Going to nearest station", Logging.White);
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoNearestStation;
                    }
                    else
                    {
                        Logging.Log("Local not safe", "Station not found. Going back to base", Logging.White);
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
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
                _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
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
                DebugDedicatedBookmarkSalvagerBehaviorStates();
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
            }
            DebugPanicstates();

            switch (_States.CurrentDedicatedBookmarkSalvagerBehaviorState)
            {
                case DedicatedBookmarkSalvagerBehaviorState.Idle:

                    if (Cache.Instance.StopBot)
                        return;

                    _States.CurrentAgentInteractionState = AgentInteractionState.Idle;
                    _States.CurrentArmState = ArmState.Idle;
                    _States.CurrentDroneState = DroneState.Idle;
                    _States.CurrentSalvageState = SalvageState.Idle;
                    _States.CurrentStorylineState = StorylineState.Idle;
                    _States.CurrentTravelerState = TravelerState.Idle;
                    _States.CurrentUnloadLootState = UnloadLootState.Idle;
                    _States.CurrentTravelerState = TravelerState.AtDestination;

                    if (Cache.Instance.InSpace)
                    {
                        // Questor does not handle in space starts very well, head back to base to try again
                        Logging.Log("DedicatedBookmarkSalvagerBehavior", "Started questor while in space, heading back to base in 15 seconds", Logging.White);
                        LastAction = DateTime.UtcNow;
                        Cache.Instance.NextSalvageTrip = DateTime.UtcNow;
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.CheckBookmarkAge;
                        break;
                    }

                    // only attempt to write the mission statistics logs if one of the mission stats logs is enabled in settings
                    //if (Settings.Instance.SalvageStats1Log)
                    //{
                    //    if (!Statistics.Instance.SalvageLoggingCompleted)
                    //    {
                    //        Statistics.WriteSalvagerStatistics();
                    //        break;
                    //    }
                    //}

                    if (Settings.Instance.AutoStart)
                    {
                        //we know we are connected here
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;

                        // Don't start a new action an hour before downtime
                        if (DateTime.UtcNow.Hour == 10)
                        {
                            if (Settings.Instance.DebugAutoStart) Logging.Log("DedicatedBookmarkSalvagerBehavior", "Autostart: if (DateTime.UtcNow.Hour == 10)", Logging.White);
                            break;
                        }

                        // Don't start a new action near downtime
                        if (DateTime.UtcNow.Hour == 11 && DateTime.UtcNow.Minute < 15)
                        {
                            if (Settings.Instance.DebugAutoStart) Logging.Log("DedicatedBookmarkSalvagerBehavior", "if (DateTime.UtcNow.Hour == 11 && DateTime.UtcNow.Minute < 15)", Logging.White);
                            break;
                        }

                        //Logging.Log("DedicatedBookmarkSalvagerBehavior::: _nextBookmarksrefresh.subtract(DateTime.UtcNow).totalminutes [" +
                        //            Math.Round(DateTime.UtcNow.Subtract(_nextBookmarkRefreshCheck).TotalMinutes,0) + "]");

                        //Logging.Log("DedicatedBookmarkSalvagerBehavior::: Next Salvage Trip Scheduled in [" +
                        //            _Cache.Instance.NextSalvageTrip.ToString(CultureInfo.InvariantCulture) + "min]");

                        if (DateTime.UtcNow > _nextBookmarkRefreshCheck)
                        {
                            _nextBookmarkRefreshCheck = DateTime.UtcNow.AddMinutes(1);
                            if (Cache.Instance.InStation && (DateTime.UtcNow > _nextBookmarksrefresh))
                            {
                                _nextBookmarksrefresh = DateTime.UtcNow.AddMinutes(Cache.Instance.RandomNumber(18, 24));
                                Logging.Log("DedicatedBookmarkSalvagerBehavior", "Next Bookmark refresh in [" +
                                               Math.Round(_nextBookmarksrefresh.Subtract(DateTime.UtcNow).TotalMinutes, 0) + "min]", Logging.White);
                                Cache.Instance.DirectEve.RefreshBookmarks();
                            }
                            else
                            {
                                Logging.Log("DedicatedBookmarkSalvagerBehavior", "Next Bookmark refresh in [" +
                                               Math.Round(_nextBookmarksrefresh.Subtract(DateTime.UtcNow).TotalMinutes, 0) + "min]", Logging.White);

                                Logging.Log("DedicatedBookmarkSalvagerBehavior", "Next Salvage Trip Scheduled in [" +
                                               Math.Round(Cache.Instance.NextSalvageTrip.Subtract(DateTime.UtcNow).TotalMinutes, 0) + "min]", Logging.White);
                            }
                        }

                        if (DateTime.UtcNow > Cache.Instance.NextSalvageTrip)
                        {
                            Logging.Log("DedicatedBookmarkSalvagerBehavior.BeginAftermissionSalvaging", "Starting Another Salvage Trip", Logging.White);
                            LastAction = DateTime.UtcNow;
                            _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Start;
                            return;
                        }
                    }
                    else
                    {
                        Cache.Instance.LastScheduleCheck = DateTime.UtcNow;
                        Questor.TimeCheck();   //Should we close questor due to stoptime or runtime?
                    }
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.DelayedGotoBase:
                    if (DateTime.UtcNow.Subtract(LastAction).TotalSeconds < Time.Instance.DelayedGotoBase_seconds)
                        break;

                    Logging.Log("DedicatedBookmarkSalvagerBehavior", "Heading back to base", Logging.White);
                    _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.Start:
                    Cache.Instance.OpenWrecks = true;
                    ValidateDedicatedSalvageSettings();
                    _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.UnloadLoot;
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.LocalWatch:
                    if (Settings.Instance.UseLocalWatch)
                    {
                        Cache.Instance.LastLocalWatchAction = DateTime.UtcNow;
                        if (Cache.Instance.LocalSafe(Settings.Instance.LocalBadStandingPilotsToTolerate, Settings.Instance.LocalBadStandingLevelToConsiderBad))
                        {
                            Logging.Log("DedicatedBookmarkSalvagerBehavior.LocalWatch", "local is clear", Logging.White);
                            if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.LocalWatch) _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.BeginAfterMissionSalvaging;
                        }
                        else
                        {
                            Logging.Log("DedicatedBookmarkSalvagerBehavior.LocalWatch", "Bad standings pilots in local: We will stay 5 minutes in the station and then we will check if it is clear again", Logging.White);
                            if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.LocalWatch)
                            {
                                _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.WaitingforBadGuytoGoAway;
                            }
                            Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                            Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        }
                    }
                    else
                    {
                        if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.LocalWatch) _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.BeginAfterMissionSalvaging;
                    }
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.WaitingforBadGuytoGoAway:
                    Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                    Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastLocalWatchAction).TotalMinutes < Time.Instance.WaitforBadGuytoGoAway_minutes)
                        break;
                    if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.WaitingforBadGuytoGoAway) _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.LocalWatch;
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.GotoBase:

                    if (Settings.Instance.DebugGotobase) Logging.Log("DedicatedBookmarkSalvagerBehavior", "GotoBase: AvoidBumpingThings()", Logging.White);
                    NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjects.FirstOrDefault(), "DedicatedBookmarkSalvagerBehaviorState.GotoBase");
                    if (Settings.Instance.DebugGotobase) Logging.Log("DedicatedBookmarkSalvagerBehavior", "GotoBase: Traveler.TravelHome()", Logging.White);
                    Traveler.TravelHome("DedicatedBookmarkSalvagerBehavior");

                    if (_States.CurrentTravelerState == TravelerState.AtDestination) // || DateTime.UtcNow.Subtract(Cache.Instance.EnteredCloseQuestor_DateTime).TotalMinutes > 10)
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log("DedicatedBookmarkSalvagerBehavior", "GotoBase: We are at destination", Logging.White);
                        Cache.Instance.GotoBaseNow = false; //we are there - turn off the 'forced' gotobase
                        Cache.Instance.Mission = Cache.Instance.GetAgentMission(AgentID, false);
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.UnloadLoot;
                        Traveler.Destination = null;
                    }
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.UnloadLoot:
                    if (_States.CurrentUnloadLootState == UnloadLootState.Idle)
                    {
                        Logging.Log("DedicatedBookmarkSalvagerBehavior", "UnloadLoot: Begin", Logging.White);
                        _States.CurrentUnloadLootState = UnloadLootState.Begin;
                    }

                    _unloadLoot.ProcessState();

                    if (Settings.Instance.DebugStates)
                        Logging.Log("DedicatedBookmarkSalvagerBehavior", "UnloadLoot.State = " + _States.CurrentUnloadLootState, Logging.White);

                    if (_States.CurrentUnloadLootState == UnloadLootState.Done)
                    {
                        Cache.Instance.LootAlreadyUnloaded = true;
                        _States.CurrentUnloadLootState = UnloadLootState.Idle;
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.CheckBookmarkAge;
                    }
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.CheckBookmarkAge:

                    if (DateTime.UtcNow >= Cache.Instance.NextSalvageTrip)
                    {
                        if (Cache.Instance.GetSalvagingBookmark == null)
                        {
                            BookmarksThatAreNotReadyYet = Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ");
                            if (BookmarksThatAreNotReadyYet.Any())
                            {
                                Logging.Log("DedicatedBookmarkSalvagerBehavior", "CheckBookmarkAge: There are [" + BookmarksThatAreNotReadyYet.Count() + "] Salvage Bookmarks that have not yet aged [" + Settings.Instance.AgeofBookmarksForSalvageBehavior + "] min.", Logging.White);
                            }
                            Logging.Log("DedicatedBookmarkSalvagerBehavior", "CheckBookmarkAge: Character mode is BookmarkSalvager and no bookmarks are ready to salvage.", Logging.White);

                            //We just need a NextSalvagerSession timestamp to key off of here to add the delay
                            if (Cache.Instance.InSpace)
                            {
                                // Questor does not handle in space starts very well, head back to base to try again
                                LastAction = DateTime.UtcNow;
                                Cache.Instance.NextSalvageTrip = DateTime.UtcNow.AddMinutes(Time.Instance.DelayBetweenSalvagingSessions_minutes);
                                _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                                break;
                            }

                            _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                            _States.CurrentQuestorState = QuestorState.Idle;
                            Cache.Instance.NextSalvageTrip = DateTime.UtcNow.AddMinutes(Time.Instance.DelayBetweenSalvagingSessions_minutes);

                            break;
                        }

                        Logging.Log("DedicatedBookmarkSalvagerBehavior", "CheckBookmarkAge: There are [ " + Cache.Instance.AfterMissionSalvageBookmarks.Count() + " ] more salvage bookmarks older then:" + Cache.Instance.AgedDate.ToString(CultureInfo.InvariantCulture) + ", left to process", Logging.White);
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.BeginAfterMissionSalvaging;
                        Statistics.Instance.StartedSalvaging = DateTime.UtcNow;
                    }
                    else
                    {
                        Logging.Log("DedicatedBookmarkSalvagerBehavior", "CheckBookmarkAge: next salvage timer not expired. Waiting...", Logging.White);
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                        _States.CurrentQuestorState = QuestorState.Idle;
                        return;
                    }
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.BeginAfterMissionSalvaging:

                    if (DateTime.UtcNow > Statistics.Instance.StartedSalvaging.AddMinutes(2))
                    {
                        Logging.Log("DedicatedBookmarkSalvagebehavior", "Found [" + Cache.Instance.AfterMissionSalvageBookmarks.Count() + "] salvage bookmarks ready to process.", Logging.White);
                        Statistics.Instance.StartedSalvaging = DateTime.UtcNow; //this will be reset for each "run" between the station and the field if using <unloadLootAtStation>true</unloadLootAtStation>
                        Cache.Instance.NextSalvageTrip = DateTime.UtcNow.AddMinutes(Time.Instance.DelayBetweenSalvagingSessions_minutes);
                    }
                    //we know we are connected here
                    Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                    Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;

                    Cache.Instance.OpenWrecks = true;
                    if (Cache.Instance.InStation)
                    {
                        if (_States.CurrentArmState == ArmState.Idle)
                            _States.CurrentArmState = ArmState.SwitchToSalvageShip;

                        _arm.ProcessState();
                    }
                    if (_States.CurrentArmState == ArmState.Done || Cache.Instance.InSpace)
                    {
                        _States.CurrentArmState = ArmState.Idle;
                        DirectBookmark bookmark = Cache.Instance.AfterMissionSalvageBookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault();
                        if (bookmark == null)
                        {
                            _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                            Cache.Instance.NextSalvageTrip = DateTime.UtcNow.AddMinutes(Time.Instance.DelayBetweenSalvagingSessions_minutes);
                            return;
                        }
                        Logging.Log("DedicatedBookmarkSalvagerBehavior.Salvager", "Salvaging at first oldest bookmarks created on: " + bookmark.CreatedOn.ToString(), Logging.White);

                        var bookmarksInLocal = new List<DirectBookmark>(Cache.Instance.AfterMissionSalvageBookmarks.Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId).
                                                                               OrderBy(b => b.CreatedOn));
                        DirectBookmark localBookmark = bookmarksInLocal.FirstOrDefault();
                        if (localBookmark != null)
                        {
                            Traveler.Destination = new BookmarkDestination(localBookmark);
                        }
                        else
                        {
                            Traveler.Destination = new BookmarkDestination(bookmark);
                        }
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoSalvageBookmark;

                        //we know we are connected here
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.LastInWarp = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        return;
                    }
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.GotoSalvageBookmark:
                    Traveler.ProcessState();
                    if (Cache.Instance.GateInGrid())
                    {
                        Logging.Log("DedicatedBookmarkSalvagerBehavior", "GotoSalvageBookmark: We found gate in salvage bookmark. Going back to Base", Logging.White);

                        //we know we are connected here
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                        Traveler.Destination = null;
                        Cache.Instance.NextSalvageTrip = DateTime.UtcNow.AddMinutes(Time.Instance.DelayBetweenSalvagingSessions_minutes);
                        return;
                    }

                    if (_States.CurrentTravelerState == TravelerState.AtDestination)
                    {
                        Logging.Log("DedicatedBookmarkSalvagerBehavior", "GotoSalvageBookmark: Gate not found, we can start salvaging", Logging.White);

                        //we know we are connected here
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                        Cache.Instance.LastInWarp = DateTime.UtcNow;

                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Salvage;
                        Traveler.Destination = null;
                        return;
                    }

                    if (Settings.Instance.DebugStates)
                        Logging.Log("Traveler.State is ", _States.CurrentTravelerState.ToString(), Logging.White);
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.Salvage:
                    if (Settings.Instance.DebugSalvage) Logging.Log("DedicatedBookmarkSalvagerBehavior", "salvage: attempting to open cargo hold", Logging.White);
                    if (!Cache.Instance.OpenCargoHold("DedicatedSalvageBehavior: Salvage")) break;
                    if (Settings.Instance.DebugSalvage) Logging.Log("DedicatedBookmarkSalvagerBehavior", "salvage: done opening cargo hold", Logging.White);
                    Cache.Instance.SalvageAll = true;
                    Cache.Instance.OpenWrecks = true;

                    const int distanceToCheck = (int)Distance.OnGridWithMe;

                    // is there any NPCs within distanceToCheck?
                    EntityCache deadlyNPC = Cache.Instance.Entities.Where(t => t.Distance < distanceToCheck && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure).OrderBy(t => t.Distance).FirstOrDefault();

                    if (deadlyNPC != null)
                    {
                        Logging.Log("DedicatedBookmarkSalvagerBehavior.Salvage", "Npc name:[" + deadlyNPC.Name + "] with groupId:[" + deadlyNPC.GroupId + "].", Logging.White);

                        // found NPCs that will likely kill out fragile salvage boat!

                        DirectBookmark bookmark = Cache.Instance.AfterMissionSalvageBookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault();
                        if (bookmark != null)
                        {
                            Cache.Instance.DeleteBookmarksOnGrid("DedicatedBookmarkSalvageBehavior");
                            return;
                        }

                        Statistics.Instance.FinishedSalvaging = DateTime.UtcNow;
                        Cache.Instance.NextSalvageTrip = DateTime.UtcNow;
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                        return;
                    }

                    if (!Cache.Instance.OpenCargoHold("DedicatedBookmarkSalvageBehavior: Salvage")) break;

                    if (Cache.Instance.CargoHold.IsValid && (Cache.Instance.CargoHold.Capacity - Cache.Instance.CargoHold.UsedCapacity) < Settings.Instance.ReserveCargoCapacity)
                    {
                        Logging.Log("DedicatedBookmarkSalvageBehavior.Salvage", "We are full, go to base to unload", Logging.White);
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                        break;
                    }

                    if (!Cache.Instance.UnlootedContainers.Any())
                    {
                        Cache.Instance.DeleteBookmarksOnGrid("DedicatedBookmarkSalvageBehavior");
                        return;
                    }
                    else
                    {
                        if (DateTime.UtcNow > Cache.Instance.LastInWarp.AddMinutes(20))
                        {
                            Logging.Log("DedicatedBookmarkSalvagerBehavior", "It has been over 20 min since we were last in warp. Assuming something went wrong: setting GoToBase", Logging.Orange);
                            _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                            return;
                        }
                    }

                    if (Settings.Instance.DebugSalvage) Logging.Log("DedicatedBookmarkSalvagerBehavior", "salvage: we have more wrecks to salvage", Logging.White);
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
                        ApplySalvageSettings();
                    }
                    break;

                case DedicatedBookmarkSalvagerBehaviorState.Default:
                    _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                    break;
            }
        }
    }
}