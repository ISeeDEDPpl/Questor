//------------------------------------------------------------------------------
//  <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//    Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//    Please look in the accompanying license.htm file for the license that
//    applies to this source code. (a copy can also be found at:
//    http://www.thehackerwithin.com/license.htm)
//  </copyright>
//-------------------------------------------------------------------------------

namespace Questor
{
    using System;
    using System.Windows.Forms;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using System.IO;
    using System.Timers;
    using Mono.Options;
    using System.Globalization;
    using LavishScriptAPI;
    using global::Questor.Modules.BackgroundTasks;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using DirectEve;

    internal static class Program
    {
        private static bool _done;
        private static DirectEve _directEve;

        public static List<CharSchedule> CharSchedules { get; private set; }

        private static int _pulsedelay = Time.Instance.QuestorBeforeLoginPulseDelay_seconds;

        public static DateTime AppStarted = DateTime.UtcNow;
        private static string _username;
        private static string _password;
        public static string _character;
        private static string _scriptFile;
        private static string _scriptAfterLoginFile;
        private static bool _loginOnly;
        private static bool _showHelp;
        private static int _maxRuntime;
        private static bool _chantlingScheduler;
        private static bool _loginNowIgnoreScheduler;

        private static double _minutesToStart;
        private static bool _readyToStarta;
        private static bool _readyToStart;
        private static bool _humanInterventionRequired;

        static readonly System.Timers.Timer Timer = new System.Timers.Timer();
        private const int RandStartDelay = 30; //Random startup delay in minutes
        private static readonly Random R = new Random();
        public static bool StopTimeSpecified; //false;

        private static DateTime _nextPulse;
        public static DateTime StartTime = DateTime.MaxValue;
        public static DateTime StopTime = DateTime.MinValue;

        public static int MaxRuntime
        {
            get
            {
                return _maxRuntime;
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            _maxRuntime = Int32.MaxValue;
            var p = new OptionSet {
                "Usage: questor [OPTIONS]",
                "Run missions and make uber ISK.",
                "",
                "Options:",
                {"u|user=", "the {USER} we are logging in as.", v => _username = v},
                {"p|password=", "the user's {PASSWORD}.", v => _password = v},
                {"c|character=", "the {CHARACTER} to use.", v => _character = v},
                {"s|script=", "a {SCRIPT} file to execute before login.", v => _scriptFile = v},
                {"t|scriptAfterLogin=", "a {SCRIPT} file to execute after login.", v => _scriptAfterLoginFile = v},
                {"l|loginOnly", "login only and exit.", v => _loginOnly = v != null},
                {"x|chantling", "use chantling's scheduler", v => _chantlingScheduler = v != null},
                {"n|loginNow", "Login using info in scheduler", v => _loginNowIgnoreScheduler = v != null},
                {"h|help", "show this message and exit", v => _showHelp = v != null}
                };

            List<string> extra;
            try
            {
                extra = p.Parse(args);

                //Logging.Log(string.Format("questor: extra = {0}", string.Join(" ", extra.ToArray())));
            }
            catch (OptionException ex)
            {
                Logging.Log("Startup", "questor: ", Logging.White);
                Logging.Log("Startup", ex.Message, Logging.White);
                Logging.Log("Startup", "Try `questor --help' for more information.", Logging.White);
                return;
            }
            _readyToStart = true;

            if (_showHelp)
            {
                System.IO.StringWriter sw = new System.IO.StringWriter();
                p.WriteOptionDescriptions(sw);
                Logging.Log("Startup", sw.ToString(), Logging.White);
                return;
            }

            if (_loginNowIgnoreScheduler && !_chantlingScheduler)
            {
                _chantlingScheduler = true;
            }

            if (_chantlingScheduler && string.IsNullOrEmpty(_character))
            {
                Logging.Log("Startup", "Error: to use chantling's scheduler, you also need to provide a character name!", Logging.Red);
                return;
            }

            //
            // login using info from schedules.xml
            //
            if (_chantlingScheduler && !string.IsNullOrEmpty(_character))
            {
                LoginUsingScheduler();
            }

            //
            // direct login, no schedules.xml
            //
            if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password) && !string.IsNullOrEmpty(_character))
            {
                _readyToStart = true;
            }

