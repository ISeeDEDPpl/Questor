// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using DirectEve;
    using global::Questor.Behaviors;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Combat;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.BackgroundTasks;
    using LavishScriptAPI;

    public class Questor
    {
        private readonly QuestorfrmMain _mParent;
        private readonly Defense _defense;
        private readonly DirectEve _directEve;

        private DateTime _lastPulse;
        private static DateTime _nextQuestorAction = DateTime.UtcNow.AddHours(-1);
        private readonly CombatMissionsBehavior _combatMissionsBehavior;
        private readonly CombatHelperBehavior _combatHelperBehavior;
        private readonly DedicatedBookmarkSalvagerBehavior _dedicatedBookmarkSalvagerBehavior;
        private readonly DirectionalScannerBehavior _directionalScannerBehavior;
        private readonly DebugHangarsBehavior _debugHangarsBehavior;
        private readonly MiningBehavior _miningBehavior;
        //private readonly BackgroundBehavior _backgroundbehavior;
        private readonly Cleanup _cleanup;

        public DateTime LastAction;
        public string ScheduleCharacterName = Program._character;
        public bool PanicStateReset = false;
        private bool _runOnce30SecAfterStartupalreadyProcessed;

        private readonly Stopwatch _watch;

        public Questor(QuestorfrmMain form1)
        {
            _mParent = form1;
            _lastPulse = DateTime.UtcNow;

            _defense = new Defense();
            _combatMissionsBehavior = new CombatMissionsBehavior();
            _combatHelperBehavior = new CombatHelperBehavior();
            _dedicatedBookmarkSalvagerBehavior = new DedicatedBookmarkSalvagerBehavior();
            _directionalScannerBehavior = new DirectionalScannerBehavior();
            _debugHangarsBehavior = new DebugHangarsBehavior();
            _miningBehavior = new MiningBehavior();
            //_backgroundbehavior = new BackgroundBehavior();
            _cleanup = new Cleanup();
            _watch = new Stopwatch();

            ScheduleCharacterName = Program._character;
            Cache.Instance.ScheduleCharacterName = ScheduleCharacterName;
            Cache.Instance.NextStartupAction = DateTime.UtcNow;
            // State fixed on ExecuteMission
            _States.CurrentQuestorState = QuestorState.Idle;

            try
            {
                _directEve = new DirectEve();
            }
            catch (Exception ex)
            {
                Logging.Log("Questor", "Error on Loading DirectEve, maybe server is down", Logging.Orange);
                Logging.Log("Questor", string.Format("DirectEVE: Exception {0}...", ex), Logging.White);
                //Cache.Instance.CloseQuestorCMDLogoff = false;
                //Cache.Instance.CloseQuestorCMDExitGame = true;
                //Cache.Instance.CloseQuestorEndProcess = true;
                //Settings.Instance.AutoStart = true;
                //Cache.Instance.ReasonToStopQuestor = "Error on Loading DirectEve, maybe license server is down";
                //Cache.Instance.SessionState = "Quitting";
                Cleanup.CloseQuestor();
            }
            Cache.Instance.DirectEve = _directEve;

            try
            {
                if (_directEve.HasSupportInstances())
                {
                    Logging.Log("Questor", "You have a valid directeve.lic file and have instances available", Logging.Orange);
                }
                else
                {
                    Logging.Log("Questor", "You have 0 Support Instances available [ _directEve.HasSupportInstances() is false ]", Logging.Orange);
                }

            }
            catch (Exception exception)
            {
                Logging.Log("Questor", "Exception while checking: _directEve.HasSupportInstances() - exception was: [" + exception + "]", Logging.Orange);
            }
            
            Cache.Instance.StopTimeSpecified = Program.StopTimeSpecified;
            Cache.Instance.MaxRuntime = Program.MaxRuntime;
            if (Program.StartTime.AddMinutes(10) < Program.StopTime)
            {
                Cache.Instance.StopTime = Program.StopTime;
                Logging.Log("Questor", "Schedule: setup correctly: stoptime is [" + Cache.Instance.StopTime.ToShortTimeString() + "]", Logging.Orange);
            }
            else
            {
                Cache.Instance.StopTime = DateTime.Now.AddHours(Time.Instance.QuestorScheduleNotUsed_Hours);
                Logging.Log("Questor", "Schedule: NOT setup correctly: stoptime  set to [" + Time.Instance.QuestorScheduleNotUsed_Hours + "] hours from now at [" + Cache.Instance.StopTime.ToShortTimeString() + "]", Logging.Orange);
                Logging.Log("Questor", "You can correct this by editing schedules.xml to have an entry for this toon", Logging.Orange);
                Logging.Log("Questor", "Ex: <char user=\"" + Settings.Instance.CharacterName + "\" pw=\"MyPasswordForEVEHere\" name=\"MyLoginNameForEVEHere\" start=\"06:45\" stop=\"08:10\" start2=\"09:05\" stop2=\"14:20\"/>", Logging.Orange);
                Logging.Log("Questor", "make sure each toon has its own innerspace profile and specify the following startup program line:", Logging.Orange);
                Logging.Log("Questor", "dotnet questor questor -x -c \"MyEVECharacterName\"", Logging.Orange);
            }

            Cache.Instance.StartTime = Program.StartTime;
            Cache.Instance.QuestorStarted_DateTime = DateTime.UtcNow;

            // get the current process
            Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();

            // get the physical mem usage
            Cache.Instance.TotalMegaBytesOfMemoryUsed = ((currentProcess.WorkingSet64 / 1024) / 1024);
            Logging.Log("Questor", "EVE instance: totalMegaBytesOfMemoryUsed - " + Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.White);
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
                Logging.Log("Questor", string.Format("DirectEVE.OnFrame: Exception {0}...", ex), Logging.White);
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.CloseQuestorEndProcess = true;
                Settings.Instance.AutoStart = true;
                Cache.Instance.ReasonToStopQuestor = "Error on DirectEve.OnFrame, maybe lic server is down";
                Cache.Instance.SessionState = "Quitting";
                Cleanup.CloseQuestor();
            }
        }

        public void DebugCombatMissionsBehaviorStates()
        {
            if (Settings.Instance.DebugStates)
                Logging.Log("CombatMissionsBehavior.State is", _States.CurrentQuestorState.ToString(), Logging.White);
        }

        public void RunOnceAfterStartup()
        {
            if (!_runOnce30SecAfterStartupalreadyProcessed && DateTime.UtcNow > Cache.Instance.QuestorStarted_DateTime.AddSeconds(15) && Cache.Instance.InStation && DateTime.UtcNow > Cache.Instance.LastInSpace.AddSeconds(10))
            {
                if (Settings.Instance.CharacterXMLExists && DateTime.UtcNow > Cache.Instance.NextStartupAction)
                {
                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar) || !string.IsNullOrEmpty(Settings.Instance.LootHangar) && Cache.Instance.InStation)
                    {
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenCorpHangar);
                    }

                    Cache.Instance.DirectEve.Skills.RefreshMySkills();
                    _runOnce30SecAfterStartupalreadyProcessed = true;
                    if (Settings.Instance.UseInnerspace)
                    {
                        Logging.Log("Questor.RunOnce30SecAfterStartup", "Running Innerspace command: WindowText EVE - " + Settings.Instance.CharacterName, Logging.White);
                        LavishScript.ExecuteCommand("WindowText EVE - " + Settings.Instance.CharacterName);

                        //enable windowtaskbar = on, so that minimized windows do not make us die in a fire.
                        Logging.Log("Questor.RunOnce30SecAfterStartup", "Running Innerspace command: timedcommand 100 windowtaskbar on " + Settings.Instance.CharacterName, Logging.White);
                        LavishScript.ExecuteCommand("timedcommand 100 windowtaskbar on " + Settings.Instance.CharacterName);

                        if (Settings.Instance.EVEWindowXSize >= 100 && Settings.Instance.EVEWindowYSize >= 100)
                        {
                            Logging.Log("Questor.RunOnce30SecAfterStartup", "Running Innerspace command: timedcommand 150 WindowCharacteristics -size " + Settings.Instance.EVEWindowXSize + "x" + Settings.Instance.EVEWindowYSize, Logging.White);
                            LavishScript.ExecuteCommand("timedcommand 150 WindowCharacteristics -size " + Settings.Instance.EVEWindowXSize + "x" + Settings.Instance.EVEWindowYSize);
                            Logging.Log("Questor.RunOnce30SecAfterStartup", "Running Innerspace command: timedcommand 200 WindowCharacteristics -pos " + Settings.Instance.EVEWindowXPosition + "," + Settings.Instance.EVEWindowYPosition, Logging.White);
                            LavishScript.ExecuteCommand("timedcommand 200 WindowCharacteristics -pos " + Settings.Instance.EVEWindowXPosition + "," + Settings.Instance.EVEWindowYPosition);
                        }

                        if (Settings.Instance.MinimizeEveAfterStartingUp)
                        {
                            Logging.Log("Questor.RunOnce30SecAfterStartup", "MinimizeEveAfterStartingUp is true: Minimizing EVE with: WindowCharacteristics -visibility minimize", Logging.White);
                            LavishScript.ExecuteCommand("WindowCharacteristics -visibility minimize");
                        }

                        if (Settings.Instance.LoginQuestorArbitraryOSCmd)
                        {
                            Logging.Log("Questor.RunOnce30SecAfterStartup", "After Questor Login: executing LoginQuestorArbitraryOSCmd", Logging.White);
                            LavishScript.ExecuteCommand("Echo [${Time}] OSExecute " + Settings.Instance.LoginQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                            LavishScript.ExecuteCommand("OSExecute " + Settings.Instance.LoginQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                            Logging.Log("Questor.RunOnce30SecAfterStartup", "Done: executing LoginQuestorArbitraryOSCmd", Logging.White);
                        }

                        if (Settings.Instance.LoginQuestorLavishScriptCmd)
                        {
                            Logging.Log("Questor.RunOnce30SecAfterStartup", "After Questor Login: executing LoginQuestorLavishScriptCmd", Logging.White);
                            LavishScript.ExecuteCommand("Echo [${Time}] runscript " + Settings.Instance.LoginQuestorLavishScriptContents.ToString(CultureInfo.InvariantCulture));
                            LavishScript.ExecuteCommand("runscript " + Settings.Instance.LoginQuestorLavishScriptContents.ToString(CultureInfo.InvariantCulture));
                            Logging.Log("Questor.RunOnce30SecAfterStartup", "Done: executing LoginQuestorLavishScriptCmd", Logging.White);
                        }

                        Logging.MaintainConsoleLogs();
                    }
                }
                else
                {
                    Logging.Log("Questor.RunOnce30SecAfterStartup", "RunOnce30SecAfterStartup: Settings.Instance.CharacterName is still null", Logging.Orange);
                    Cache.Instance.NextStartupAction = DateTime.UtcNow.AddSeconds(10);
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
                Logging.Log(whatWeAreTiming, " took " + _watch.ElapsedMilliseconds + "ms", Logging.White);
        }

        public static bool SkillQueueCheck()
        {
            if (DateTime.UtcNow < Cache.Instance.NextSkillsCheckAction)
                return true;

            if (Cache.Instance.DirectEve.HasSupportInstances() && Settings.Instance.ThisToonShouldBeTrainingSkills)
            {
                if (Settings.Instance.DebugSkillTraining) Logging.Log("Questor.SkillQueueCheck", "Current Training Queue Length is [" + Cache.Instance.DirectEve.Skills.SkillQueueLength.ToString() + "]", Logging.White);
                if (Cache.Instance.DirectEve.Skills.SkillQueueLength.TotalDays < 1)
                {
                    Logging.Log("Questor.SkillQueueCheck", "Training Queue currently has room. [" + Math.Round(24 - Cache.Instance.DirectEve.Skills.SkillQueueLength.TotalHours, 2) + " hours free]", Logging.White);
                    _States.LavishEvent_SkillQueueHasRoom();

                    string ScriptPath = System.IO.Path.Combine(Settings.Instance.Path, "../Scripts");
                    if (Settings.Instance.DebugSkillTraining) Logging.Log("Questor.SkillQueueCheck", "Settings.Instance.ScriptPath [" + ScriptPath + "]", Logging.White);
                    string SkillTrainerScriptFullPath = ScriptPath + "\\" + Settings.Instance.SkillTrainerScript;
                    if (!File.Exists(SkillTrainerScriptFullPath))
                    {
                        if (Settings.Instance.DebugSkillTraining) Logging.Log("Questor.SkillQueueCheck", "Missing [" + Settings.Instance.SkillTrainerScript + "] from .Net programs - It is not part of questor and is only available as a binary.", Logging.Teal);
                        return true;
                    }

                    Logging.Log("Questor.SkillQueueCheck", "Questor will now wait 60 seconds. Launching SkillTrainer", Logging.White);
                    _nextQuestorAction = DateTime.UtcNow.AddSeconds(60);
                    //
                    // this eventually needs to be fixed to use the full path, at the moment if we pass SkillTrainerFullPath to innerspace it has 
                    // only 1 slash between directories and they get eaten (directory names with no separating slashes)
                    //
                    LavishScript.ExecuteCommand("echo runscript " + Settings.Instance.SkillTrainerScript);
                    LavishScript.ExecuteCommand("runscript " + Settings.Instance.SkillTrainerScript);
                    return true;
                }
                Logging.Log("Questor.SkillQueueCheck", "Training Queue is full. [" + Math.Round(Cache.Instance.DirectEve.Skills.SkillQueueLength.TotalHours, 2) + " is more than 24 hours]", Logging.White);
                Cache.Instance.NextSkillsCheckAction = DateTime.UtcNow.AddHours(3);
                return true;
            }
            
            if (Settings.Instance.DebugSkillTraining) Logging.Log("Questor.SkillQueueCheck", "if (Cache.Instance.DirectEve.HasSupportInstances() && Settings.Instance.ThisToonShouldBeTrainingSkills)", Logging.White);
            if (Settings.Instance.DebugSkillTraining) Logging.Log("Questor.SkillQueueCheck", "Cache.Instance.DirectEve.HasSupportInstances() [" + Cache.Instance.DirectEve.HasSupportInstances() + "]", Logging.White);
            if (Settings.Instance.DebugSkillTraining) Logging.Log("Questor.SkillQueueCheck", "Settings.Instance.ThisToonShouldBeTrainingSkills [" + Settings.Instance.ThisToonShouldBeTrainingSkills + "]", Logging.White);
            return true;
        }

        public static bool TimeCheck()
        {
            if (DateTime.UtcNow < Cache.Instance.NextTimeCheckAction)
                return false;

            Cache.Instance.NextTimeCheckAction = DateTime.UtcNow.AddSeconds(90);
            Logging.Log("Questor", "Checking: Current time [" + DateTime.Now.ToString(CultureInfo.InvariantCulture) +
                        "] StopTimeSpecified [" + Cache.Instance.StopTimeSpecified +
                        "] StopTime [ " + Cache.Instance.StopTime +
                        "] ManualStopTime = " + Cache.Instance.ManualStopTime, Logging.White);

            if (DateTime.Now.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes > Cache.Instance.MaxRuntime)
            {
                // quit questor
                Logging.Log("Questor", "Maximum runtime exceeded.  Quitting...", Logging.White);
                Cache.Instance.ReasonToStopQuestor = "Maximum runtime specified and reached.";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                Cleanup.BeginClosingQuestor();
                return true;
            }

            if (Cache.Instance.StopTimeSpecified)
            {
                if (DateTime.Now >= Cache.Instance.StopTime)
                {
                    Logging.Log("Questor", "Time to stop. StopTimeSpecified and reached. Quitting game.", Logging.White);
                    Cache.Instance.ReasonToStopQuestor = "StopTimeSpecified and reached.";
                    Settings.Instance.AutoStart = false;
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                    Cleanup.BeginClosingQuestor();
                    return true;
                }
            }

            if (DateTime.Now >= Cache.Instance.ManualRestartTime)
            {
                Logging.Log("Questor", "Time to stop. ManualRestartTime reached. Quitting game.", Logging.White);
                Cache.Instance.ReasonToStopQuestor = "ManualRestartTime reached.";
                Settings.Instance.AutoStart = true;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                Cleanup.BeginClosingQuestor();
                return true;
            }

            if (DateTime.Now >= Cache.Instance.ManualStopTime)
            {
                Logging.Log("Questor", "Time to stop. ManualStopTime reached. Quitting game.", Logging.White);
                Cache.Instance.ReasonToStopQuestor = "ManualStopTime reached.";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                Cleanup.BeginClosingQuestor();
                return true;
            }

            if (Cache.Instance.ExitWhenIdle)
            {
                Logging.Log("Questor", "ExitWhenIdle set to true.  Quitting game.", Logging.White);
                Cache.Instance.ReasonToStopQuestor = "ExitWhenIdle set to true";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                Cleanup.BeginClosingQuestor();
                return true;
            }

            if (Cache.Instance.MissionsThisSession > Cache.Instance.StopSessionAfterMissionNumber)
            {
                Logging.Log("Questor", "MissionsThisSession [" + Cache.Instance.MissionsThisSession + "] is greater than StopSessionAfterMissionNumber [" + Cache.Instance.StopSessionAfterMissionNumber + "].  Quitting game.", Logging.White);
                Cache.Instance.ReasonToStopQuestor = "MissionsThisSession > StopSessionAfterMissionNumber";
                Settings.Instance.AutoStart = false;
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.SessionState = "Exiting";
                Cleanup.BeginClosingQuestor();
                return true;
            }
            return false;
        }

        public static void WalletCheck()
        {
            if (_States.CurrentQuestorState == QuestorState.Mining ||
                _States.CurrentQuestorState == QuestorState.CombatHelperBehavior ||
                _States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior)
                //_States.CurrentQuestorState == QuestorState.BackgroundBehavior)
            {
                return;
            }

            Cache.Instance.LastWalletCheck = DateTime.UtcNow;

            //Logging.Log("[Questor] Wallet Balance Debug Info: LastKnownGoodConnectedTime = " + Settings.Instance.lastKnownGoodConnectedTime);
            //Logging.Log("[Questor] Wallet Balance Debug Info: DateTime.UtcNow - LastKnownGoodConnectedTime = " + DateTime.UtcNow.Subtract(Settings.Instance.LastKnownGoodConnectedTime).TotalSeconds);
            if (Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) > 1)
            {
                Logging.Log("Questor", String.Format("Wallet Balance Has Not Changed in [ {0} ] minutes.",
                                          Math.Round(
                                              DateTime.UtcNow.Subtract(Cache.Instance.LastKnownGoodConnectedTime).
                                                  TotalMinutes, 0)), Logging.White);
            }

            //Settings.Instance.WalletBalanceChangeLogOffDelay = 2;  //used for debugging purposes
            //Logging.Log("Cache.Instance.lastKnownGoodConnectedTime is currently: " + Cache.Instance.LastKnownGoodConnectedTime);
            if (Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) < Settings.Instance.WalletBalanceChangeLogOffDelay)
            {
                try
                {
                    if ((long)Cache.Instance.MyWalletBalance != (long)Cache.Instance.DirectEve.Me.Wealth)
                    {
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                        Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                    }
                }
                catch (Exception exception)
                {
                    Logging.Log("Questor.WalletCheck", "Checking my wallet balance caused an exception [" + exception + "]", Logging.White);
                }
            }
            else if (Settings.Instance.WalletBalanceChangeLogOffDelay != 0)
            {
                if ((Cache.Instance.InStation) || (Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes) > Settings.Instance.WalletBalanceChangeLogOffDelay + 5))
                {
                    Logging.Log("Questor", String.Format("Questor: Wallet Balance Has Not Changed in [ {0} ] minutes. Switching to QuestorState.CloseQuestor", Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes, 0)), Logging.White);
                    Cache.Instance.ReasonToStopQuestor = "Wallet Balance did not change for over " + Settings.Instance.WalletBalanceChangeLogOffDelay + "min";
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.SessionState = "Exiting";
                    Cleanup.BeginClosingQuestor();
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

        public bool OnframeProcessEveryPulse()
        {
            if (_directEve.Login.AtLogin)
            {
                return false;
            }

            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            Cache.Instance.LastFrame = DateTime.UtcNow;

            // Only pulse state changes every 1.5s
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < Time.Instance.QuestorPulse_milliseconds) //default: 1500ms
            {
                return false;
            }

            _lastPulse = DateTime.UtcNow;

            if (Cache.Instance.SessionState != "Quitting")
            {
                // Update settings (settings only load if character name changed)
                if (!Settings.Instance.DefaultSettingsLoaded)
                {
                    Settings.Instance.LoadSettings();
                }
            }

            if (DateTime.Now < Cache.Instance.QuestorStarted_DateTime.AddSeconds(30))
            {
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
            }

            // Start _cleanup.ProcessState
            // Description: Closes Windows, and eventually other things considered 'cleanup' useful to more than just Questor(Missions) but also Anomalies, Mining, etc
            //
            DebugPerformanceClearandStartTimer();
            _cleanup.ProcessState();
            DebugPerformanceStopandDisplayTimer("Cleanup.ProcessState");

            if (Settings.Instance.DebugStates)
                Logging.Log("Cleanup.State is", _States.CurrentCleanupState.ToString(), Logging.White);

            // Done
            // Cleanup State: ProcessState

            // Session is not ready yet, do not continue
            if (!Cache.Instance.DirectEve.Session.IsReady)
            {
                return false;
            }

            if (Cache.Instance.DirectEve.Session.IsReady)
            {
                Cache.Instance.LastSessionIsReady = DateTime.UtcNow;
            }

            // We are not in space or station, don't do shit yet!
            if (!Cache.Instance.InSpace && !Cache.Instance.InStation)
            {
                Cache.Instance.NextInSpaceorInStation = DateTime.UtcNow.AddSeconds(12);
                Cache.Instance.LastSessionChange = DateTime.UtcNow;
                return false;
            }

            if (DateTime.UtcNow < Cache.Instance.NextInSpaceorInStation)
            {
                if (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Capsule)
                {
                    Logging.Log("Panic", "We are in a pod. Don't wait for the session wait timer to expire!", Logging.Red);
                    Cache.Instance.NextInSpaceorInStation = DateTime.UtcNow;
                    return true;
                }
                return false;
            }

            // Check 3D rendering
            if (Cache.Instance.DirectEve.Session.IsInSpace && Cache.Instance.DirectEve.Rendering3D != !Settings.Instance.Disable3D)
            {
                Cache.Instance.DirectEve.Rendering3D = !Settings.Instance.Disable3D;
            }

            if (DateTime.UtcNow.Subtract(Cache.Instance.LastUpdateOfSessionRunningTime).TotalSeconds < Time.Instance.SessionRunningTimeUpdate_seconds)
            {
                Cache.Instance.SessionRunningTime = (int)DateTime.UtcNow.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes;
                Cache.Instance.LastUpdateOfSessionRunningTime = DateTime.UtcNow;
            }
            return true;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (!OnframeProcessEveryPulse()) return;
            if (Settings.Instance.DebugOnframe) Logging.Log("Questor", "Onframe: this is Questor.cs [" + DateTime.UtcNow + "] by default the next pulse will be in [" + Time.Instance.QuestorPulse_milliseconds + "]milliseconds", Logging.Teal);

            RunOnceAfterStartup();

            if (!Cache.Instance.Paused)
            {
                if (DateTime.UtcNow.Subtract(Cache.Instance.LastWalletCheck).TotalMinutes > Time.Instance.WalletCheck_minutes && !Settings.Instance.DefaultSettingsLoaded)
                {
                    WalletCheck();
                }
            }

            // We always check our defense state if we're in space, regardless of questor state
            // We also always check panic
            if ((Cache.Instance.LastInSpace.AddSeconds(2) > DateTime.UtcNow) && Cache.Instance.InSpace)
            {
                DebugPerformanceClearandStartTimer();
                if (!Cache.Instance.DoNotBreakInvul)
                {
                    _defense.ProcessState();
                }
                DebugPerformanceStopandDisplayTimer("Defense.ProcessState");
            }

            if (Cache.Instance.Paused || DateTime.UtcNow < _nextQuestorAction)
            {
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                Cache.Instance.GotoBaseNow = false;
                Cache.Instance.SessionState = string.Empty;
                return;
            }

            if (Cache.Instance.SessionState == "Quitting")
            {
                if (_States.CurrentQuestorState != QuestorState.CloseQuestor)
                {
                    Cleanup.BeginClosingQuestor();
                }
            }

            // When in warp there's nothing we can do, so ignore everything
            if (Cache.Instance.InWarp)
            {
                return;
            }

            switch (_States.CurrentQuestorState)
            {
                case QuestorState.Idle:
                    if (TimeCheck()) return; //Should we close questor due to stoptime or runtime?
                    
                    if (!SkillQueueCheck()) return; //Should we 'pause' questor for a few while an external app trains skills?

                    if (Cache.Instance.StopBot)
                    {
                        if (Settings.Instance.DebugIdle) Logging.Log("Questor", "Cache.Instance.StopBot = true - this is set by the localwatch code so that we stay in station when local is unsafe", Logging.Orange);
                        return;
                    }

                    if (_States.CurrentQuestorState == QuestorState.Idle && Settings.Instance.CharacterMode != "none" && Settings.Instance.CharacterName != null)
                    {
                        _States.CurrentQuestorState = QuestorState.Start;
                        return;
                    }

                    Logging.Log("Questor", "Settings.Instance.CharacterMode = [" + Settings.Instance.CharacterMode + "]", Logging.Orange);
                    _States.CurrentQuestorState = QuestorState.Error;
                    break;

                case QuestorState.CombatMissionsBehavior:

                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    _combatMissionsBehavior.ProcessState();
                    break;

                case QuestorState.CombatHelperBehavior:

                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    _combatHelperBehavior.ProcessState();
                    break;

                case QuestorState.DedicatedBookmarkSalvagerBehavior:

                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    _dedicatedBookmarkSalvagerBehavior.ProcessState();
                    break;

                case QuestorState.DirectionalScannerBehavior:

                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    _directionalScannerBehavior.ProcessState();
                    break;

                case QuestorState.DebugHangarsBehavior:

                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    _debugHangarsBehavior.ProcessState();
                    break;

                case QuestorState.DebugReloadAll:
                    if (!Combat.ReloadAll(Cache.Instance.EntitiesNotSelf.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < (double)Distance.OnGridWithMe))) return;
                    _States.CurrentQuestorState = QuestorState.Start;
                    break;

                case QuestorState.Mining:
                    _miningBehavior.ProcessState();
                    break;

                case QuestorState.Start:
                    switch (Settings.Instance.CharacterMode.ToLower())
                    {
                        case "combat missions":
                        case "combat_missions":
                        case "dps":
                            Logging.Log("Questor", "Start Mission Behavior", Logging.White);
                            _States.CurrentQuestorState = QuestorState.CombatMissionsBehavior;
                            break;

                        case "salvage":
                            Logging.Log("Questor", "Start Salvaging Behavior", Logging.White);
                            _States.CurrentQuestorState = QuestorState.DedicatedBookmarkSalvagerBehavior;
                            break;

                        case "mining":
                            Logging.Log("Questor", "Start Mining Behavior", Logging.White);
                            _States.CurrentQuestorState = QuestorState.Mining;
                            _States.CurrentMiningState = MiningState.Start;
                            break;

                        case "combat helper":
                        case "combat_helper":
                        case "combathelper":
                            Logging.Log("Questor", "Start CombatHelper Behavior", Logging.White);
                            _States.CurrentQuestorState = QuestorState.CombatHelperBehavior;
                            break;

                        case "custom":
                            Logging.Log("Questor", "Start Custom Behavior", Logging.White);
                            //_States.CurrentQuestorState = QuestorState.BackgroundBehavior;
                            break;

                        case "directionalscanner":
                            Logging.Log("Questor", "Start DirectionalScanner Behavior", Logging.White);
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
                    Logging.Log("Questor", "CloseQuestorCMDUplinkInnerspaceProfile: " + Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile, Logging.White);
                    Logging.Log("Questor", "CloseQuestorCMDUplinkISboxerCharacterSet: " + Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet, Logging.White);
                    Logging.Log("Questor", "CloseQuestorArbitraryOSCmd" + Settings.Instance.CloseQuestorArbitraryOSCmd, Logging.White);
                    Logging.Log("Questor", "CloseQuestorOSCmdContents" + Settings.Instance.CloseQuestorOSCmdContents, Logging.White);
                    Logging.Log("Questor", "WalletBalanceChangeLogOffDelay: " + Settings.Instance.WalletBalanceChangeLogOffDelay, Logging.White);
                    Logging.Log("Questor", "WalletBalanceChangeLogOffDelayLogoffOrExit: " + Settings.Instance.WalletBalanceChangeLogOffDelayLogoffOrExit, Logging.White);
                    Logging.Log("Questor", "EVEProcessMemoryCeiling: " + Settings.Instance.EVEProcessMemoryCeiling, Logging.White);
                    Logging.Log("Questor", "EVEProcessMemoryCeilingLogofforExit: " + Settings.Instance.EVEProcessMemoryCeilingLogofforExit, Logging.White);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorCMDExitGame: " + Cache.Instance.CloseQuestorCMDExitGame, Logging.White);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorCMDLogoff: " + Cache.Instance.CloseQuestorCMDLogoff, Logging.White);
                    Logging.Log("Questor", "Cache.Instance.CloseQuestorEndProcess: " + Cache.Instance.CloseQuestorEndProcess, Logging.White);
                    Logging.Log("Questor", "Cache.Instance.EnteredCloseQuestor_DateTime: " + Cache.Instance.EnteredCloseQuestor_DateTime.ToShortTimeString(), Logging.White);
                    _States.CurrentQuestorState = QuestorState.Error;
                    return;

                case QuestorState.DebugWindows:
                    List<DirectWindow> windows = Cache.Instance.DirectEve.Windows;

                    if (windows != null && windows.Any())
                    {
                        foreach (DirectWindow window in windows)
                        {
                            Logging.Log("Questor", "--------------------------------------------------", Logging.Orange);
                            Logging.Log("Questor", "Debug_Window.Name: [" + window.Name + "]", Logging.White);
                            Logging.Log("Questor", "Debug_Window.Caption: [" + window.Caption + "]", Logging.White);
                            Logging.Log("Questor", "Debug_Window.Type: [" + window.Type + "]", Logging.White);
                            Logging.Log("Questor", "Debug_Window.IsModal: [" + window.IsModal + "]", Logging.White);
                            Logging.Log("Questor", "Debug_Window.IsDialog: [" + window.IsDialog + "]", Logging.White);
                            Logging.Log("Questor", "Debug_Window.Id: [" + window.Id + "]", Logging.White);
                            Logging.Log("Questor", "Debug_Window.IsKillable: [" + window.IsKillable + "]", Logging.White);
                            Logging.Log("Questor", "Debug_Window.Html: [" + window.Html + "]", Logging.White);
                        }

                        //Logging.Log("Questor", "Debug_InventoryWindows", Logging.White);
                        //foreach (DirectWindow window in windows)
                        //{
                        //    if (window.Type.Contains("inventory"))
                        //    {
                        //        Logging.Log("Questor", "Debug_Window.Name: [" + window.Name + "]", Logging.White);
                        //        Logging.Log("Questor", "Debug_Window.Type: [" + window.Type + "]", Logging.White);
                        //        Logging.Log("Questor", "Debug_Window.Caption: [" + window.Caption + "]", Logging.White);
                        //        //Logging.Log("Questor", "Debug_Window.Type: [" + window. + "]", Logging.White);
                        //    }
                        //}
                    }
                    else
                    {
                        Logging.Log("Questor", "DebugWindows: No Windows Found", Logging.White);
                    }
                    _States.CurrentQuestorState = QuestorState.Error;
                    return;

                case QuestorState.DebugInventoryTree:

                    if (Cache.Instance.PrimaryInventoryWindow.ExpandCorpHangarView())
                    {
                        Logging.Log("DebugInventoryTree", "ExpandCorpHangar executed", Logging.Teal);
                    }
                    Logging.Log("DebugInventoryTree", "--------------------------------------------------", Logging.Orange);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Name: [" + Cache.Instance.PrimaryInventoryWindow.Name + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Caption: [" + Cache.Instance.PrimaryInventoryWindow.Caption + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Type: [" + Cache.Instance.PrimaryInventoryWindow.Type + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsModal: [" + Cache.Instance.PrimaryInventoryWindow.IsModal + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsDialog: [" + Cache.Instance.PrimaryInventoryWindow.IsDialog + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.Id: [" + Cache.Instance.PrimaryInventoryWindow.Id + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsKillable: [" + Cache.Instance.PrimaryInventoryWindow.IsKillable + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.IsReady: [" + Cache.Instance.PrimaryInventoryWindow.IsReady + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.LocationFlag: [" + Cache.Instance.PrimaryInventoryWindow.LocationFlag + "]", Logging.White);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.currInvIdName: " + Cache.Instance.PrimaryInventoryWindow.currInvIdName, Logging.Red);
                    Logging.Log("DebugInventoryTree", "InventoryWindow.currInvIdName: " + Cache.Instance.PrimaryInventoryWindow.currInvIdItem, Logging.Red);

                    foreach (Int64 itemInTree in Cache.Instance.IDsinInventoryTree)
                    {
                        if (Cache.Instance.PrimaryInventoryWindow.GetIdsFromTree(false).Contains(itemInTree))
                        {
                            Cache.Instance.PrimaryInventoryWindow.SelectTreeEntryByID(itemInTree);
                            Cache.Instance.IDsinInventoryTree.Remove(itemInTree);
                            break;
                        }
                    }
                    break;

                //case QuestorState.BackgroundBehavior:

                    //
                    // QuestorState will stay here until changed externally by the behavior we just kicked into starting
                    //
                    //_backgroundbehavior.ProcessState();
                    break;
            }
        }
    }
}