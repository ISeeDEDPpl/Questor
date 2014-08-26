

namespace Questor.Modules.BackgroundTasks
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using DirectEve;
    using LavishScriptAPI;
    using global::Questor.Modules.Activities;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Combat;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;

    public static class Cleanup
    {
        private static DateTime _lastCleanupAction;
        private static DateTime _lastCleanupProcessState;
        private static int _droneBayClosingAttempts;
        private static bool MemoryManagerHasBeenRunThisIteration;
        private static DateTime CloseQuestorDelay { get; set; }

        private static bool _closeQuestor10SecWarningDone;
        private static bool _closeQuestorCMDUplink = true;
        public static bool CloseQuestorFlag = true;
        private static bool FoundDuelInvitation;
        private static DateTime FoundDuelInvitationTime = DateTime.UtcNow.AddDays(-1);
        public static string ReasonToStopQuestor { get; set; }
        public static string SessionState { get; set; }
        public static bool SignalToQuitQuestorAndEVEAndRestartInAMoment { get; set; }
        public static bool SignalToQuitQuestor { get; set; }
        
        public static void BeginClosingQuestor()
        {
            Time.EnteredCloseQuestor_DateTime = DateTime.UtcNow;
            Cleanup.SessionState = "BeginClosingQuestor";
            SignalToQuitQuestorAndEVEAndRestartInAMoment = true;
            _States.CurrentQuestorState = QuestorState.CloseQuestor;
        }

        public static void DirecteveDispose()
        {
            Logging.Log("Questor", "started calling DirectEve.Dispose()", Logging.White);
            Cache.Instance.DirectEve.Dispose(); //could this hang?
            Logging.Log("Questor", "finished calling DirectEve.Dispose()", Logging.White);
            //
            // if Cleanup.SessionState is "Quitting!!" then we know for certain we are scheduling a restart, NOT just exiting questor (and possibly trying to restart it)
            //
            if (Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment)
            {
                Process.GetCurrentProcess().Kill();
                Environment.Exit(0);    
            }
            
            Cleanup.SignalToQuitQuestor = true;
        }

        public static bool CloseQuestor(string Reason, bool restart = false)
        {
            // 30 seconds + 1 to 60 seconds + 1 to 60 seconds before restarting (this should make each instance a bit more spread out over 2 min)
            int secRestart = Cache.Instance.RandomNumber(Time.Instance.ReLogDelayMinimum_seconds * 10, Time.Instance.ReLogDelayMaximum_seconds * 10);

            // so that IF we changed the state we would not be caught in a loop of re-entering QuestorState.CloseQuestor
            // keep in mind that CloseQuestor() itself DOES need to run multiple times across multiple iterations 
            // (roughly 20x before the timer expires and we actually close questor)
            SessionState = "Quitting!!"; 
            
            if (!Cache.Instance.CloseQuestorCMDLogoff && !Cache.Instance.CloseQuestorCMDExitGame)
            {
                Cache.Instance.CloseQuestorCMDExitGame = true;
            }

            //if (_traveler.State == TravelerState.Idle)
            //{
            //    Logging.Log(
            //        "QuestorState.CloseQuestor: Entered Traveler - making sure we will be docked at Home Station");
            //}
            //AvoidBumpingThings();
            //TravelToAgentsStation();

            //if (_traveler.State == TravelerState.AtDestination ||
            //    DateTime.UtcNow.Subtract(Time.EnteredCloseQuestor_DateTime).TotalSeconds >
            //   Settings.Instance.SecondstoWaitAfterExitingCloseQuestorBeforeExitingEVE)
            //{
            //Logging.Log("QuestorState.CloseQuestor: At Station: Docked");
            // Write to Session log
            if (!Statistics.WriteSessionLogClosing()) return false;

            if ((Settings.Instance.AutoStart && Settings.Instance.CloseQuestorAllowRestart) || restart)
            //if autostart is disabled do not schedule a restart of questor - let it stop gracefully.
            {
                if (Cache.Instance.CloseQuestorCMDLogoff)
                {
                    if (CloseQuestorFlag)
                    {
                        Logging.Log("Questor", "Logging off EVE: EVE and questor should restart on their own when the client comes back up", Logging.White);
                        if (Logging.UseInnerspace)
                        {
                            LavishScript.ExecuteCommand("uplink echo Logging off EVE:  \\\"${Game}\\\" \\\"${Profile}\\\"");
                        }
                        Logging.Log("Questor", "you can change this option by setting the wallet and EveProcessMemoryCeiling options to use exit instead of logoff: see the settings.xml file", Logging.White);
                        Logging.Log("Questor", "Logging Off eve in 15 seconds.", Logging.White);
                        CloseQuestorFlag = false;
                        CloseQuestorDelay = DateTime.UtcNow.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                    }

                    if (!_closeQuestor10SecWarningDone)
                    {
                        _closeQuestor10SecWarningDone = true;
                        Logging.Log("Questor", "Exiting eve in [" + Time.Instance.CloseQuestorDelayBeforeExit_seconds + "] seconds", Logging.White);
                        return false;
                    }

                    if (DateTime.UtcNow > CloseQuestorDelay)
                    {
                        Logging.Log("Questor", "Exiting eve now.", Logging.White);
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdLogOff);
                    }
                    return true;
                }

                if (Cache.Instance.CloseQuestorCMDExitGame)
                {
                    if (Logging.UseInnerspace)
                    {
                        //Logging.Log("Questor: We are in station: Exit option has been configured.");
                        if (((Settings.Instance.CloseQuestorArbitraryOSCmd) && (Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet)) ||
                             (Settings.Instance.CloseQuestorArbitraryOSCmd) && (Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile))
                        {
                            Logging.Log("Questor", "You can't combine CloseQuestorArbitraryOSCmd with either of the other two options, fix your settings", Logging.White);
                            return false;
                        }

                        if ((Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet) && (Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile))
                        {
                            Logging.Log("Questor", "You cant use both the CloseQuestorCMDUplinkIsboxerProfile and the CloseQuestorCMDUplinkIsboxerProfile setting, choose one", Logging.White);
                            return false;
                        }

                        if (Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile)

                        //if configured as true we will use the innerspace profile to restart this session
                        {
                            //Logging.Log("Questor: We are in station: CloseQuestorCMDUplinkInnerspaceProfile is ["+ CloseQuestorCMDUplinkInnerspaceProfile.tostring() +"]");
                            if (_closeQuestorCMDUplink)
                            {
                                Logging.Log("Questor", "Starting a timer in the innerspace uplink to restart this innerspace profile session", Logging.White);
                                LavishScript.ExecuteCommand("uplink exec Echo [${Time}] " + Settings.Instance.CharacterName + "'s Questor is starting a timedcommand to restart itself in a moment");
                                LavishScript.ExecuteCommand("uplink exec Echo [${Time}] timedcommand " + secRestart + " open \\\"${Game}\\\" \\\"${Profile}\\\"");
                                LavishScript.ExecuteCommand("uplink exec timedcommand " + secRestart + " open \\\"${Game}\\\" \\\"${Profile}\\\"");
                                Logging.Log("Questor", "Done: quitting this session so the new innerspace session can take over", Logging.White);
                                Logging.Log("Questor", "Exiting eve in 15 seconds.", Logging.White);
                                _closeQuestorCMDUplink = false;
                                CloseQuestorDelay = DateTime.UtcNow.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                            }

                            if (!_closeQuestor10SecWarningDone)
                            {
                                _closeQuestor10SecWarningDone = true;
                                Logging.Log("Questor", "Exiting eve in [" + Time.Instance.CloseQuestorDelayBeforeExit_seconds + "] seconds", Logging.White);
                                return false;
                            }

                            if (DateTime.UtcNow > CloseQuestorDelay)
                            {
                                Logging.Log("Questor", "Exiting eve now.", Logging.White);
                                DirecteveDispose();
                                return false;
                            }
                            return false;
                        }

                        if (Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet) //if configured as true we will use isboxer to restart this session
                        {
                            //Logging.Log("Questor: We are in station: CloseQuestorCMDUplinkIsboxerProfile is ["+ CloseQuestorCMDUplinkIsboxerProfile.tostring() +"]");
                            if (_closeQuestorCMDUplink)
                            {
                                Logging.Log("Questor", "Starting a timer in the innerspace uplink to restart this isboxer character set", Logging.White);
                                LavishScript.ExecuteCommand("uplink exec Echo [${Time}] " + Settings.Instance.CharacterName + "'s Questor is starting a timedcommand to restart itself in a moment");
                                LavishScript.ExecuteCommand("uplink exec Echo [${Time}] timedcommand " + secRestart + " runscript isboxer -launchslot \\\"${ISBoxerCharacterSet}\\\" \\\"${ISBoxerSlot}\\\"");
                                LavishScript.ExecuteCommand("uplink timedcommand " + secRestart + " runscript isboxer -launchslot \\\"${ISBoxerCharacterSet}\\\" \\\"${ISBoxerSlot}\\\"");
                                Logging.Log("Questor", "Done: quitting this session so the new isboxer session can take over", Logging.White);
                                Logging.Log("Questor", "Exiting eve.", Logging.White);
                                _closeQuestorCMDUplink = false;
                                CloseQuestorDelay = DateTime.UtcNow.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                            }

                            if (!_closeQuestor10SecWarningDone)
                            {
                                _closeQuestor10SecWarningDone = true;
                                Logging.Log("Questor", "Exiting eve in [" + Time.Instance.CloseQuestorDelayBeforeExit_seconds + "] seconds", Logging.White);
                                return false;
                            }

                            if (DateTime.UtcNow > CloseQuestorDelay)
                            {
                                Logging.Log("Questor", "Exiting eve now.", Logging.White);
                                DirecteveDispose();
                                return false;
                            }
                            return false;
                        }

                        if (Settings.Instance.CloseQuestorArbitraryOSCmd) // will execute an arbitrary OS command through the IS Uplink
                        {
                            if (_closeQuestorCMDUplink)
                            {
                                Logging.Log("Questor", "Starting a timer in the innerspace uplink to execute an arbitrary OS command", Logging.White);
                                LavishScript.ExecuteCommand("uplink exec Echo [${Time}] " + Settings.Instance.CharacterName + "'s Questor is starting a timedcommand to restart itself in a moment");
                                LavishScript.ExecuteCommand("uplink exec Echo [${Time}] timedcommand " + secRestart + " OSExecute " + Settings.Instance.CloseQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                                LavishScript.ExecuteCommand("uplink exec timedcommand " + secRestart + " OSExecute " + Settings.Instance.CloseQuestorOSCmdContents.ToString(CultureInfo.InvariantCulture));
                                Logging.Log("Questor", "Done: quitting this session", Logging.White);
                                Logging.Log("Questor", "Exiting eve in 15 seconds.", Logging.White);
                                _closeQuestorCMDUplink = false;
                                CloseQuestorDelay = DateTime.UtcNow.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                            }

                            if (!_closeQuestor10SecWarningDone)
                            {
                                _closeQuestor10SecWarningDone = true;
                                Logging.Log("Questor", "Exiting eve in [" + Time.Instance.CloseQuestorDelayBeforeExit_seconds + "] seconds", Logging.White);
                                return false;
                            }

                            if (DateTime.UtcNow > CloseQuestorDelay)
                            {
                                Logging.Log("Questor", "Exiting eve now.", Logging.White);
                                DirecteveDispose();
                                return false;
                            }
                            return false;
                        }

                        if (!Settings.Instance.CloseQuestorCMDUplinkInnerspaceProfile && !Settings.Instance.CloseQuestorCMDUplinkIsboxerCharacterSet && !Settings.Instance.CloseQuestorArbitraryOSCmd)
                        {
                            Logging.Log("Questor", "CloseQuestorArbitraryOSCmd, CloseQuestorCMDUplinkInnerspaceProfile and CloseQuestorCMDUplinkIsboxerProfile all false", Logging.White);
                            if (_closeQuestorCMDUplink)
                            {
                                _closeQuestorCMDUplink = false;
                                CloseQuestorDelay = DateTime.UtcNow.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                            }

                            if (!_closeQuestor10SecWarningDone)
                            {
                                _closeQuestor10SecWarningDone = true;
                                Logging.Log("Questor", "Exiting eve in [" + Time.Instance.CloseQuestorDelayBeforeExit_seconds + "] seconds", Logging.White);
                                return false;
                            }

                            if (DateTime.UtcNow > CloseQuestorDelay)
                            {
                                Logging.Log("Questor", "Exiting eve now.", Logging.White);
                                DirecteveDispose();
                                return false;
                            }
                            return false;
                        }
                    }
                    else
                    {
                        Logging.Log("Questor", "CloseQuestor: We are configured to NOT use innerspace. useInnerspace = false", Logging.White);
                        Logging.Log("Questor", "CloseQuestor: Currently the questor will exit (and not restart itself) in this configuration, this likely needs additional work to make questor reentrant so we can use a scheduled task?!", Logging.White);
                        if (_closeQuestorCMDUplink)
                        {
                            _closeQuestorCMDUplink = false;
                            CloseQuestorDelay = DateTime.UtcNow.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
                        }

                        if (!_closeQuestor10SecWarningDone)
                        {
                            _closeQuestor10SecWarningDone = true;
                            Logging.Log("Questor", "Exiting eve in [" + Time.Instance.CloseQuestorDelayBeforeExit_seconds + "] seconds", Logging.White);
                            return false;
                        }

                        if (DateTime.UtcNow > CloseQuestorDelay)
                        {
                            Logging.Log("Questor", "Exiting eve now.", Logging.White);
                            DirecteveDispose();
                            return false;
                        }
                    }
                }
            }
            if (_closeQuestorCMDUplink)
            {
                Logging.Log("Questor", "Autostart is false: Stopping EVE with quit command (if EVE is going to restart it will do so externally)", Logging.White);
                _closeQuestorCMDUplink = false;
                CloseQuestorDelay = DateTime.UtcNow.AddSeconds(Time.Instance.CloseQuestorDelayBeforeExit_seconds);
            }

            if (!_closeQuestor10SecWarningDone)
            {
                _closeQuestor10SecWarningDone = true;
                Logging.Log("Questor", "Exiting eve in [" + Time.Instance.CloseQuestorDelayBeforeExit_seconds + "] seconds", Logging.White);
                return false;
            }

            if (DateTime.UtcNow > CloseQuestorDelay)
            {
                Logging.Log("Questor", "Closing with: Process.GetCurrentProcess().Kill()", Logging.White);
                DirecteveDispose();
                return false;
            }
            return false;
        }

        public static bool CloseInventoryWindows()
        {
            try
            {
                if (DateTime.UtcNow < _lastCleanupAction.AddMilliseconds(500))
                    return false;

                if (!Cache.Instance.Windows.Any())
                {
                    return false;
                }

                _lastCleanupAction = DateTime.UtcNow;

                //
                // go through *every* window
                //
                foreach (DirectWindow window in Cache.Instance.Windows)
                {
                    if (window.Name.Contains("_ShipDroneBay_") && window.Caption.Contains("Drone Bay") && window.Type.Contains("Inventory"))
                    {
                        Logging.Log("Cleanup", "CloseInventoryWindows: Closing Drone Bay Window", Logging.White);
                        window.Close();
                        _lastCleanupAction = DateTime.UtcNow;
                        return false;
                    }

                    if (window.Name.Contains("_ShipCargo_") && window.Caption.Contains("active ship") && window.Type.Contains("Inventory"))
                    {
                        Logging.Log("Cleanup", "CloseInventoryWindows: Closing Cargo Bay Window", Logging.White);
                        window.Close();
                        _lastCleanupAction = DateTime.UtcNow;
                        return false;
                    }

                    if (window.Name.Contains("_StationItems_") && window.Caption.Contains("Item hangar") && window.Type.Contains("Inventory"))
                    {
                        Logging.Log("Cleanup", "CloseInventoryWindows: Closing Item Hangar Window", Logging.White);
                        window.Close();
                        _lastCleanupAction = DateTime.UtcNow;
                        return false;
                    }

                    if (window.Name.Contains("_StationShips_") && window.Caption.Contains("Ship hangar") && window.Type.Contains("Inventory"))
                    {
                        Logging.Log("Cleanup", "CloseInventoryWindows: Closing Ship Hangar Window", Logging.White);
                        window.Close();
                        _lastCleanupAction = DateTime.UtcNow;
                        return false;
                    }

                    if (window.Type.Contains("Inventory"))
                    {
                        Logging.Log("Cleanup", "CloseInventoryWindows: Closing other Inventory Window named [ " + window.Name + "]", Logging.White);
                        window.Close();
                        _lastCleanupAction = DateTime.UtcNow;
                        return false;
                    }

                    //
                    // add ship hangar, items hangar, corp hangar, etc... as at least come of those may be open in space (pos?) or may someday be bugged by ccp.
                    // add repairship, lpstore, marketwindow, etc
                    //
                }

                return true;
            }
            catch (System.Exception ex)
            {
                Logging.Log("Cleanup", "Exception [" + ex + "]", Logging.White);
                return false;
            }
        }

        public static void CheckEVEStatus()
        {
            try
            {
                // get the current process
                Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();

                // get the physical mem usage (this only runs between missions)
                if (currentProcess.WorkingSet64 != 0)
                {
                    Cache.Instance.TotalMegaBytesOfMemoryUsed = ((currentProcess.WorkingSet64 / 1024) / 1024);
                }
                Logging.Log("Cleanup", "EVE instance: totalMegaBytesOfMemoryUsed - " + Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.White);

                if (Cache.Instance.TotalMegaBytesOfMemoryUsed > (Settings.Instance.EVEProcessMemoryCeiling - 50))
                {
                    Logging.Log("Cleanup", "Memory usage is above the EVEProcessMemoryCeiling threshold. EVE instance: totalMegaBytesOfMemoryUsed - " + Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.White);
                    Cleanup.ReasonToStopQuestor = "Memory usage is above the EVEProcessMemoryCeiling threshold. EVE instance: totalMegaBytesOfMemoryUsed - " + Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB";
                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cleanup.SessionState = "Exiting";
                    BeginClosingQuestor();
                    return;
                }
                
                Cleanup.SessionState = "Running";
            }
            catch (System.Exception ex)
            {
                Logging.Log("Cleanup", "Exception [" + ex + "]", Logging.White);
            }
            
        }

        public static void ProcessState()
        {
            if (DateTime.UtcNow < _lastCleanupProcessState.AddMilliseconds(100) || Logging.DebugDisableCleanup) //if it has not been 100ms since the last time we ran this ProcessState return. We can't do anything that close together anyway
                return;

            _lastCleanupProcessState = DateTime.UtcNow;

            if (DateTime.UtcNow < Time.Instance.LastSessionChange.AddSeconds(10))
            {
                if (Logging.DebugCleanup) Logging.Log("Cleanup", "last session change was at [" + Time.Instance.LastSessionChange + "] waiting until 20 sec have passed", Logging.Teal);
                return;
            }

            if (Cache.Instance.InSpace)
            {
                // When in warp there's nothing we can do, so ignore everything
                if (Cache.Instance.InWarp)
                {
                    if (Logging.DebugCleanup) Logging.Log("Cleanup", "Processstate: we are in warp: do nothing", Logging.Teal);
                    _States.CurrentSalvageState = SalvageState.Idle;
                    return;
                }

                if (Logging.DebugCleanup) Logging.Log("Cleanup", "Processstate: we are in space", Logging.Teal);
                if (DateTime.UtcNow < Time.Instance.LastInStation.AddSeconds(10))
                {
                    if (Logging.DebugCleanup) Logging.Log("Cleanup", "Processstate: last in station time is [" + Time.Instance.LastInStation + " waiting until 10 seconds have passed", Logging.Teal);
                    return;
                }
            }

            if (Cache.Instance.InStation)
            {
                if (Logging.DebugCleanup) Logging.Log("Cleanup", "Processstate: we are in station", Logging.Teal);
                if (DateTime.UtcNow < Time.Instance.LastInSpace.AddSeconds(10))
                {
                    if (Logging.DebugCleanup) Logging.Log("Cleanup", "Processstate: last in space time is [" + Time.Instance.LastInSpace + " waiting until 10 seconds have passed", Logging.Teal);
                    return;
                }
            }

            switch (_States.CurrentCleanupState)
            {
                case CleanupState.Idle:

                    //Cleanup State should only run every 4 seconds
                    if (DateTime.UtcNow.Subtract(_lastCleanupAction).TotalSeconds < 4)
                        return;
                    _States.CurrentCleanupState = CleanupState.CheckModalWindows;
                    break;

                case CleanupState.CheckModalWindows:

                    //
                    // go through *every* window
                    //
                    if (!Cache.Instance.InSpace && !Cache.Instance.InStation && Settings.Instance.CharacterName != "AtLoginScreenNoCharactersLoggedInYet")
                    {
                        if (Logging.DebugCleanup) Logging.Log("Cleanup", "CheckModalWindows: We are in a session change, waiting 4 seconds", Logging.White);
                        _lastCleanupAction = DateTime.UtcNow;
                        _States.CurrentCleanupState = CleanupState.Idle;
                        return;
                    }

                    if (Settings.Instance.CharacterName == "AtLoginScreenNoCharactersLoggedInYet" && Time.Instance.LastInStation.AddHours(1) > DateTime.UtcNow)
                    {
                        Cleanup.ReasonToStopQuestor = "we are no longer in a valid session (not logged in) and we had been logged in. restarting";
                        Logging.Log("Cleanup", Cleanup.ReasonToStopQuestor, Logging.White);
                        Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment = true;
                        Cleanup.CloseQuestor(Cleanup.ReasonToStopQuestor);
                        return;
                    }

                    if (Cache.Instance.Windows == null || !Cache.Instance.Windows.Any())
                    {
                        if (Logging.DebugCleanup) Logging.Log("Cleanup", "CheckModalWindows: Cache.Instance.Windows returned null or empty", Logging.White);
                        _lastCleanupAction = DateTime.UtcNow;
                        _States.CurrentCleanupState = CleanupState.Idle;
                        return;
                    }
                    if (Logging.DebugCleanup) Logging.Log("Cleanup", "Checking Each window in Cache.Instance.Windows", Logging.Teal);

                    foreach (DirectWindow window in Cache.Instance.Windows)
                    {
                        // Telecom messages are generally mission info messages: close them
                        if (window.Name == "telecom")
                        {
                            Logging.Log("Cleanup", "Closing telecom message...", Logging.White);
                            Logging.Log("Cleanup", "Content of telecom window (HTML): [" + (window.Html ?? string.Empty).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                            window.Close();
                        }

                        // Modal windows must be closed
                        // But lets only close known modal windows
                        if (window.IsModal)
                        {
                            bool close = false;
                            bool restart = false;
                            bool restartHarsh = false;
                            bool gotoBaseNow = false;
                            bool sayYes = false;
                            bool sayOk = false;
                            bool pause = false;
                            bool stackHangars = false;
                            bool clearPocket = false;
                            //bool sayno = false;
                            if (!string.IsNullOrEmpty(window.Html))
                            {
                                // Server going down /unscheduled/ potentially very soon!
                                // CCP does not reboot in the middle of the day because the server is behaving
                                // dock now to avoid problems
                                gotoBaseNow |= window.Html.Contains("for a short unscheduled reboot");

                                //fitting window errors - DO NOT undock if this happens! people should fix the fits they load to not move more modules than necessary as that causes problems and requires extra modules

                                //if (_States.CurrentQuestorState == QuestorState.BackgroundBehavior)
                                //{
                                    //
                                    // we do not care about fitting errors when using the BackgroundBehavior
                                    //
                                //    sayOk |= window.Html.Contains("Not all the items could be fitted");
                                //}
                                //else
                                //{
                                    pause |= window.Html.Contains("Not all the items could be fitted");   
                                //}

                                pause |= window.Html.Contains("Cannot move");

                                if (window.Type == "form.MessageBox" && window.IsDialog && window.IsModal && window.IsKillable)
                                {
                                    sayOk |= window.Html.Contains("If you decline of fail a mission from an agent he/she might become displeased and lower your standing towards him/her. You can decline a mission every four hours without penalty"); //4 hours without penalty
                                }

                                // quitting eve?
                                close |= window.Html.Contains("Do you really want to quit now?");

                                // Server going down
                                close |= window.Html.Contains("Please make sure your characters are out of harm");
                                close |= window.Html.Contains("the servers are down for 30 minutes each day for maintenance and updates");

                                // In space "shit"
                                close |= window.Html.Contains("Item cannot be moved back to a loot container.");
                                close |= window.Html.Contains("you do not have the cargo space");
                                close |= window.Html.Contains("cargo units would be required to complete this operation.");
                                close |= window.Html.Contains("You are too far away from the acceleration gate to activate it!");
                                close |= window.Html.Contains("maximum distance is 2500 meters");
                                // agent mission decline warning (ok button)
                                close |= window.Html.Contains("If you decline of fail a mission from an agent he/she might become displeased and lower your standing towards him/her. You can decline a mission every four hours without penalty"); //4 hours without penalty
                                // Stupid warning, lets see if we can find it
                                close |= window.Html.Contains("Do you wish to proceed with this dangerous action?");
                                // Yes we know the mission is not complete, Questor will just redo the mission
                                close |= window.Html.Contains("weapons in that group are already full");
                                //close |= window.Html.Contains("You have to be at the drop off location to deliver the items in person");

                                //fitting window message(s)
                                close |= window.Html.Contains("No rigs were added to or removed from the ship");
                                //In station - Flying Between Hangars
                                close |= window.Html.Contains("You can't fly your active ship into someone else's hangar");
                                // Lag :/
                                
                                clearPocket |= window.Html.Contains("This gate is locked!");


                                close |= window.Html.Contains("The Zbikoki's Hacker Card");
                                close |= window.Html.Contains(" units free.");
                                close |= window.Html.Contains("already full");
                                //windows that can be disabled, but may not yet be disabled
                                //why are we reloading an already full weapon?
                                close |= window.Html.Contains("All the weapons in this group are already full");
                                //trial account
                                close |= window.Html.Contains("At any time you can log in to the account management page and change your trial account to a paying account");

                                restartHarsh |= window.Html.Contains("The user's connection has been usurped on the proxy");
                                restartHarsh |= window.Html.Contains("The connection to the server was closed"); 										//CONNECTION LOST
                                restartHarsh |= window.Html.Contains("server was closed");  															//CONNECTION LOST
                                restartHarsh |= window.Html.Contains("The socket was closed"); 															//CONNECTION LOST
                                restartHarsh |= window.Html.Contains("The connection was closed"); 														//CONNECTION LOST
                                restartHarsh |= window.Html.Contains("Connection to server lost"); 														//CONNECTION LOST
                                restartHarsh |= window.Html.Contains("The user connection has been usurped on the proxy"); 								//CONNECTION LOST
                                restartHarsh |= window.Html.Contains("The transport has not yet been connected, or authentication was not successful");	//CONNECTION LOST
                                restartHarsh |= window.Html.Contains("Your client has waited"); //SOUL-CRUSHING LAG - Your client has waited x minutes for a remote call to complete.
                                restartHarsh |= window.Html.Contains("This could mean the server is very loaded"); //SOUL-CRUSHING LAG - Your client has waited x minutes for a remote call to complete.

                                //
                                // restart the client if these are encountered
                                //
                                restart |= window.Html.Contains("Local cache is corrupt");
                                restart |= window.Html.Contains("Local session information is corrupt");
                                //
                                // Modal Dialogs the need "yes" pressed
                                //
                                sayYes |= window.Html.Contains("objectives requiring a total capacity");
                                sayYes |= window.Html.Contains("your ship only has space for");
                                sayYes |= window.Html.Contains("Are you sure you want to remove location");

                                //
                                // Accept fleet invites from this specific character
                                //
                                sayYes |= window.Html.Contains(Settings.Instance.CharacterToAcceptInvitesFrom + " wants you to join their fleet");

                                //sayyes |= window.Html.Contains("Repairing these items will cost");
                                sayYes |= window.Html.Contains("Are you sure you would like to decline this mission");
                                //sayyes |= window.Html.Contains("You can decline a mission every four hours without penalty");
                                sayYes |= window.Html.Contains("has no other missions to offer right now. Are you sure you want to decline");

                                //
                                // LP Store "Accept offer" dialog
                                //
                                sayOk |= window.Html.Contains("Are you sure you want to accept this offer?");
                                
                                sayOk |= window.Html.Contains("You do not have an outstanding invitation to this fleet.");
                                sayOk |= window.Html.Contains("You have already selected a character for this session.");
                                sayOk |= window.Html.Contains("If you decline or fail a mission from an agent");

                                //
                                // Not Enough Shelf Space
                                // "You can't add the Militants as there are simply too many items here already."
                                //
                                stackHangars |= window.Html.Contains("as there are simply too many items here already");

                                //
                                // Modal Dialogs the need "no" pressed
                                //
                                //sayno |= window.Html.Contains("Do you wish to proceed with this dangerous action
                            }

                            // Unfortunately, it now seems that the html content of payment confirmation windows during drone repair is empty, 
                            // therefore the following check is necessary to press Ok in that window
                            if (_States.CurrentArmState == ArmState.RepairShop || _States.CurrentPanicState == PanicState.Panic)
                                sayOk |= window.Type.Contains("form.HybridWindow") && window.Caption.Contains("Set Quantity");


                            if (restartHarsh)
                            {
                                Logging.Log("Cleanup: RestartWindow", "Restarting eve...", Logging.White);
                                Logging.Log("Cleanup: RestartWindow", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                                Cache.Instance.CloseQuestorCMDLogoff = false;
                                Cache.Instance.CloseQuestorCMDExitGame = true;
                                Cache.Instance.CloseQuestorEndProcess = true;
                                Cleanup.ReasonToStopQuestor = "A message from ccp indicated we were disconnected";
                                Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment = true;
                                Cleanup.CloseQuestor(Cleanup.ReasonToStopQuestor);
                                return;
                            }

                            if (restart)
                            {
                                Logging.Log("Cleanup", "Restarting eve...", Logging.White);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                                Cache.Instance.CloseQuestorCMDLogoff = false;
                                Cache.Instance.CloseQuestorCMDExitGame = true;
                                Cache.Instance.CloseQuestorEndProcess = false;
                                Cleanup.ReasonToStopQuestor = "A message from ccp indicated we were should restart";
                                Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment = true;
                                window.Close();
                                Cleanup.CloseQuestor(Cleanup.ReasonToStopQuestor);
                                return;
                            }

                            if (sayYes)
                            {
                                Logging.Log("Cleanup", "Found a window that needs 'yes' chosen...", Logging.White);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                                window.AnswerModal("Yes");
                                continue;
                            }

                            if (sayOk)
                            {
                                Logging.Log("Cleanup", "Found a window that needs 'ok' chosen...", Logging.White);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);

                                if (window.Html.Contains("Repairing these items will cost"))
                                {
                                    Cache.Instance.doneUsingRepairWindow = true;
                                }
                                window.AnswerModal("OK");
                                continue;
                            }

                            if (stackHangars)
                            {
                                if (!Cache.Instance.StackAmmoHangar("Cleanup")) return;
                                if (!Cache.Instance.StackLootHangar("Cleanup")) return;
                                //if (!Cache.Instance.StackItemhangar("Cleanup")) return;
                                continue;
                            }

                            if (gotoBaseNow)
                            {
                                Logging.Log("Cleanup", "Evidently the cluster is dieing... and CCP is restarting the server", Logging.White);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                                Cache.Instance.GotoBaseNow = true;
                                Settings.Instance.AutoStart = false;

                                //
                                // do not close eve, let the shutdown of the server do that
                                //
                                //Cache.Instance.CloseQuestorCMDLogoff = false;
                                //Cache.Instance.CloseQuestorCMDExitGame = true;
                                //Cleanup.ReasonToStopQuestor = "A message from ccp indicated we were disconnected";
                                //Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment = true;
                                window.Close();
                                continue;
                            }

                            if (pause)
                            {
                                Logging.Log("Cleanup", "This window indicates an error fitting the ship. pausing", Logging.White);
                                Cache.Instance.Paused = true;
                            }

                            if (close)
                            {
                                Logging.Log("Cleanup", "Closing modal window...", Logging.White);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                                window.Close();
                                continue;
                            }

                            if (clearPocket)
                            {
                                Logging.Log("Cleanup", "Closing modal window...", Logging.White);
                                Logging.Log("Cleanup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                                window.Close();
                                //
                                //  queue up a clearpocket action;
                                //
                                CombatMissionCtrl.ReplaceMissionsActions();
                                continue;
                            }
                        }

                        if (Cache.Instance.InSpace)
                        {
                            if (FoundDuelInvitation && window.IsDialog && window.IsModal && window.Caption == "Duel Invitation")
                            {
                                if (DateTime.UtcNow > FoundDuelInvitationTime.AddSeconds(Cache.Instance.RandomNumber(4, 25)))
                                {
                                    //window.AnswerModal("yes");
                                    //window.Close();
                                    FoundDuelInvitation = true;
                                }
                            }
                            
                            if (window.IsDialog && window.IsModal && window.Caption == "Duel Invitation")
                            {
                                FoundDuelInvitation = true;
                                FoundDuelInvitationTime = DateTime.UtcNow;
                            }

                            if (window.Name.Contains("_ShipDroneBay_") && window.Caption == "Drone Bay")
                            {
                                if (Drones.UseDrones &&
                                   (Cache.Instance.ActiveShip.GroupId != (int)Group.Shuttle &&
                                    Cache.Instance.ActiveShip.GroupId != (int)Group.Industrial &&
                                    Cache.Instance.ActiveShip.GroupId != (int)Group.TransportShip &&
                                    _droneBayClosingAttempts <= 1))
                                {
                                    _lastCleanupAction = DateTime.UtcNow;
                                    _droneBayClosingAttempts++;

                                    // Close the drone bay, its not required in space.
                                    window.Close();
                                }
                            }
                            else
                            {
                                _droneBayClosingAttempts = 0;
                            }
                        }
                    }

                    _States.CurrentCleanupState = CleanupState.CleanupTasks;
                    break;

                case CleanupState.CleanupTasks:
                    if (Settings.Instance.EVEMemoryManager) //https://github.com/VendanAndrews/EveMemManager
                    {
                        if (!MemoryManagerHasBeenRunThisIteration && Cache.Instance.InStation && DateTime.UtcNow > Time.Instance.LastInSpace.AddSeconds(20))
                        {
                            // get the current process
                            Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();

                            // get the physical mem usage (this only runs between missions)
                            if (currentProcess.WorkingSet64 != 0)
                            {
                                Cache.Instance.TotalMegaBytesOfMemoryUsed = ((currentProcess.WorkingSet64 / 1024) / 1024 + 1);    
                            }
                            Logging.Log("Questor", "EVE instance: totalMegaBytesOfMemoryUsed - " + Cache.Instance.TotalMegaBytesOfMemoryUsed + " MB", Logging.White);
                            string MemoryManagerCommandToRun = "dotnet m1 memmanager.exe " + Settings.Instance.MemoryManagerTrimThreshold;
                            Logging.Log("Cleanup.CleanupTasks", "EVEMemoryManager: running [ " + MemoryManagerCommandToRun + " ]", Logging.White);
                            if (Logging.UseInnerspace) LavishScript.ExecuteCommand(MemoryManagerCommandToRun);
                            MemoryManagerHasBeenRunThisIteration = true;
                        }

                        if (MemoryManagerHasBeenRunThisIteration && Cache.Instance.InSpace && DateTime.UtcNow > Time.Instance.LastInStation.AddSeconds(300))
                        {
                            //
                            // reset the flag so that MemManager.exe will run again when we are next in station.
                            //
                            MemoryManagerHasBeenRunThisIteration = false;
                        }
                    }

                    if (DateTime.UtcNow > Time.Instance.LastSessionChange.AddSeconds(30) && (
                        _States.CurrentQuestorState == QuestorState.CombatMissionsBehavior ||
                        _States.CurrentQuestorState == QuestorState.CombatHelperBehavior ||
                        _States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior ||
                        _States.CurrentQuestorState == QuestorState.Idle ||
                        _States.CurrentQuestorState == QuestorState.Cleanup) &&
                        string.Compare(Logging.FilterPath(Settings.Instance.CharacterName).ToUpperInvariant(), Logging.FilterPath(Cache.Instance.DirectEve.Me.Name).ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) == 1
                       )
                    {
                        Logging.Log("Cleanup", "DebugInfo:  Settings.Instance.CharacterName [" + Settings.Instance.CharacterName + "]", Logging.White);
                        Logging.Log("Cleanup", "DebugInfo: Cache.Instance.DirectEve.Me.Name [" + Cache.Instance.DirectEve.Me.Name + "]", Logging.White);
                        Cleanup.ReasonToStopQuestor = "CharacterName not defined! - Are we still logged in? Did we lose connection to eve? Questor should be restarting here.";
                        Logging.Log("Cleanup", "CharacterName not defined! - Are we still logged in? Did we lose connection to eve? Questor should be restarting here.", Logging.White);
                        Settings.Instance.CharacterName = "NoCharactersLoggedInAnymore";
                        Time.EnteredCloseQuestor_DateTime = DateTime.UtcNow;
                        Cleanup.SignalToQuitQuestorAndEVEAndRestartInAMoment = true;
                        _States.CurrentQuestorState = QuestorState.CloseQuestor;
                        Cleanup.CloseQuestor(Cleanup.ReasonToStopQuestor);
                        return;
                    }

                    _lastCleanupAction = DateTime.UtcNow;
                    _States.CurrentCleanupState = CleanupState.Idle;
                    break;

                default:

                    // Next state
                    _States.CurrentCleanupState = CleanupState.Idle;
                    break;
            }
        }
    }
}