            try
            {
                _directEve = new DirectEve();
            }
            catch (Exception ex)
            {
                Logging.Log("Startup", "Error on Loading DirectEve, maybe server is down", Logging.Orange);
                Logging.Log("Startup", string.Format("DirectEVE: Exception {0}...", ex), Logging.White);
                Cache.Instance.CloseQuestorCMDLogoff = false;
                Cache.Instance.CloseQuestorCMDExitGame = true;
                Cache.Instance.CloseQuestorEndProcess = true;
                Settings.Instance.AutoStart = true;
                Cache.Instance.ReasonToStopQuestor = "Error on Loading DirectEve, maybe server is down";
                Cache.Instance.SessionState = "Quitting";
                Cleanup.CloseQuestor();
            }

            try
            {
                if (_directEve.HasSupportInstances())
                {
                    Logging.Log("Startup", "You have a valid directeve.lic file and have instances available", Logging.Orange);
                }
                else
                {
                    Logging.Log("Startup", "You have 0 Support Instances available [ _directEve.HasSupportInstances() is false ]", Logging.Orange);
                }

            }
            catch (Exception exception)
            {
                Logging.Log("Questor", "Exception while checking: _directEve.HasSupportInstances() - exception was: [" + exception + "]", Logging.Orange);
            }

            try
            {
                _directEve.OnFrame += OnFrame;
            }
            catch (Exception ex)
            {
                Logging.Log("Startup", string.Format("DirectEVE.OnFrame: Exception {0}...", ex), Logging.White);
            }

            // Sleep until we're done
            while (!_done)
            {
                System.Threading.Thread.Sleep(50); //this runs while we wait to login
            }

            try
            {
                _directEve.Dispose();
            }
            catch (Exception ex)
            {
                Logging.Log("Startup", string.Format("DirectEVE.Dispose: Exception {0}...", ex), Logging.White);
            }

            if (_done) //this is just here for clarity, we are really held up in LoginUsingScheduler() or LoginUsingUserNamePassword(); until done == true
            {
                if (!string.IsNullOrEmpty(_scriptAfterLoginFile))
                {
                    Logging.Log("Startup", "Running Script After Login: [ timedcommand 150 runscript " + _scriptAfterLoginFile + "]", Logging.Teal);
                    LavishScript.ExecuteCommand("timedcommand 150 runscript " + _scriptAfterLoginFile);
                }

                // If the last parameter is false, then we only auto-login
                if (_loginOnly)
                {
                    Logging.Log("Startup", "_loginOnly: done and exiting", Logging.Teal);
                    return;
                }
            }

            StartTime = DateTime.Now;

            //
            // We should only get this far if run if we are already logged in...
            // launch questor
            //
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Logging.Log("Startup", "We are logged in: Launching Questor", Logging.Teal);
            Application.Run(new QuestorfrmMain());
        }

        private static void LoginUsingScheduler()
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _character = _character.Replace("\"", "");  // strip quotation marks if any are present

            CharSchedules = new List<CharSchedule>();
            if (path != null)
            {
                XDocument values = XDocument.Load(Path.Combine(path, "Schedules.xml"));
                if (values.Root != null)
                {
                    foreach (XElement value in values.Root.Elements("char"))
                    {
                        CharSchedules.Add(new CharSchedule(value));
                    }
                }
            }

            //
            // chantling scheduler
            //
            CharSchedule schedule = CharSchedules.FirstOrDefault(v => v.ScheduleCharacterName == _character);
            if (schedule == null)
            {
                Logging.Log("Startup", "Error - character not found!", Logging.Red);
                return;
            }

            if (schedule.LoginUserName == null || schedule.LoginPassWord == null)
            {
                Logging.Log("Startup", "Error - Login details not specified in Schedules.xml!", Logging.Red);
                return;
            }

            _username = schedule.LoginUserName;
            _password = schedule.LoginPassWord;
            Logging.Log("Startup", "User: " + schedule.LoginUserName + " Name: " + schedule.ScheduleCharacterName, Logging.White);

            if (schedule.StartTimeSpecified)
            {
                if (schedule.Start1 > schedule.Stop1) schedule.Stop1 = schedule.Stop1.AddDays(1);
                if (DateTime.Now.AddHours(2) > schedule.Start1 && DateTime.Now < schedule.Stop1)
                {
                    StartTime = schedule.Start1;
                    StopTime = schedule.Stop1;
                    StopTimeSpecified = true;
                    Logging.Log("Startup", "Schedule1: Start1: " + schedule.Start1 + " Stop1: " + schedule.Stop1, Logging.White);
                }
            }

