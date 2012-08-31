// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

using System.Globalization;
using Questor.Behaviors;
using Questor.Modules.Combat;

namespace Questor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using DirectEve;
    using System.Linq;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.BackgroundTasks;
    using LavishScriptAPI;

    public class Questor
    {
        private readonly QuestorfrmMain m_Parent;
        private readonly LocalWatch _localwatch;
        private readonly Defense _defense;
        private readonly DirectEve _directEve;

        private DateTime _lastPulse;
        private readonly CombatMissionsBehavior _combatMissionsBehavior;
        private readonly CombatHelperBehavior _combatHelperBehavior;
        private readonly DedicatedBookmarkSalvagerBehavior _dedicatedBookmarkSalvagerBehavior;
        private readonly DirectionalScannerBehavior _directionalScannerBehavior;
        private readonly Cleanup _cleanup;

        public DateTime LastAction;

        public bool Panicstatereset = false;
        private bool _runOnce30SecAfterStartupalreadyProcessed = false;

        private readonly Stopwatch _watch;

        public Questor(QuestorfrmMain form1)
        {
            m_Parent = form1;
            _lastPulse = DateTime.Now;

            _defense = new Defense();
            _localwatch = new LocalWatch();
            _combatMissionsBehavior = new CombatMissionsBehavior();
            _combatHelperBehavior = new CombatHelperBehavior();
            _dedicatedBookmarkSalvagerBehavior = new DedicatedBookmarkSalvagerBehavior();
            _directionalScannerBehavior = new DirectionalScannerBehavior();
            _cleanup = new Cleanup();
            _watch = new Stopwatch();

            // State fixed on ExecuteMission
            _States.CurrentQuestorState = QuestorState.Idle;

            try
            {
                _directEve = new DirectEve();
            }
            catch (Exception ex)
            {
                Logging.Log("Startup", "Error on Loading DirectEve, maybe server is down", Logging.orange);
                Logging.Log("Startup", string.Format("DirectEVE: Exception {0}...", ex), Logging.white);
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.CloseQuestorEndProcess = true;
                Settings.Instance.AutoStart = true;
                Cache.Instance.ReasonToStopQuestor = "Error on Loading DirectEve, maybe lic server is down";
                Cache.Instance.SessionState = "Quitting";
                Cleanup.CloseQuestor();
            }
            Cache.Instance.DirectEve = _directEve;

            Cache.Instance.StopTimeSpecified = Program.StopTimeSpecified;
            Cache.Instance.MaxRuntime = Program.MaxRuntime;
            if (Program.StartTime.AddMinutes(10) < Program.StopTime)
            {
                Cache.Instance.StopTime = Program.StopTime;
                Logging.Log("Questor", "Schedule: setup correctly: stoptime is [" + Cache.Instance.StopTime.ToShortTimeString() + "]", Logging.orange);
            }
            else
            {
                Cache.Instance.StopTime = DateTime.Now.AddHours(Time.Instance.QuestorScheduleNotUsed_Hours);
                Logging.Log("Questor", "Schedule: NOT setup correctly: stoptime  set to [" + (int)Time.Instance.QuestorScheduleNotUsed_Hours + "] hours from now at [" + Cache.Instance.StopTime.ToShortTimeString() + "]", Logging.orange);
                Logging.Log("Questor", "You can correct this by editing schedules.xml to have an entry for this toon", Logging.orange);
                Logging.Log("Questor", "Ex: <char user=\"" + Settings.Instance.CharacterName + "\" pw=\"MyPasswordForEVEHere\" name=\"MyLoginNameForEVEHere\" start=\"06:45\" stop=\"08:10\" start2=\"09:05\" stop2=\"14:20\"/>", Logging.orange);
                Logging.Log("Questor", "make sure each toon has its own innerspace profile and specify the following startup program line:", Logging.orange);
                Logging.Log("Questor", "dotnet questor questor -x -c \"MyEVECharacterName\"", Logging.orange);
            }
            Cache.Instance.StartTime = Program.StartTime;
            Cache.Instance.QuestorStarted_DateTime = DateTime.Now;

            // get the current process
            Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            // get the physical mem usage
            Cache.Instance.TotalMegaBytesOfMemoryUsed = ((currentProcess.WorkingSet64 / 1024) / 1024);
            Logging.Log("Questor", "EVE instance: totalMegaBytesOfMemoryUsed - " +
                        Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.white);
            Cache.Instance.SessionIskGenerated = 0;
            Cache.Instance.SessionLootGenerated = 0;
            Cache.Instance.SessionLPGenerated = 0;
            Settings.Instance.CharacterMode = "none";
            try
            {
                _directEve.OnFrame += OnFrame;
            }
            catch (Exception ex)
            {
                Logging.Log("Startup", string.Format("DirectEVE.OnFrame: Exception {0}...", ex), Logging.white);
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.CloseQuestorEndProcess = true;
                Settings.Instance.AutoStart = true;
                Cache.Instance.ReasonToStopQuestor = "Error on DirectEve.OnFrame, maybe lic server is down";
                Cache.Instance.SessionState = "Quitting";
                Cleanup.CloseQuestor();
            }
        }

        public string CharacterName { get; set; }

        public static long AgentID;

        public void DebugCombatMissionsBehaviorStates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("CombatMissionsBehavior.State is", _States.CurrentQuestorState.ToString(), Logging.white);
        }

        //public void DebugPanicstates()
        //{
        //    if (Settings.Instance.DebugStates)
        //        Logging.Log("Panic.State = " + _panic.State);
        //    }

        public void RunOnce30SecAfterStartup()
        {
            if (!_runOnce30SecAfterStartupalreadyProcessed && DateTime.Now > Cache.Instance.QuestorStarted_DateTime.AddSeconds(30))
            {
                if (Settings.Instance.CharacterName != null && DateTime.Now > Cache.Instance.NextStartupAction)
                {
                    _runOnce30SecAfterStartupalreadyProcessed = true;
                    if (Settings.Instance.UseInnerspace)
                    {
                        //enable windowtaskbar = on, so that minimized windows do not make us die in a fire.
                        Logging.Log("Questor", "Running Innerspace command: windowtaskbar on" + Settings.Instance.CharacterName, Logging.white);
                        LavishScript.ExecuteCommand("windowtaskbar on" + Settings.Instance.CharacterName);
                        if (Settings.Instance.LoginQuestorArbitraryOSCmd)
                        {
                            Logging.Log("Questor", "After Questor Login: executing LoginQuestorArbitraryOSCmd", Logging.white);
                            LavishScript.ExecuteCommand("Echo [${Time}] OSExecute " + Settings.Instance.LoginQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                            LavishScript.ExecuteCommand("OSExecute " + Settings.Instance.LoginQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                            Logging.Log("Questor", "Done: executing LoginQuestorArbitraryOSCmd", Logging.white);
                        }
                        if (Settings.Instance.LoginQuestorLavishScriptCmd)
                        {
                            Logging.Log("Questor", "After Questor Login: executing LoginQuestorLavishScriptCmd", Logging.white);
                            LavishScript.ExecuteCommand("Echo [${Time}] runscript " + Settings.Instance.LoginQuestorLavishScriptContents.ToString(CultureInfo.InvariantCulture));
                            LavishScript.ExecuteCommand("runscript " + Settings.Instance.LoginQuestorLavishScriptContents.ToString(CultureInfo.InvariantCulture));
                            Logging.Log("Questor", "Done: executing LoginQuestorLavishScriptCmd", Logging.white);
                        }
                    }
                }
                else
                {
                    Logging.Log("Questor", "RunOnce30SecAfterStartup: Settings.Instance.CharacterName is still null", Logging.orange);
                    Cache.Instance.NextStartupAction = DateTime.Now.AddSeconds(30);
                    _runOnce30SecAfterStartupalreadyProcessed = false;
                    return;
                }
            }
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
                Logging.Log(whatWeAreTiming, " took " + _watch.ElapsedMilliseconds + "ms", Logging.white);
        }

        public static void BeginClosingQuestor()
        {
            Cache.Instance.EnteredCloseQuestor_DateTime = DateTime.Now;
            Cache.Instance.SessionState = "Quitting";
            _States.CurrentQuestorState = QuestorState.CloseQuestor;
            return;
        }

        public static void TimeCheck()
        {
            if (DateTime.Now < Cache.Instance.NextTimeCheckAction)
                return;

            Cache.Instance.NextTimeCheckAction = DateTime.Now.AddMinutes(5);
            Logging.Log("Questor",
                        "Checking: Current time [" + DateTime.Now.ToString(CultureInfo.InvariantCulture) +
                        "] StopTimeSpecified [" + Cache.Instance.StopTimeSpecified +
                        "] StopTime [ " + Cache.Instance.StopTime +
                        "] ManualStopTime = " + Cache.Instance.ManualStopTime, Logging.white);

            if (DateTime.Now.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes > Cache.Instance.MaxRuntime)
            {
                // quit questor
                Logging.Log("Questor", "Maximum runtime exceeded.  Quiting...", Logging.white);
                Cache.Instance.ReasonToStopQuestor = "Maximum runtime specified and reached.";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
                return;
            }
            if (Cache.Instance.StopTimeSpecified)
            {
                if (DateTime.Now >= Cache.Instance.StopTime)
                {
                    Logging.Log("Questor", "Time to stop. StopTimeSpecified and reached. Quitting game.", Logging.white);
                    Cache.Instance.ReasonToStopQuestor = "StopTimeSpecified and reached.";
                    Settings.Instance.AutoStart = false;
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                    BeginClosingQuestor();
                    return;
                }
            }
            if (DateTime.Now >= Cache.Instance.ManualRestartTime)
            {
                Logging.Log("Questor", "Time to stop. ManualRestartTime reached. Quitting game.", Logging.white);
                Cache.Instance.ReasonToStopQuestor = "ManualRestartTime reached.";
                Settings.Instance.AutoStart = true;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
                return;
            }
            if (DateTime.Now >= Cache.Instance.ManualStopTime)
            {
                Logging.Log("Questor", "Time to stop. ManualStopTime reached. Quitting game.", Logging.white);
                Cache.Instance.ReasonToStopQuestor = "ManualStopTime reached.";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
                return;
            }
            if (Cache.Instance.ExitWhenIdle)
            {
                Logging.Log("Questor", "ExitWhenIdle set to true.  Quitting game.", Logging.white);
                Cache.Instance.ReasonToStopQuestor = "ExitWhenIdle set to true";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
                return;
            }
            if (Cache.Instance.MissionsThisSession > Cache.Instance.StopSessionAfterMissionNumber)
            {
                Logging.Log("Questor", "MissionsThisSession [" + Cache.Instance.MissionsThisSession + "] is greater than StopSessionAfterMissionNumber [" + Cache.Instance.StopSessionAfterMissionNumber + "].  Quitting game.", Logging.white);
                Cache.Instance.ReasonToStopQuestor = "MissionsThisSession > StopSessionAfterMissionNumber";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                BeginClosingQuestor();
                return;
            }
        }

        public static void WalletCheck()
        {
            Cache.Instance.LastWalletCheck = DateTime.Now;
            //Logging.Log("[Questor] Wallet Balance Debug Info: lastknowngoodconnectedtime = " + Settings.Instance.lastKnownGoodConnectedTime);
            //Logging.Log("[Questor] Wallet Balance Debug Info: DateTime.Now - lastknowngoodconnectedtime = " + DateTime.Now.Subtract(Settings.Instance.lastKnownGoodConnectedTime).TotalSeconds);
            if (Math.Round(DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) > 1)
            {
                Logging.Log("Questor", String.Format("Wallet Balance Has Not Changed in [ {0} ] minutes.",
                                          Math.Round(
                                              DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).
                                                  TotalMinutes, 0)), Logging.white);
            }

            //Settings.Instance.walletbalancechangelogoffdelay = 2;  //used for debugging purposes
            //Logging.Log("Cache.Instance.lastKnownGoodConnectedTime is currently: " + Cache.Instance.lastKnownGoodConnectedTime);
            if (Math.Round(DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) < Settings.Instance.Walletbalancechangelogoffdelay)
            {
                if ((long)Cache.Instance.MyWalletBalance != (long)Cache.Instance.DirectEve.Me.Wealth)
                {
                    Cache.Instance.LastKnownGoodConnectedTime = DateTime.Now;
                    Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                }
            }
            else if (Settings.Instance.Walletbalancechangelogoffdelay != 0)
            {
                if ((Cache.Instance.InStation) || (Math.Round(DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) > Settings.Instance.Walletbalancechangelogoffdelay + 5))
                {
                    Logging.Log("Questor", String.Format("Questor: Wallet Balance Has Not Changed in [ {0} ] minutes. Switching to QuestorState.CloseQuestor", Math.Round(DateTime.Now.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes, 0)), Logging.white);
                    Cache.Instance.ReasonToStopQuestor = "Wallet Balance did not change for over " + Settings.Instance.Walletbalancechangelogoffdelay + "min";
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                    BeginClosingQuestor();
                    return;
                }

                //
                // it is assumed if you got this far that you are in space. If you are 'stuck' in a session change then you'll be stuck another 5 min until the timeout above. 
                //
                _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;
                _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.GotoBase;
                _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.GotoBase;
                return;
            }
        }

        public static void CheckEVEStatus()
        {
            // get the current process
            Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();

            // get the physical mem usage (this only runs between missions)
            Cache.Instance.TotalMegaBytesOfMemoryUsed = ((currentProcess.WorkingSet64 / 1024) / 1024);
            Logging.Log("Questor", "EVE instance: totalMegaBytesOfMemoryUsed - " +
                        Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.white);

            if (Cache.Instance.TotalMegaBytesOfMemoryUsed > (Settings.Instance.EVEProcessMemoryCeiling - 50) &&
                        Settings.Instance.EVEProcessMemoryCeilingLogofforExit != "")
            {
                Logging.Log(
                    "Questor", ": Memory usage is above the EVEProcessMemoryCeiling threshold. EVE instance: totalMegaBytesOfMemoryUsed - " +
                    Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.white);
                Cache.Instance.ReasonToStopQuestor =
                    "Memory usage is above the EVEProcessMemoryCeiling threshold. EVE instance: totalMegaBytesOfMemoryUsed - " +
                    Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB";
                if (Settings.Instance.EVEProcessMemoryCeilingLogofforExit == "logoff")
                {
                    Cache.Instance.CloseQuestorCMDLogoff = true;
                    Cache.Instance.CloseQuestorCMDExitGame = false;
                    Cache.Instance.SessionState = "LoggingOff";
                    BeginClosingQuestor();
                    return;
                }
                if (Settings.Instance.EVEProcessMemoryCeilingLogofforExit == "exit")
                {
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                    BeginClosingQuestor();
                    return;
                }
                Logging.Log(
                    "Questor", "EVEProcessMemoryCeilingLogofforExit was not set to exit or logoff - doing nothing ", Logging.white);
            }
            else
            {
                Cache.Instance.SessionState = "Running";
            }
        }

        public bool OnframeProcessEveryPulse()
        {
            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            var watch = new Stopwatch();
            Cache.Instance.LastFrame = DateTime.Now;

            // Only pulse state changes every 1.5s
            if (DateTime.Now.Subtract(_lastPulse).TotalMilliseconds < Time.Instance.QuestorPulse_milliseconds) //default: 1500ms
                return false;
            _lastPulse = DateTime.Now;

            // Update settings (settings only load if character name changed)
            if (!Settings.Instance.Defaultsettingsloaded)
            {
                Settings.Instance.LoadSettings();
            }
            CharacterName = Cache.Instance.DirectEve.Me.Name;

            if (DateTime.Now < Cache.Instance.QuestorStarted_DateTime.AddSeconds(30))
            {
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.Now;
            }

            // Start _cleanup.ProcessState
            // Description: Closes Windows, and eventually other things considered 'cleanup' useful to more than just Questor(Missions) but also Anomalies, Mining, etc
            //
            DebugPerformanceClearandStartTimer();
            _cleanup.ProcessState();
            DebugPerformanceStopandDisplayTimer("Cleanup.ProcessState");

            if (Settings.Instance.DebugStates)
                Logging.Log("Cleanup.State is", _States.CurrentCleanupState.ToString(), Logging.white);

            // Done
            // Cleanup State: ProcessState

            // Session is not ready yet, do not continue
            if (!Cache.Instance.DirectEve.Session.IsReady)
                return false;

            if (Cache.Instance.DirectEve.Session.IsReady)
                Cache.Instance.LastSessionIsReady = DateTime.Now;

            // We are not in space or station, don't do shit yet!
            if (!Cache.Instance.InSpace && !Cache.Instance.InStation)
            {
                Cache.Instance.NextInSpaceorInStation = DateTime.Now.AddSeconds(12);
                Cache.Instance.LastSessionChange = DateTime.Now;
                return false;
            }

            if (DateTime.Now < Cache.Instance.NextInSpaceorInStation)
            {
                if (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Capsule)
                {
                    Logging.Log("Panic", "We are in a pod. Don't wait for the session wait timer to expire!", Logging.red);
                    Cache.Instance.NextInSpaceorInStation = DateTime.Now;
                    return true;
                }
                return false;
            }

            // Check 3D rendering
            if (Cache.Instance.DirectEve.Session.IsInSpace &&
                Cache.Instance.DirectEve.Rendering3D != !Settings.Instance.Disable3D)
                Cache.Instance.DirectEve.Rendering3D = !Settings.Instance.Disable3D;

            if (DateTime.Now.Subtract(Cache.Instance.LastupdateofSessionRunningTime).TotalSeconds <
                Time.Instance.SessionRunningTimeUpdate_seconds)
            {
                Cache.Instance.SessionRunningTime =
                    (int)DateTime.Now.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes;
                Cache.Instance.LastupdateofSessionRunningTime = DateTime.Now;
            }
            return true;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (!OnframeProcessEveryPulse()) return;
            if (Settings.Instance.DebugOnframe) Logging.Log("Questor", "Onframe: this is Questor.cs [" + DateTime.Now + "] by default the next pulse will be in [" + (int)Time.Instance.QuestorPulse_milliseconds + "]milliseconds", Logging.teal);

            if (!Cache.Instance.Paused)
            {
                if (DateTime.Now.Subtract(Cache.Instance.LastWalletCheck).TotalMinutes > Time.Instance.WalletCheck_minutes && !Settings.Instance.Defaultsettingsloaded)
                {
                    WalletCheck();
                }
            }

            // We always check our defense state if we're in space, regardless of questor state
            // We also always check panic
            if ((Cache.Instance.LastInSpace.AddSeconds(2) > DateTime.Now) && Cache.Instance.InSpace)
            {
                DebugPerformanceClearandStartTimer();
                if (!Cache.Instance.DoNotBreakInvul)
                {
                    _defense.ProcessState();
                }
                DebugPerformanceStopandDisplayTimer("Defense.ProcessState");
            }

            if (Cache.Instance.Paused)
            {
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.Now;
                Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                Cache.Instance.GotoBaseNow = false;
                Cache.Instance.SessionState = string.Empty;
                return;
            }

            if (Cache.Instance.SessionState == "Quitting")
            {
                if (_States.CurrentQuestorState != QuestorState.CloseQuestor)
                {
                    BeginClosingQuestor();
                }
            }

            // When in warp there's nothing we can do, so ignore everything
            if (Cache.Instance.InWarp)
                return;

            //DirectAgentMission mission;
            switch (_States.CurrentQuestorState)
            {
                case QuestorState.Idle:
                    TimeCheck(); //Should we close questor due to stoptime or runtime?

                    if (Cache.Instance.StopBot)
                    {
                        if (Settings.Instance.DebugIdle) Logging.Log("Questor", "Cache.Instance.StopBot = true - this is set by the localwatch code so that we stay in station when local is unsafe", Logging.orange);
                        return;
                    }

                    if (_States.CurrentQuestorState == QuestorState.Idle && Settings.Instance.CharacterMode != "none")
                    {
                        _States.CurrentQuestorState = QuestorState.Start;
                        return;
                    }
                    else
                    {
                        Logging.Log("Questor", "Settings.Instance.CharacterMode = [" + Settings.Instance.CharacterMode + "]", Logging.orange);
                        _States.CurrentQuestorState = QuestorState.Error;
                    }
                    break;

                case QuestorState.CombatMissionsBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Idle)
                    {
                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Idle;
                    }
                    _combatMissionsBehavior.ProcessState();
                    break;

                case QuestorState.CombatHelperBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.Idle)
                    {
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                    }
                    _combatHelperBehavior.ProcessState();
                    break;

                case QuestorState.DedicatedBookmarkSalvagerBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.Idle)
                    {
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                    }
                    _dedicatedBookmarkSalvagerBehavior.ProcessState();
                    break;

                case QuestorState.DirectionalScannerBehavior:
                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    if (_States.CurrentDirectionalScannerBehaviorState == DirectionalScannerBehaviorState.Idle)
                    {
                        _States.CurrentDirectionalScannerBehaviorState = DirectionalScannerBehaviorState.Idle;
                    }
                    _directionalScannerBehavior.ProcessState();
                    break;

                case QuestorState.DebugReloadAll:
                    if (!Combat.ReloadAll(Cache.Instance.EntitiesNotSelf.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < (double)Distance.OnGridWithMe))) return;
                    _States.CurrentQuestorState = QuestorState.Start;
                    break;

                case QuestorState.Start:
                    switch (Settings.Instance.CharacterMode.ToLower())
                    {
                        case "combat missions":
                        case "combat_missions":
                        case "dps":
                            Logging.Log("Questor", "Start Mission Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.CombatMissionsBehavior;
                            break;

                        case "salvage":
                            Logging.Log("Questor", "Start Salvaging Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.DedicatedBookmarkSalvagerBehavior;
                            break;

                        case "combat helper":
                        case "combat_helper":
                        case "combathelper":
                            Logging.Log("Questor", "Start CombatHelper Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.CombatHelperBehavior;
                            break;

                        case "directionalscanner":
                            Logging.Log("Questor", "Start DirectionalScanner Behavior", Logging.white);
                            _States.CurrentQuestorState = QuestorState.DirectionalScannerBehavior;
                            break;
                    }
                    break;

                case QuestorState.CloseQuestor:
                    Cleanup.CloseQuestor();
                    return;

                case QuestorState.DebugCloseQuestor:
                    //Logging.Log("ISBoxerCharacterSet: " + Settings.Instance.Lavish_ISBoxerCharacterSet);
                    //Logging.Log("Profile: " + Settings.Instance.Lavish_InnerspaceProfile);
                    //Logging.Log("Game: " + Settings.Instance.Lavish_Game);
                    Logging.Log("Questor", "CloseQuestorCMDUplinkInnerspaceProfile: " + Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile, Logging.white);
                    Logging.Log("Questor", "CloseQuestorCMDUplinkISboxerCharacterSet: " + Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet, Logging.white);
                    Logging.Log("Questor", "CloseQuestorArbitraryOSCmd" + Settings.Instance.CloseQuestorArbitraryOSCmd, Logging.white);
                    Logging.Log("Questor", "CloseQuestorOSCmdContents" + Settings.Instance.CloseQuestorOSCmdContents, Logging.white);
                    Logging.Log("Questor", "walletbalancechangelogoffdelay: " + Settings.Instance.Walletbalancechangelogoffdelay, Logging.white);
                    Logging.Log("Questor", "walletbalancechangelogoffdelayLogofforExit: " + Settings.Instance.WalletbalancechangelogoffdelayLogofforExit, Logging.white);
                    Logging.Log("Questor", "EVEProcessMemoryCeiling: " + Settings.Instance.EVEProcessMemoryCeiling, Logging.white);
                    Logging.Log("Questor", "EVEProcessMemoryCielingLogofforExit: " + Settings.Instance.EVEProcessMemoryCeilingLogofforExit, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorCMDExitGame: " + Cache.Instance.CloseQuestorCMDExitGame, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorCMDLogoff: " + Cache.Instance.CloseQuestorCMDLogoff, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorEndProcess: " + Cache.Instance.CloseQuestorEndProcess, Logging.white);
                    Logging.Log("Questor", "Cache.Instance.EnteredCloseQuestor_DateTime: " + Cache.Instance.EnteredCloseQuestor_DateTime.ToShortTimeString(), Logging.white);
                    _States.CurrentQuestorState = QuestorState.Error;
                    return;

                case QuestorState.DebugWindows:
                    List<DirectWindow> windows = Cache.Instance.Windows;

                    foreach (DirectWindow window in windows)
                    {
                        Logging.Log("Questor", "--------------------------------------------------", Logging.orange);
                        Logging.Log("Questor", "Debug_Window.Name: [" + window.Name + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.Caption: [" + window.Caption + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.Type: [" + window.Type + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.IsModal: [" + window.IsModal + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.IsDialog: [" + window.IsDialog + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.Id: [" + window.Id + "]", Logging.white);
                        Logging.Log("Questor", "Debug_Window.IsKillable: [" + window.IsKillable + "]", Logging.white);
                        //Logging.Log("Questor", "Debug_Window.Html: [" + window.Html + "]", Logging.white);
                    }
                    Logging.Log("Questor", "Debug_InventoryWindows", Logging.white);
                    foreach (DirectWindow window in windows)
                    {
                        if (window.Type.Contains("inventory"))
                        {
                            Logging.Log("Questor", "Debug_Window.Name: [" + window.Name + "]", Logging.white);
                            Logging.Log("Questor", "Debug_Window.Type: [" + window.Type + "]", Logging.white);
                            Logging.Log("Questor", "Debug_Window.Caption: [" + window.Caption + "]", Logging.white);
                            //Logging.Log("Questor", "Debug_Window.Type: [" + window. + "]", Logging.white);
                        }
                    }
                    _States.CurrentQuestorState = QuestorState.Error;
                    return;

                case QuestorState.DebugInventoryTree:
                    
                    if (Cache.Instance.InventoryWindow.ExpandCorpHangarView())
                    {
                        Logging.Log("DebugInventoryTree", "ExpandCorpHangar executed", Logging.teal);
                    }
                    Logging.Log("DebugInventoryTree", "--------------------------------------------------", Logging.orange);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Name: [" + Cache.Instance.InventoryWindow.Name + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Caption: [" + Cache.Instance.InventoryWindow.Caption + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Type: [" + Cache.Instance.InventoryWindow.Type + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsModal: [" + Cache.Instance.InventoryWindow.IsModal + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsDialog: [" + Cache.Instance.InventoryWindow.IsDialog + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Id: [" + Cache.Instance.InventoryWindow.Id + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsKillable: [" + Cache.Instance.InventoryWindow.IsKillable + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsReady: [" + Cache.Instance.InventoryWindow.IsReady + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.LocationFlag: [" + Cache.Instance.InventoryWindow.LocationFlag + "]", Logging.white);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.currInvIdName: " + Cache.Instance.InventoryWindow.currInvIdName, Logging.red);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.currInvIdName: " + Cache.Instance.InventoryWindow.currInvIdItem, Logging.red);

                    foreach (Int64 itemintree in Cache.Instance.IDsinInventoryTree)
                    {
                        if (Cache.Instance.InventoryWindow.GetIdsFromTree(false).Contains(itemintree))
                        {
                            Cache.Instance.InventoryWindow.SelectTreeEntryByID(itemintree);
                            Cache.Instance.IDsinInventoryTree.Remove(itemintree);
                            break;
                        }
                    }
                    break;
            }
        }
    }
}