            if (schedule.StartTime2Specified)
            {
                if (DateTime.Now > schedule.Stop1 || DateTime.Now.DayOfYear > schedule.Stop1.DayOfYear) //if after schedule1 stoptime or the next day
                {
                    if (schedule.Start2 > schedule.Stop2) schedule.Stop2 = schedule.Stop2.AddDays(1);
                    if (DateTime.Now.AddHours(2) > schedule.Start2 && DateTime.Now < schedule.Stop2)
                    {
                        StartTime = schedule.Start2;
                        StopTime = schedule.Stop2;
                        StopTimeSpecified = true;
                        Logging.Log("Startup", "Schedule2: Start2: " + schedule.Start2 + " Stop2: " + schedule.Stop2, Logging.White);
                    }
                }
            }

            if (schedule.StartTime3Specified)
            {
                if (DateTime.Now > schedule.Stop2 || DateTime.Now.DayOfYear > schedule.Stop2.DayOfYear) //if after schedule2 stoptime or the next day
                {
                    if (schedule.Start3 > schedule.Stop3) schedule.Stop3 = schedule.Stop3.AddDays(1);
                    if (DateTime.Now.AddHours(2) > schedule.Start3 && DateTime.Now < schedule.Stop3)
                    {
                        StartTime = schedule.Start3;
                        StopTime = schedule.Stop3;
                        StopTimeSpecified = true;
                        Logging.Log("Startup", "Schedule3: Start3: " + schedule.Start3 + " Stop3: " + schedule.Stop3, Logging.White);
                    }
                }
            }

            //
            // if we havent found a worksable schedule yet assume schedule 1 is correct. what we want.
            //
            if (schedule.StartTimeSpecified && StartTime == DateTime.MaxValue)
            {
                StartTime = schedule.Start1;
                StopTime = schedule.Stop1;
                Logging.Log("Startup", "Forcing Schedule 1 because none of the schedules started within 2 hours", Logging.White);
                Logging.Log("Startup", "Schedule 1: Start1: " + schedule.Start1 + " Stop1: " + schedule.Stop1, Logging.White);
            }

            if (schedule.StartTimeSpecified || schedule.StartTime2Specified || schedule.StartTime3Specified)
                StartTime = StartTime.AddSeconds(R.Next(0, (RandStartDelay * 60)));

            if ((DateTime.Now > StartTime))
            {
                if ((DateTime.Now.Subtract(StartTime).TotalMinutes < 1200)) //if we're less than x hours past start time, start now
                {
                    StartTime = DateTime.Now;
                    _readyToStarta = true;
                }
                else
                    StartTime = StartTime.AddDays(1); //otherwise, start tomorrow at start time
            }
            else if ((StartTime.Subtract(DateTime.Now).TotalMinutes > 1200)) //if we're more than x hours shy of start time, start now
            {
                StartTime = DateTime.Now;
                _readyToStarta = true;
            }

            if (StopTime < StartTime)
                StopTime = StopTime.AddDays(1);

            //if (schedule.RunTime > 0) //if runtime is specified, overrides stop time
            //    StopTime = StartTime.AddMinutes(schedule.RunTime); //minutes of runtime

            //if (schedule.RunTime < 18 && schedule.RunTime > 0)     //if runtime is 10 or less, assume they meant hours
            //    StopTime = StartTime.AddHours(schedule.RunTime);   //hours of runtime

            if (_loginNowIgnoreScheduler)
            {
                _readyToStarta = true;
            }
            else Logging.Log("Startup", " Start Time: " + StartTime + " - Stop Time: " + StopTime, Logging.White);

            if (!_readyToStarta)
            {
                _minutesToStart = StartTime.Subtract(DateTime.Now).TotalMinutes;
                Logging.Log("Startup", "Starting at " + StartTime + ". " + String.Format("{0:0.##}", _minutesToStart) + " minutes to go.", Logging.Yellow);
                Timer.Elapsed += new ElapsedEventHandler(TimerEventProcessor);
                if (_minutesToStart > 0)
                {
                    Timer.Interval = (int)(_minutesToStart * 60000);
                }
                else
                {
                    Timer.Interval = 1000;
                }

                Timer.Enabled = true;
                Timer.Start();
            }
            else
            {
                _readyToStart = true;
                Logging.Log("Startup", "Already passed start time.  Starting in 15 seconds.", Logging.White);
                System.Threading.Thread.Sleep(15000);
            }

            //
            // chantling scheduler (above)
            //
        }

        private static void OnFrame(object sender, EventArgs e)
        {
            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            Cache.Instance.LastFrame = DateTime.UtcNow;
            Cache.Instance.LastSessionIsReady = DateTime.UtcNow; //update this regardless before we login there is no session

            if (DateTime.UtcNow < _nextPulse)
            {
                //Logging.Log("if (DateTime.UtcNow.Subtract(_lastPulse).TotalSeconds < _pulsedelay) then return");
                return;
            }
            _nextPulse = DateTime.UtcNow.AddSeconds(_pulsedelay);

            if (!_readyToStart)
            {
                //Logging.Log("if (!_readyToStart) then return");
                return;
            }

            if (_chantlingScheduler && !string.IsNullOrEmpty(_character) && !_readyToStarta)
            {
                //Logging.Log("if (_chantlingScheduler && !string.IsNullOrEmpty(_character) && !_readyToStarta) then return");
                return;
            }

            if (_humanInterventionRequired)
            {
                //Logging.Log("Startup", "Onframe: _humanInterventionRequired is true (this will spam every second or so)", Logging.Orange);
                return;
            }

            // If the session is ready, then we are done :)
            if (_directEve.Session.IsReady)
            {
                Logging.Log("Startup", "We have successfully logged in", Logging.White);
                Cache.Instance.LastSessionIsReady = DateTime.UtcNow;
                _done = true;
                return;
            }

            // We should not get any windows
            if (_directEve.Windows.Count != 0)
            {
                foreach (var window in _directEve.Windows)
                {
                    if (string.IsNullOrEmpty(window.Html))
                        continue;
                    Logging.Log("Startup", "windowtitles:" + window.Name + "::" + window.Html, Logging.White);

                    //
                    // Close these windows and continue
                    //
                    if (window.Name == "telecom")
                    {
                        Logging.Log("Startup", "Closing telecom message...", Logging.Yellow);
                        Logging.Log("Startup", "Content of telecom window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Yellow);
                        window.Close();
                        continue;
                    }

                    // Modal windows must be closed
                    // But lets only close known modal windows
                    if (window.Name == "modal")
                    {
                        bool close = false;
                        bool restart = false;
                        bool needHumanIntervention = false;
                        bool sayYes = false;
                        bool sayOk = false;
                        bool quit = false;

                        //bool update = false;

                        if (!string.IsNullOrEmpty(window.Html))
                        {
                            //errors that are repeatable and unavoidable even after a restart of eve/questor
                            needHumanIntervention = window.Html.Contains("reason: Account subscription expired");

                            //update |= window.Html.Contains("The update has been downloaded");

                            // Server going down
                            //Logging.Log("[Startup] (1) close is: " + close);
                            close |= window.Html.ToLower().Contains("please make sure your characters are out of harms way");
                            close |= window.Html.ToLower().Contains("accepting connections");
                            close |= window.Html.ToLower().Contains("could not connect");
                            close |= window.Html.ToLower().Contains("the connection to the server was closed");
                            close |= window.Html.ToLower().Contains("server was closed");
                            close |= window.Html.ToLower().Contains("make sure your characters are out of harm");
                            close |= window.Html.ToLower().Contains("connection to server lost");
                            close |= window.Html.ToLower().Contains("the socket was closed");
                            close |= window.Html.ToLower().Contains("the specified proxy or server node");
                            close |= window.Html.ToLower().Contains("starting up");
                            close |= window.Html.ToLower().Contains("unable to connect to the selected server");
                            close |= window.Html.ToLower().Contains("could not connect to the specified address");
                            close |= window.Html.ToLower().Contains("connection timeout");
                            close |= window.Html.ToLower().Contains("the cluster is not currently accepting connections");
                            close |= window.Html.ToLower().Contains("your character is located within");
                            close |= window.Html.ToLower().Contains("the transport has not yet been connected");
                            close |= window.Html.ToLower().Contains("the user's connection has been usurped");
                            close |= window.Html.ToLower().Contains("the EVE cluster has reached its maximum user limit");
                            close |= window.Html.ToLower().Contains("the connection to the server was closed");
                            close |= window.Html.ToLower().Contains("client is already connecting to the server");

                            //close |= window.Html.Contains("A client update is available and will now be installed");
                            //
                            // eventually it would be nice to hit ok on this one and let it update
                            //
                            close |= window.Html.ToLower().Contains("client update is available and will now be installed");
                            close |= window.Html.ToLower().Contains("change your trial account to a paying account");

                            //
                            // these windows require a restart of eve all together
                            //
                            restart |= window.Html.ToLower().Contains("the connection was closed");
                            restart |= window.Html.ToLower().Contains("connection to server lost."); //INFORMATION
                            restart |= window.Html.ToLower().Contains("local cache is corrupt");
                            restart |= window.Html.ToLower().Contains("local session information is corrupt");
                            restart |= window.Html.ToLower().Contains("The client's local session"); // information is corrupt");
                            restart |= window.Html.ToLower().Contains("restart the client prior to logging in");

                            //
                            // these windows require a quit of eve all together
                            //
                            quit |= window.Html.ToLower().Contains("the socket was closed");

                            //
                            // Modal Dialogs the need "yes" pressed
                            //
                            sayOk |= window.Html.Contains("The transport has not yet been connected, or authentication was not successful");

                            //Logging.Log("[Startup] (2) close is: " + close);
                            //Logging.Log("[Startup] (1) window.Html is: " + window.Html);
                            _pulsedelay = 60;
                        }

                        //if (update)
                        //{
                        //    int secRestart = (400 * 3) + Cache.Instance.RandomNumber(3, 18) * 100 + Cache.Instance.RandomNumber(1, 9) * 10;
                        //    LavishScript.ExecuteCommand("uplink exec Echo [${Time}] timedcommand " + secRestart + " OSExecute taskkill /IM launcher.exe");
                        //}

                        if (sayYes)
                        {
                            Logging.Log("Startup", "Found a window that needs 'yes' chosen...", Logging.White);
                            Logging.Log("Startup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                            window.AnswerModal("Yes");
                            continue;
                        }

                        if (sayOk)
                        {
                            Logging.Log("Startup", "Found a window that needs 'ok' chosen...", Logging.White);
                            Logging.Log("Startup", "Content of modal window (HTML): [" + (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.White);
                            window.AnswerModal("OK");
                            if (window.Html.Contains("The update has been downloaded. The client will now close and the update process begin"))
                            {
                                //
                                // schedule the closing of launcher.exe via a timedcommand (10 min?) in the uplink...
                                //
                            }
                            continue;
                        }

                        if (quit)
                        {
                            Logging.Log("Startup", "Restarting eve...", Logging.Red);
                            Logging.Log("Startup", "Content of modal window (HTML): [" +
                                        (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Red);
                            window.AnswerModal("quit");

                            //_directEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                        }

                        if (restart)
                        {
                            Logging.Log("Startup", "Restarting eve...", Logging.Red);
                            Logging.Log("Startup", "Content of modal window (HTML): [" +
                                        (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Red);
                            window.AnswerModal("restart");

                            //_directEve.ExecuteCommand(DirectCmd.CmdQuitGame);
                            continue;
                        }

                        if (close)
                        {
                            Logging.Log("Startup", "Closing modal window...", Logging.Yellow);
                            Logging.Log("Startup", "Content of modal window (HTML): [" +
                                        (window.Html).Replace("\n", "").Replace("\r", "") + "]", Logging.Yellow);
                            window.Close();
                            continue;
                        }

                        if (needHumanIntervention)
                        {
                            Logging.Log("Startup", "ERROR! - Human Intervention is required in this case: halting all login attempts - ERROR!", Logging.Red);
                            Logging.Log("Startup", "window.Name is: " + window.Name, Logging.Red);
                            Logging.Log("Startup", "window.Html is: " + window.Html, Logging.Red);
                            Logging.Log("Startup", "window.Caption is: " + window.Caption, Logging.Red);
                            Logging.Log("Startup", "window.Type is: " + window.Type, Logging.Red);
                            Logging.Log("Startup", "window.ID is: " + window.Id, Logging.Red);
                            Logging.Log("Startup", "window.IsDialog is: " + window.IsDialog, Logging.Red);
                            Logging.Log("Startup", "window.IsKillable is: " + window.IsKillable, Logging.Red);
                            Logging.Log("Startup", "window.Viewmode is: " + window.ViewMode, Logging.Red);
                            Logging.Log("Startup", "ERROR! - Human Intervention is required in this case: halting all login attempts - ERROR!", Logging.Red);
                            _humanInterventionRequired = true;
                            return;
                        }
                    }

                    if (string.IsNullOrEmpty(window.Html))
                        continue;

                    if (window.Name == "telecom")
                        continue;
                    Logging.Log("Startup", "We have an unexpected window, auto login halted.", Logging.Red);
                    Logging.Log("Startup", "window.Name is: " + window.Name, Logging.Red);
                    Logging.Log("Startup", "window.Html is: " + window.Html, Logging.Red);
                    Logging.Log("Startup", "window.Caption is: " + window.Caption, Logging.Red);
                    Logging.Log("Startup", "window.Type is: " + window.Type, Logging.Red);
                    Logging.Log("Startup", "window.ID is: " + window.Id, Logging.Red);
                    Logging.Log("Startup", "window.IsDialog is: " + window.IsDialog, Logging.Red);
                    Logging.Log("Startup", "window.IsKillable is: " + window.IsKillable, Logging.Red);
                    Logging.Log("Startup", "window.Viewmode is: " + window.ViewMode, Logging.Red);
                    Logging.Log("Startup", "We have got an unexpected window, auto login halted.", Logging.Red);
                    _done = true;
                    return;
                }
                return;
            }

            if (!string.IsNullOrEmpty(_scriptFile))
            {
                try
                {
                    // Replace this try block with the following once new DirectEve is pushed
                    // _directEve.RunScript(_scriptFile);

                    System.Reflection.MethodInfo info = _directEve.GetType().GetMethod("RunScript");

                    if (info == null)
                    {
                        Logging.Log("Startup", "DirectEve.RunScript() does not exist.  Upgrade DirectEve.dll!", Logging.Red);
                    }
                    else
                    {
                        Logging.Log("Startup", string.Format("Running {0}...", _scriptFile), Logging.White);
                        info.Invoke(_directEve, new Object[] { _scriptFile });
                    }
                }
                catch (System.Exception ex)
                {
                    Logging.Log("Startup", string.Format("Exception {0}...", ex), Logging.White);
                    _done = true;
                }
                finally
                {
                    _scriptFile = null;
                }
                return;
            }

            if (_directEve.Login.AtLogin && !_directEve.Login.IsLoading && !_directEve.Login.IsConnecting)
            {
                if (!_directEve.HasSupportInstances())
                {
                    Logging.Log("Startup", "DirectEVE Requires Active Support Instances to use the convenient like Auto-Login, Market Functions (Valuedump and Market involving storylines) among other features.", Logging.White);
                    Logging.Log("Startup", "Make sure you have support instances and that you have downloaded your directeve.lic file and placed it in the .net programs folder with your directeve.dll", Logging.White);
                    _humanInterventionRequired = true;
                }

                if (DateTime.UtcNow.Subtract(AppStarted).TotalSeconds > 5)
                {
                    Logging.Log("Startup", "Login account [" + _username + "]", Logging.White);
                    _directEve.Login.Login(_username, _password);
                    Logging.Log("Startup", "Waiting for Character Selection Screen", Logging.White);
                    _pulsedelay = Time.Instance.QuestorBeforeLoginPulseDelay_seconds;
                    return;
                }
            }

            if (_directEve.Login.AtCharacterSelection && _directEve.Login.IsCharacterSelectionReady && !_directEve.Login.IsConnecting && !_directEve.Login.IsLoading)
            {
                if (DateTime.UtcNow.Subtract(AppStarted).TotalSeconds > 20)
                {
                    foreach (DirectLoginSlot slot in _directEve.Login.CharacterSlots)
                    {
                        if (slot.CharId.ToString(CultureInfo.InvariantCulture) != _character && System.String.Compare(slot.CharName, _character, System.StringComparison.OrdinalIgnoreCase) != 0)
                        {
                            continue;
                        }

                        Logging.Log("Startup", "Activating character [" + slot.CharName + "]", Logging.White);
                        slot.Activate();
                        return;
                    }
                    Logging.Log("Startup", "Character id/name [" + _character + "] not found, retrying in 10 seconds", Logging.White);
                }
            }
        }

        private static void TimerEventProcessor(Object myObject, EventArgs myEventArgs)
        {
            Timer.Stop();
            Logging.Log("Startup", "Timer elapsed.  Starting now.", Logging.White);
            _readyToStart = true;
            _readyToStarta = true;
        }
    }
}