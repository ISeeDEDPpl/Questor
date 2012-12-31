using Questor.Modules.Alerts;
using Questor.Properties;

namespace Questor
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Reflection;
    using System.Linq;
    using System.Windows.Forms;
    using System.IO;
    using LavishScriptAPI;
    using global::Questor.Modules.Actions;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.BackgroundTasks;

    public partial class QuestorfrmMain : Form
    {
        private readonly Questor _questor;
        private DateTime _nextConsoleLogRefresh = DateTime.MinValue;
        private DateTime _nextUIDataRefresh = DateTime.UtcNow;
        private DateTime _nextScheduleUpdate = DateTime.UtcNow;

        public QuestorfrmMain()
        {
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "QuestorfrmMain", Logging.White);
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "InitializeComponent", Logging.White);
            InitializeComponent();
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "_questor = new Questor(this);", Logging.White);
            _questor = new Questor(this);
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "PopulateStateComboBoxes", Logging.White);
            PopulateStateComboBoxes();
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "PopulateBehaviorStateComboBox", Logging.White);
            PopulateBehaviorStateComboBox();
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "CreateLavishCommands", Logging.White);
            CreateLavishCommands();
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "this.Show();", Logging.White);
            Show();
            if (Settings.Instance.DebugAttachVSDebugger)
            {
                if (!System.Diagnostics.Debugger.IsAttached)
                {
                    Logging.Log("QuestorUI", "VS Debugger is not yet attached: System.Diagnostics.Debugger.Launch()", Logging.Teal);
                    System.Diagnostics.Debugger.Launch();
                }
            }
        }

        private void QuestorfrmMainFormClosed(object sender, FormClosedEventArgs e)
        {
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "QuestorfrmMainFormClosed", Logging.White);

            Cache.Instance.DirectEve.Dispose();
            Cache.Instance.DirectEve = null;
        }

        private void PopulateStateComboBoxes()
        {
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "PopulateStateComboBoxes", Logging.White);
            QuestorStateComboBox.Items.Clear();
            foreach (string text in Enum.GetNames(typeof(QuestorState)))
                QuestorStateComboBox.Items.Add(text);

            if (Settings.Instance.CharacterMode != null)
            {
                //
                // populate combo boxes with the various states that are possible
                //
                // ComboxBoxes on main windows (at top)
                //
                DamageTypeComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(DamageType)))
                {
                    DamageTypeComboBox.Items.Add(text);
                }

                //
                // middle column
                //
                PanicStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(PanicState)))
                {
                    PanicStateComboBox.Items.Add(text);
                }

                CombatStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(CombatState)))
                {
                    CombatStateComboBox.Items.Add(text);
                }

                DronesStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(DroneState)))
                {
                    DronesStateComboBox.Items.Add(text);
                }

                CleanupStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(CleanupState)))
                {
                    CleanupStateComboBox.Items.Add(text);
                }

                LocalWatchStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(LocalWatchState)))
                {
                    LocalWatchStateComboBox.Items.Add(text);
                }

                SalvageStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(SalvageState)))
                {
                    SalvageStateComboBox.Items.Add(text);
                }

                //
                // right column
                //
                CombatMissionCtrlStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(CombatMissionCtrlState)))
                {
                    CombatMissionCtrlStateComboBox.Items.Add(text);
                }

                StorylineStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(StorylineState)))
                {
                    StorylineStateComboBox.Items.Add(text);
                }

                ArmStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(ArmState)))
                {
                    ArmStateComboBox.Items.Add(text);
                }

                UnloadStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(UnloadLootState)))
                {
                    UnloadStateComboBox.Items.Add(text);
                }

                TravelerStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(TravelerState)))
                {
                    TravelerStateComboBox.Items.Add(text);
                }

                AgentInteractionStateComboBox.Items.Clear();
                foreach (string text in Enum.GetNames(typeof(AgentInteractionState)))
                {
                    AgentInteractionStateComboBox.Items.Add(text);
                }
            }
        }

        private void PopulateMissionLists()
        {
            //Logging.Log("QuestorUI","populating MissionBlacklisttextbox",Logging.White);
            BlacklistedMissionstextbox.Text = "";
            foreach (string blacklistedmission in Settings.Instance.MissionBlacklist)
            {
                BlacklistedMissionstextbox.AppendText(blacklistedmission + "\r\n");
            }

            //Logging.Log("QuestorUI", "populating MissionBlacklisttextbox", Logging.White);
            GreyListedMissionsTextBox.Text = "";
            foreach (string GreyListedMission in Settings.Instance.MissionGreylist)
            {
                GreyListedMissionsTextBox.AppendText(GreyListedMission + "\r\n");
            }
        }

        private void RefreshInfoDisplayedInUI()
        {
            if (DateTime.UtcNow > _nextUIDataRefresh && DateTime.UtcNow > Cache.Instance.QuestorStarted_DateTime.AddSeconds(30))
            {
                _nextUIDataRefresh = DateTime.UtcNow.AddMilliseconds(1000);
                try
                {
                    if (Cache.Instance.LastInSpace.AddMilliseconds(1000) > DateTime.UtcNow)
                    {
                        CurrentTimeData1.Text = DateTime.UtcNow.ToLongTimeString();
                        CurrentTimeData2.Text = DateTime.UtcNow.ToLongTimeString();
                        NextOpenContainerInSpaceActionData.Text = Cache.Instance.NextOpenContainerInSpaceAction.ToLongTimeString();
                        NextOpenJournalWindowActionData.Text = Cache.Instance.NextOpenJournalWindowAction.ToLongTimeString();
                        NextOpenLootContainerActionData.Text = Cache.Instance.NextOpenLootContainerAction.ToLongTimeString();
                        NextDroneBayActionData.Text = Cache.Instance.NextDroneBayAction.ToLongTimeString();
                        NextOpenHangarActionData.Text = Cache.Instance.NextOpenHangarAction.ToLongTimeString();
                        NextOpenCargoActionData.Text = Cache.Instance.NextOpenCargoAction.ToLongTimeString();
                        LastActionData.Text = Cache.Instance.LastAction.ToLongTimeString();
                        NextArmActionData.Text = Cache.Instance.NextArmAction.ToLongTimeString();
                        NextSalvageActionData.Text = Cache.Instance.NextSalvageAction.ToLongTimeString();
                        NextLootActionData.Text = Cache.Instance.NextLootAction.ToLongTimeString();
                        LastJettisonData.Text = Cache.Instance.LastJettison.ToLongTimeString();
                        NextDefenceModuleActionData.Text = Cache.Instance.NextDefenseModuleAction.ToLongTimeString();
                        NextAfterburnerActionData.Text = Cache.Instance.NextAfterburnerAction.ToLongTimeString();
                        NextRepModuleActionData.Text = Cache.Instance.NextRepModuleAction.ToLongTimeString();
                        NextActivateSupportModulesData.Text = Cache.Instance.NextActivateSupportModules.ToLongTimeString();
                        NextApproachActionData.Text = Cache.Instance.NextApproachAction.ToLongTimeString();
                        NextOrbitData.Text = Cache.Instance.NextOrbit.ToLongTimeString();
                        NextWarpToData.Text = Cache.Instance.NextWarpTo.ToLongTimeString();
                        NextTravelerActionData.Text = Cache.Instance.NextTravelerAction.ToLongTimeString();
                        NextTargetActionData.Text = Cache.Instance.NextTargetAction.ToLongTimeString();
                        NextReloadData.Text = Cache.Instance.NextReload.ToLongTimeString();
                        NextWeaponActionData.Text = Cache.Instance.NextWeaponAction.ToLongTimeString();
                        NextWebActionData.Text = Cache.Instance.NextWebAction.ToLongTimeString();
                        NextNosActionData.Text = Cache.Instance.NextNosAction.ToLongTimeString();
                        NextPainterActionData.Text = Cache.Instance.NextPainterAction.ToLongTimeString();
                        NextActivateActionData.Text = Cache.Instance.NextActivateAction.ToLongTimeString();
                        NextAlignData.Text = Cache.Instance.NextAlign.ToLongTimeString();
                        NextUndockActionData.Text = Cache.Instance.NextUndockAction.ToLongTimeString();
                        NextDockActionData.Text = Cache.Instance.NextDockAction.ToLongTimeString();
                        NextDroneRecallData.Text = Cache.Instance.NextDroneRecall.ToLongTimeString();
                        NextStartupActionData.Text = Cache.Instance.NextStartupAction.ToLongTimeString();
                        LastSessionChangeData.Text = Cache.Instance.LastSessionChange.ToLongTimeString();
                        AutostartData.Text = Settings.Instance.AutoStart.ToString(CultureInfo.InvariantCulture);

                        DamageTypeData.Text = Cache.Instance.DamageType.ToString();

                        //OrbitDistanceData.Text = Cache.Instance.OrbitDistance.ToString(CultureInfo.InvariantCulture);
                        //AgentStationIDData.Text = Cache.Instance.AgentStationID.ToString(CultureInfo.InvariantCulture);
                        //AgentIdData.Text = Cache.Instance.AgentId.ToString(CultureInfo.InvariantCulture);
                        //AgentData.Text = Cache.Instance.CurrentAgent.ToString(CultureInfo.InvariantCulture);
                        AgentInteractionPurposeData.Text = AgentInteraction.Purpose.ToString();
                        MissionsThisSessionData.Text = Cache.Instance.MissionsThisSession.ToString(CultureInfo.InvariantCulture);

                        //crashes questor when in station?
                        //
                        //if (Cache.Instance.MaxRange > 0)
                        //{
                        //    MaxRangeData.Text = Cache.Instance.MaxRange.ToString(CultureInfo.InvariantCulture);
                             //causes problems / crashes
                        //}
                        //WeaponRangeData.Text = Cache.Instance.WeaponRange.ToString(CultureInfo.InvariantCulture); //causes problems / crashes
                        //ActiveDronesData.Text = Cache.Instance.ActiveDrones.Count().ToString();                   //causes problems / crashes
                        //if (!Cache.Instance.InWarp && DateTime.UtcNow > _nextWreckUpdate)                            //this was causing exceptions we cant check inarp from the UI?
                        //{
                        //    _nextWreckUpdate = DateTime.UtcNow.AddSeconds(10);
                            //WrecksData.Text = Cache.Instance.Wrecks.Count().ToString(CultureInfo.InvariantCulture);
                            //UnlootedContainersData.Text = Cache.Instance.UnlootedContainers.Count().ToString(CultureInfo.InvariantCulture); 
                            //ApproachingData.Text = Cache.Instance.IsApproaching.ToString(CultureInfo.InvariantCulture);
                        //}
                        //DamagedDronesData.Text = Cache.Instance.DamagedDrones.Count().ToString(CultureInfo.InvariantCulture);
                        //PriorityTargetsData.Text = Cache.Instance.PriorityTargets.Count().ToString(CultureInfo.InvariantCulture);
                        //if (Cache.Instance.IsMissionPocketDone) IsMissionPocketDoneData.Text = "true";
                        //else if (!Cache.Instance.IsMissionPocketDone) IsMissionPocketDoneData.Text = "false";
                    }

                    if (Cache.Instance.LastInStation.AddSeconds(2) > DateTime.UtcNow)
                    {
                        MaxRangeData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        ActiveDronesData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        ApproachingData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        DamagedDronesData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        PriorityTargetsData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        WeaponRangeData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        IsMissionPocketDoneData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        WrecksData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;
                        UnlootedContainersData.Text = Resources.QuestorfrmMain_RefreshInfoDisplayedInUI_n_a;

                        DataAmmoHangarID.Text = Cache.Instance.AmmoHangarID.ToString(CultureInfo.InvariantCulture);
                        DataAmmoHangarName.Text = Settings.Instance.AmmoHangar;
                        DataLootHangarID.Text = Cache.Instance.LootHangarID.ToString(CultureInfo.InvariantCulture);
                        DataLootHangarName.Text = Settings.Instance.LootHangar;
                    }
                }
                catch (Exception ex)
                {
                    if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "RefreshInfoDisplayedInUI: unable to update all UI labels: exception was [" + ex.Message + "]", Logging.Teal);
                }
            }
            if (DateTime.UtcNow > _nextScheduleUpdate)
            {
                _nextScheduleUpdate = DateTime.UtcNow.AddSeconds(90);
                if (Cache.Instance.StopTimeSpecified)
                {
                    ScheduledStopTimeData.Text = Cache.Instance.StopTime.ToLongTimeString();
                }

                //
                // if control is enabled (checked) then update ManualStopTime so that on next idle questor will check to see if it needs to stop
                //
                if (dateTimePicker1.Checked)
                {
                    Cache.Instance.ManualStopTime = dateTimePicker1.Value;
                    if (Cache.Instance.ManualStopTime > Cache.Instance.StopTime)
                    {
                        Cache.Instance.StopTimeSpecified = false;
                    }

                    if (Cache.Instance.ManualStopTime < Cache.Instance.StopTime)
                    {
                        Cache.Instance.StopTimeSpecified = true;
                    }
                }
                else
                {
                    dateTimePicker1.Value = DateTime.Now.AddHours(1);
                    if (!dateTimePicker2.Checked)
                    {
                        Cache.Instance.StopTimeSpecified = true;
                    }
                }
                //
                // if control is enabled (checked) then update ManualRestartTime so that on next idle questor will check to see if it needs to stop/restart
                //
                if (dateTimePicker2.Checked)
                {
                    Cache.Instance.ManualRestartTime = dateTimePicker2.Value;
                    if (Cache.Instance.ManualRestartTime > Cache.Instance.StopTime)
                    {
                        Cache.Instance.StopTimeSpecified = false;
                    }

                    if (Cache.Instance.ManualRestartTime < Cache.Instance.StopTime)
                    {
                        Cache.Instance.StopTimeSpecified = true;
                    }
                }
                else
                {
                    dateTimePicker1.Value = DateTime.Now.AddHours(1);
                    if (!dateTimePicker1.Checked)
                    {
                        Cache.Instance.StopTimeSpecified = true;
                    }
                }
            }
        }

        private void PopulateBehaviorStateComboBox()
        {
            if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "PopulateBehaviorStateComboBox", Logging.White);
            if (Settings.Instance.CharacterMode != null)
            {
                //
                // populate combo boxes with the various states that are possible
                //
                // left column
                //
                if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior)
                {
                    BehaviorComboBox.Items.Clear();
                    foreach (string text in Enum.GetNames(typeof(CombatMissionsBehaviorState)))
                    {
                        BehaviorComboBox.Items.Add(text);
                    }
                }
                if (_States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior)
                {
                    BehaviorComboBox.Items.Clear();
                    foreach (string text in Enum.GetNames(typeof(DedicatedBookmarkSalvagerBehaviorState)))
                    {
                        BehaviorComboBox.Items.Add(text);
                    }
                }
                if (_States.CurrentQuestorState == QuestorState.CombatHelperBehavior)
                {
                    BehaviorComboBox.Items.Clear();
                    foreach (string text in Enum.GetNames(typeof(CombatHelperBehaviorState)))
                    {
                        BehaviorComboBox.Items.Add(text);
                    }
                }
                if (_States.CurrentQuestorState == QuestorState.Mining)
                {
                    BehaviorComboBox.Items.Clear();
                    foreach (string text in Enum.GetNames(typeof(MiningState)))
                    {
                        BehaviorComboBox.Items.Add(text);
                    }
                }
                if (_States.CurrentQuestorState == QuestorState.DirectionalScannerBehavior)
                {
                    BehaviorComboBox.Items.Clear();
                    foreach (string text in Enum.GetNames(typeof(DirectionalScannerBehaviorState)))
                    {
                        BehaviorComboBox.Items.Add(text);
                    }
                }
                if (_States.CurrentQuestorState == QuestorState.DebugHangarsBehavior)
                {
                    BehaviorComboBox.Items.Clear();
                    foreach (string text in Enum.GetNames(typeof(DebugHangarsBehaviorState)))
                    {
                        BehaviorComboBox.Items.Add(text);
                    }
                }
            }
        }

        private void CreateLavishCommands()
        {
            //if (Settings.Instance.UseInnerspace)
            //{
                LavishScript.Commands.AddCommand("SetAutoStart", SetAutoStart);
                LavishScript.Commands.AddCommand("SetDisable3D", SetDisable3D);
                LavishScript.Commands.AddCommand("SetExitWhenIdle", SetExitWhenIdle);
                LavishScript.Commands.AddCommand("SetQuestorStatetoCloseQuestor", SetQuestorStatetoCloseQuestor);
                LavishScript.Commands.AddCommand("SetQuestorStatetoIdle", SetQuestorStatetoIdle);
                LavishScript.Commands.AddCommand("SetCombatMissionsBehaviorStatetoGotoBase", SetCombatMissionsBehaviorStatetoGotoBase);
                LavishScript.Commands.AddCommand("SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase", SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase);
                LavishScript.Commands.AddCommand("QuestorCommands", ListQuestorCommands);
                LavishScript.Commands.AddCommand("QuestorEvents", ListQuestorEvents);
            //}
        }

        private int SetAutoStart(string[] args)
        {
            bool value;
            if (args.Length != 2 || !bool.TryParse(args[1], out value))
            {
                Logging.Log("QuestorUI", "SetAutoStart true|false", Logging.White);
                return -1;
            }

            Settings.Instance.AutoStart = value;

            Logging.Log("QuestorUI", "AutoStart is turned " + (value ? "[on]" : "[off]"), Logging.White);
            return 0;
        }

        private int SetDisable3D(string[] args)
        {
            bool value;
            if (args.Length != 2 || !bool.TryParse(args[1], out value))
            {
                Logging.Log("QuestorUI", "SetDisable3D true|false", Logging.White);
                return -1;
            }

            Settings.Instance.Disable3D = value;

            Logging.Log("QuestorUI", "Disable3D is turned " + (value ? "[on]" : "[off]"), Logging.White);
            return 0;
        }

        private int SetExitWhenIdle(string[] args)
        {
            bool value;
            if (args.Length != 2 || !bool.TryParse(args[1], out value))
            {
                Logging.Log("QuestorUI", "SetExitWhenIdle true|false", Logging.White);
                Logging.Log("QuestorUI", "Note: AutoStart is automatically turned off when ExitWhenIdle is turned on", Logging.White);
                return -1;
            }

            Cache.Instance.ExitWhenIdle = value;

            Logging.Log("QuestorUI", "ExitWhenIdle is turned " + (value ? "[on]" : "[off]"), Logging.White);

            if (value && Settings.Instance.AutoStart)
            {
                Settings.Instance.AutoStart = false;
                Logging.Log("QuestorUI", "AutoStart is turned [off]", Logging.White);
            }
            return 0;
        }

        private int SetQuestorStatetoCloseQuestor(string[] args)
        {
            if (args.Length != 1)
            {
                Logging.Log("QuestorUI", "SetQuestorStatetoCloseQuestor - Changes the QuestorState to CloseQuestor which will GotoBase and then Exit", Logging.White);
                return -1;
            }

            _States.CurrentQuestorState = QuestorState.CloseQuestor;

            Logging.Log("QuestorUI", "QuestorState is now: CloseQuestor ", Logging.White);
            return 0;
        }

        private int SetQuestorStatetoIdle(string[] args)
        {
            if (args.Length != 1)
            {
                Logging.Log("QuestorUI", "SetQuestorStatetoIdle - Changes the QuestorState to Idle", Logging.White);
                return -1;
            }

            _States.CurrentQuestorState = QuestorState.Idle;

            Logging.Log("QuestorUI", "QuestorState is now: Idle ", Logging.White);
            return 0;
        }

        private int SetCombatMissionsBehaviorStatetoGotoBase(string[] args)
        {
            if (args.Length != 1)
            {
                Logging.Log("QuestorUI", "SetCombatMissionsBehaviorStatetoGotoBase - Changes the CombatMissionsBehaviorState to GotoBase", Logging.White);
                return -1;
            }

            _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.GotoBase;

            Logging.Log("QuestorUI", "CombatMissionsBehaviorState is now: GotoBase ", Logging.White);
            return 0;
        }

        private int SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase(string[] args)
        {
            if (args.Length != 1)
            {
                Logging.Log("QuestorUI", "SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase - Changes the SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase to GotoBase", Logging.White);
                return -1;
            }

            _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.GotoBase;

            Logging.Log("QuestorUI", "SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase is now: GotoBase ", Logging.White);
            return 0;
        }

        private int ListQuestorCommands(string[] args)
        {
            Logging.Log("QuestorUI", " ", Logging.White);
            Logging.Log("QuestorUI", " ", Logging.White);
            Logging.Log("QuestorUI", "Questor commands you can run from innerspace", Logging.White);
            Logging.Log("QuestorUI", " ", Logging.White);
            Logging.Log("QuestorUI", "SetAutoStart                                 - SetAutoStart true|false", Logging.White);
            Logging.Log("QuestorUI", "SetDisable3D                                 - SetDisable3D true|false", Logging.White);
            Logging.Log("QuestorUI", "SetExitWhenIdle                              - SetExitWhenIdle true|false", Logging.White);
            Logging.Log("QuestorUI", "SetQuestorStatetoCloseQuestor                - SetQuestorStatetoCloseQuestor true", Logging.White);
            Logging.Log("QuestorUI", "SetQuestorStatetoIdle                        - SetQuestorStatetoIdle true", Logging.White);
            Logging.Log("QuestorUI", "SetCombatMissionsBehaviorStatetoGotoBase     - SetCombatMissionsBehaviorStatetoGotoBase true", Logging.White);
            Logging.Log("QuestorUI", "SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase     - SetDedicatedBookmarkSalvagerBehaviorStatetoGotoBase true", Logging.White);
            Logging.Log("QuestorUI", " ", Logging.White);
            Logging.Log("QuestorUI", "QuestorCommands                              - (this command) ", Logging.White);
            Logging.Log("QuestorUI", "QuestorEvents                                - Lists the available InnerSpace Events you can listen for ", Logging.White);
            Logging.Log("QuestorUI", " ", Logging.White);
            return 0;
        }

        private int ListQuestorEvents(string[] args)
        {
            Logging.Log("QuestorUI", " ", Logging.White);
            Logging.Log("QuestorUI", " ", Logging.White);
            Logging.Log("QuestorUI", "Questor Events you can listen for from an innerspace script", Logging.White);
            Logging.Log("QuestorUI", " ", Logging.White);
            Logging.Log("QuestorUI", "QuestorIdle                                   - This Event fires when entering the QuestorState Idle ", Logging.White);
            Logging.Log("QuestorUI", "QuestorState                                  - This Event fires when the State changes", Logging.White);
            Logging.Log("QuestorUI", "QuestorCombatMissionsBehaviorState            - This Event fires when the State changes", Logging.White);
            Logging.Log("QuestorUI", "QuestorDedicatedBookmarkSalvagerBehaviorState - This Event fires when the State changes", Logging.White);
            Logging.Log("QuestorUI", "QuestorAutoStartState                         - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", "QuestorExitWhenIdleState                      - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", "QuestorDisable3DState                         - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", "QuestorPanicState                             - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", "QuestorPausedState                            - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", "QuestorDronesState                            - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", "QuestorCombatState                            - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", "QuestorTravelerState                          - This Event fires when the State changes ", Logging.White);
            Logging.Log("QuestorUI", " ", Logging.White);
            return 0;
        }

        private void UpdateUiTick(object sender, EventArgs e)
        {
            //if (Settings.Instance.DebugUI) Logging.Log("QuestorUI", "UpdateUiTick", Logging.White);
            // The if's in here stop the UI from flickering
            string text = "Questor";
            if (Settings.Instance.CharacterName != string.Empty)
            {
                text = "Questor [" + Settings.Instance.CharacterName + "]";
            }
            if (Settings.Instance.CharacterName != string.Empty && Cache.Instance.Wealth > 10000000)
            {
                text = "Questor [" + Settings.Instance.CharacterName + "][" + String.Format("{0:0,0}", Cache.Instance.Wealth / 1000000) + "mil isk]";
            }

            if (Text != text)
                Text = text;

            lastSessionisreadyData.Text = "[" + Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastSessionIsReady).TotalSeconds, 0) + "] sec ago";
            LastFrameData.Text = "[" + Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastFrame).TotalSeconds, 0) + "] sec ago";
            lastInSpaceData.Text = "[" + Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastInSpace).TotalSeconds, 0) + "] sec ago";
            lastInStationData.Text = "[" + Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastInStation).TotalSeconds, 0) + "] sec ago";
            lastKnownGoodConnectedTimeData.Text = "[" + Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastKnownGoodConnectedTime).TotalMinutes, 0) + "] min ago";
            dataStopTimeSpecified.Text = Cache.Instance.StopTimeSpecified.ToString();

            if (Cache.Instance.SessionState == "Quitting")
            {
                if (_States.CurrentQuestorState != QuestorState.CloseQuestor)
                {
                    Cleanup.CloseQuestor();
                }
            }

            RefreshInfoDisplayedInUI();

            //
            // Left Group
            //
            if ((string)QuestorStateComboBox.SelectedItem != _States.CurrentQuestorState.ToString() && !QuestorStateComboBox.DroppedDown)
            {
                QuestorStateComboBox.SelectedItem = _States.CurrentQuestorState.ToString();
                _States.LavishEvent_QuestorState();
                if (_States.CurrentQuestorState == QuestorState.Idle)
                {
                    _States.LavishEvent_QuestorIdle();
                }
            }

            if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior)
            {
                if ((string)BehaviorComboBox.SelectedItem != _States.CurrentCombatMissionBehaviorState.ToString() && !BehaviorComboBox.DroppedDown)
                {
                    BehaviorComboBox.SelectedItem = _States.CurrentCombatMissionBehaviorState.ToString();
                    _States.LavishEvent_QuestorCombatMissionsBehaviorState();
                }
            }

            if (_States.CurrentQuestorState == QuestorState.Mining)
            {
                if ((string)BehaviorComboBox.SelectedItem != _States.CurrentMiningState.ToString() && !BehaviorComboBox.DroppedDown)
                {
                    BehaviorComboBox.SelectedItem = _States.CurrentMiningState.ToString();
                }
            }

            if (_States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior)
            {
                if ((string)BehaviorComboBox.SelectedItem != _States.CurrentDedicatedBookmarkSalvagerBehaviorState.ToString() && !BehaviorComboBox.DroppedDown)
                {
                    BehaviorComboBox.SelectedItem = _States.CurrentDedicatedBookmarkSalvagerBehaviorState.ToString();
                    _States.LavishEvent_QuestorDedicatedBookmarkSalvagerBehaviorState();
                }
            }

            if (_States.CurrentQuestorState == QuestorState.CombatHelperBehavior)
            {
                if ((string)BehaviorComboBox.SelectedItem != _States.CurrentCombatHelperBehaviorState.ToString() && !BehaviorComboBox.DroppedDown)
                {
                    BehaviorComboBox.SelectedItem = _States.CurrentCombatHelperBehaviorState.ToString();
                }
            }

            if (_States.CurrentQuestorState == QuestorState.DirectionalScannerBehavior)
            {
                if ((string)BehaviorComboBox.SelectedItem != _States.CurrentDirectionalScannerBehaviorState.ToString() && !BehaviorComboBox.DroppedDown)
                {
                    BehaviorComboBox.SelectedItem = _States.CurrentDirectionalScannerBehaviorState.ToString();
                }
            }

            if ((string)DamageTypeComboBox.SelectedItem != Cache.Instance.DamageType.ToString() && !DamageTypeComboBox.DroppedDown)
            {
                DamageTypeComboBox.SelectedItem = Cache.Instance.DamageType.ToString();
            }
            //
            // Middle group
            //
            if ((string)PanicStateComboBox.SelectedItem != _States.CurrentPanicState.ToString() && !PanicStateComboBox.DroppedDown)
            {
                PanicStateComboBox.SelectedItem = _States.CurrentPanicState.ToString();
                _States.LavishEvent_QuestorPanicState();
            }

            if ((string)CombatStateComboBox.SelectedItem != _States.CurrentCombatState.ToString() && !CombatStateComboBox.DroppedDown)
            {
                CombatStateComboBox.SelectedItem = _States.CurrentCombatState.ToString();
                _States.LavishEvent_QuestorCombatState();
            }

            if ((string)DronesStateComboBox.SelectedItem != _States.CurrentDroneState.ToString() && !DronesStateComboBox.DroppedDown)
            {
                DronesStateComboBox.SelectedItem = _States.CurrentDroneState.ToString();
                _States.LavishEvent_QuestorDronesState();
            }

            if ((string)CleanupStateComboBox.SelectedItem != _States.CurrentCleanupState.ToString() && !CleanupStateComboBox.DroppedDown)
            {
                CleanupStateComboBox.SelectedItem = _States.CurrentCleanupState.ToString();
            }

            if ((string)LocalWatchStateComboBox.SelectedItem != _States.CurrentLocalWatchState.ToString() && !LocalWatchStateComboBox.DroppedDown)
            {
                LocalWatchStateComboBox.SelectedItem = _States.CurrentLocalWatchState.ToString();
            }

            if ((string)SalvageStateComboBox.SelectedItem != _States.CurrentSalvageState.ToString() && !SalvageStateComboBox.DroppedDown)
            {
                SalvageStateComboBox.SelectedItem = _States.CurrentSalvageState.ToString();
            }

            //
            // Right Group
            //
            if ((string)CombatMissionCtrlStateComboBox.SelectedItem != text && !CombatMissionCtrlStateComboBox.DroppedDown)
            {
                CombatMissionCtrlStateComboBox.SelectedItem = text;
            }

            if ((string)StorylineStateComboBox.SelectedItem != _States.CurrentStorylineState.ToString() && !StorylineStateComboBox.DroppedDown)
            {
                StorylineStateComboBox.SelectedItem = _States.CurrentStorylineState.ToString();
            }

            if ((string)ArmStateComboBox.SelectedItem != _States.CurrentArmState.ToString() && !ArmStateComboBox.DroppedDown)
            {
                ArmStateComboBox.SelectedItem = _States.CurrentArmState.ToString();
            }

            if ((string)UnloadStateComboBox.SelectedItem != _States.CurrentUnloadLootState.ToString() && !UnloadStateComboBox.DroppedDown)
            {
                UnloadStateComboBox.SelectedItem = _States.CurrentUnloadLootState.ToString();
            }

            if ((string)TravelerStateComboBox.SelectedItem != _States.CurrentTravelerState.ToString() && !TravelerStateComboBox.DroppedDown)
            {
                TravelerStateComboBox.SelectedItem = _States.CurrentTravelerState.ToString();
                _States.LavishEvent_QuestorTravelerState();
            }

            if ((string)AgentInteractionStateComboBox.SelectedItem != _States.CurrentAgentInteractionState.ToString() && !AgentInteractionStateComboBox.DroppedDown)
            {
                AgentInteractionStateComboBox.SelectedItem = _States.CurrentAgentInteractionState.ToString();
            }

            //if (Settings.Instance.CharacterMode.ToLower() == "dps" || Settings.Instance.CharacterMode.ToLower() == "combat missions")
            //{
            //
            //}

            if (AutoStartCheckBox.Checked != Settings.Instance.AutoStart)
            {
                AutoStartCheckBox.Checked = Settings.Instance.AutoStart;
                _States.LavishEvent_QuestorAutoStartState();
            }

            if (PauseCheckBox.Checked != Cache.Instance.Paused)
            {
                PauseCheckBox.Checked = Cache.Instance.Paused;
                _States.LavishEvent_QuestorPausedState();
            }

            if (Disable3DCheckBox.Checked != Settings.Instance.Disable3D)
            {
                Disable3DCheckBox.Checked = Settings.Instance.Disable3D;
                _States.LavishEvent_QuestorDisable3DState();
            }

            if (Settings.Instance.WindowXPosition.HasValue)
            {
                Left = Settings.Instance.WindowXPosition.Value;
                Settings.Instance.WindowXPosition = null;
            }

            if (Settings.Instance.WindowYPosition.HasValue)
            {
                Top = Settings.Instance.WindowYPosition.Value;
                Settings.Instance.WindowYPosition = null;
            }

            if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.ExecuteMission && Cache.Instance.CurrentPocketAction != null)
            {
                string newlblCurrentPocketActiontext = "[ " + Cache.Instance.CurrentPocketAction + " ] Action";
                if (lblCurrentPocketAction.Text != newlblCurrentPocketActiontext)
                {
                    lblCurrentPocketAction.Text = newlblCurrentPocketActiontext;
                }
            }
            else if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Salvage ||
                     _States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.GotoSalvageBookmark ||
                     _States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.SalvageNextPocket ||
                     _States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.BeginAfterMissionSalvaging ||
                     _States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.SalvageUseGate)
            {
                const string newlblCurrentPocketActiontext = "[ " + "After Mission Salvaging" + " ] ";
                if (lblCurrentPocketAction.Text != newlblCurrentPocketActiontext)
                {
                    lblCurrentPocketAction.Text = newlblCurrentPocketActiontext;
                }
            }
            else
            {
                const string newlblCurrentPocketActiontext = "[ ]";
                if (lblCurrentPocketAction.Text != newlblCurrentPocketActiontext)
                {
                    lblCurrentPocketAction.Text = newlblCurrentPocketActiontext;
                }
            }

            if (!String.IsNullOrEmpty(Cache.Instance.MissionName))
            {
                if (!String.IsNullOrEmpty(Settings.Instance.MissionsPath))
                {
                    if (File.Exists(Cache.Instance.MissionXmlPath))
                    {
                        string newlblCurrentMissionInfotext = "[ " + Cache.Instance.MissionName + " ][ " + Math.Round(DateTime.UtcNow.Subtract(Statistics.Instance.StartedMission).TotalMinutes, 0) + " min][ #" + Statistics.Instance.MissionsThisSession + " ]";
                        if (lblCurrentMissionInfo.Text != newlblCurrentMissionInfotext)
                        {
                            lblCurrentMissionInfo.Text = newlblCurrentMissionInfotext;
                            buttonOpenMissionXML.Enabled = true;
                        }
                    }
                    else
                    {
                        string newlblCurrentMissionInfotext = "[ " + Cache.Instance.MissionName + " ][ " + Math.Round(DateTime.UtcNow.Subtract(Statistics.Instance.StartedMission).TotalMinutes, 0) + " min][ #" + Statistics.Instance.MissionsThisSession + " ]";
                        if (lblCurrentMissionInfo.Text != newlblCurrentMissionInfotext)
                        {
                            lblCurrentMissionInfo.Text = newlblCurrentMissionInfotext;
                            buttonOpenMissionXML.Enabled = false;
                        }
                    }
                }
            }
            else if (String.IsNullOrEmpty(Cache.Instance.MissionName))
            {
                lblCurrentMissionInfo.Text = Resources.QuestorfrmMain_UpdateUiTick_No_Mission_Selected_Yet;
                buttonOpenMissionXML.Enabled = false;
            }
            else
            {
                //lblCurrentMissionInfo.Text = "No Mission XML exists for this mission";
                buttonOpenMissionXML.Enabled = false;
            }

            if (Settings.Instance.DefaultSettingsLoaded)
            {
                buttonOpenCharacterXML.Enabled = false;
                buttonOpenSchedulesXML.Enabled = false;
                buttonQuestormanager.Enabled = false;
                buttonQuestorSettings.Enabled = false;
                buttonQuestorStatistics.Enabled = false;
            }
            else
            {
                if (Settings.Instance.CharacterXMLExists)
                {
                    buttonOpenCharacterXML.Enabled = true;
                    Settings.Instance.CharacterXMLExists = true;
                }
                else
                {
                    buttonOpenCharacterXML.Enabled = false;
                    Settings.Instance.CharacterXMLExists = false;
                }

                //
                // Does Schedules.xml exist in the directory where we started questor?
                //
                if (Settings.Instance.SchedulesXMLExists)
                {
                    buttonOpenCharacterXML.Enabled = true;
                    Settings.Instance.SchedulesXMLExists = true;
                }
                else
                {
                    buttonOpenSchedulesXML.Enabled = false;
                    Settings.Instance.SchedulesXMLExists = false;
                }

                //
                // Does QuestorStatistics.exe exist in the directory where we started questor?
                //
                if (Settings.Instance.QuestorStatisticsExists)
                {
                    buttonQuestorStatistics.Enabled = true;
                    Settings.Instance.QuestorStatisticsExists = true;
                }
                else
                {
                    buttonQuestorStatistics.Enabled = false;
                    Settings.Instance.QuestorStatisticsExists = false;
                }

                //
                // Does QuestorSettings.exe exist in the directory where we started questor?
                //
                if (Settings.Instance.QuestorSettingsExists)
                {
                    buttonQuestorSettings.Enabled = true;
                    Settings.Instance.QuestorSettingsExists = true;
                }
                else
                {
                    buttonQuestorSettings.Enabled = false;
                    Settings.Instance.QuestorSettingsExists = false;
                }

                //
                // Does Questormanager.exe exist in the directory where we started questor?
                //
                if (Settings.Instance.QuestorManagerExists)
                {
                    buttonQuestormanager.Enabled = true;
                    Settings.Instance.QuestorManagerExists = true;
                }
                else
                {
                    buttonQuestormanager.Enabled = false;
                    Settings.Instance.QuestorManagerExists = false;
                }
            }

            if (!String.IsNullOrEmpty(Cache.Instance.ExtConsole))
            {
                if (DateTime.UtcNow > _nextConsoleLogRefresh)
                {
                    if (txtExtConsole.Lines.Count() >= Settings.Instance.MaxLineConsole)
                    {
                        txtExtConsole.Text = "";
                    }
                    txtExtConsole.AppendText(Cache.Instance.ExtConsole);
                    Cache.Instance.ExtConsole = null;
                    _nextConsoleLogRefresh = DateTime.UtcNow.AddSeconds(1);
                }
            }

            int extraWaitSeconds = 0;
            if (!System.Diagnostics.Debugger.IsAttached) //do not restart due to no frames or Session.Isready aging if a debugger is attached until it reaches absurdity...
            {
                extraWaitSeconds = 60;
            }

            if (DateTime.UtcNow.Subtract(Cache.Instance.LastFrame).TotalSeconds > (Time.Instance.NoFramesRestart_seconds + extraWaitSeconds) && DateTime.UtcNow.Subtract(Program.AppStarted).TotalSeconds > 300)
            {
                if (DateTime.UtcNow.Subtract(Cache.Instance.LastLogMessage).TotalSeconds > 30)
                {
                    Logging.Log("QuestorUI", "The Last UI Frame Drawn by EVE was [" + Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastFrame).TotalSeconds, 0) + "] seconds ago! This is bad. - Exiting EVE", Logging.Red);

                    //
                    // closing eve would be a very good idea here
                    //
                    Cleanup.CloseQuestor();

                    //Application.Exit();
                }
            }

            if (DateTime.UtcNow.Subtract(Cache.Instance.LastSessionIsReady).TotalSeconds > (Time.Instance.NoSessionIsReadyRestart_seconds + extraWaitSeconds) &&
                    DateTime.UtcNow.Subtract(Program.AppStarted).TotalSeconds > 210)
            {
                if (DateTime.UtcNow.Subtract(Cache.Instance.LastLogMessage).TotalSeconds > 60)
                {
                    Logging.Log("QuestorUI", "The Last Session.IsReady = true was [" + Math.Round(DateTime.UtcNow.Subtract(Cache.Instance.LastSessionIsReady).TotalSeconds, 0) + "] seconds ago! This is bad. - Exiting EVE", Logging.Red);
                    Cleanup.CloseQuestor();

                    //Application.Exit();
                }
            }
        }

        private void DamageTypeComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            Cache.Instance.DamageType = (DamageType)Enum.Parse(typeof(DamageType), DamageTypeComboBox.Text);
        }

        private void PauseCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            Cache.Instance.Paused = PauseCheckBox.Checked;
            _States.LavishEvent_QuestorPausedState();
        }

        private void Disable3DCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            Settings.Instance.Disable3D = Disable3DCheckBox.Checked;
            _States.LavishEvent_QuestorDisable3DState();
        }

        private void TxtComandKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13)
            {
                if (Settings.Instance.UseInnerspace)
                {
                    LavishScript.ExecuteCommand(txtComand.Text);
                }
            }
        }

        private void ChkShowConsoleCheckedChanged(object sender, EventArgs e)
        {
            var frmMain = new Form();
            Size = chkShowDetails.Checked ? new System.Drawing.Size(707, 434) : new System.Drawing.Size(362, 124);
        }

        private void FrmMainLoad(object sender, EventArgs e)
        {
        }

        private void DisableMouseWheel(object sender, MouseEventArgs e)
        {
            ((HandledMouseEventArgs)e).Handled = true;
        }

        private void ButtonQuestorStatisticsClick(object sender, EventArgs e)
        {
            string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Process[] processes = System.Diagnostics.Process.GetProcessesByName("QuestorStatistics");

            if (processes.Length == 0)
            {
                // QuestorStatistics
                try
                {
                    System.Diagnostics.Process.Start(path + "\\QuestorStatistics.exe");
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    Logging.Log("QuestorUI", "QuestorStatistics could not be launched the error was: " + ex.Message, Logging.Orange);
                }
            }
        }

        private void ButtonOpenLogDirectoryClick(object sender, EventArgs e)
        {
            //string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            System.Diagnostics.Process.Start(Settings.Instance.Logpath);
        }

        private void ButtonOpenMissionXmlClick(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Launching [" + Cache.Instance.MissionXmlPath + "]", Logging.White);
            System.Diagnostics.Process.Start(Cache.Instance.MissionXmlPath);
        }

        private void QuestorStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentQuestorState = (QuestorState)Enum.Parse(typeof(QuestorState), QuestorStateComboBox.Text);
            if (Settings.Instance.DebugStates) Logging.Log("QuestorUI", "QuestorState has been changed to [" + QuestorStateComboBox.Text + "]", Logging.White);
            PopulateBehaviorStateComboBox();
            PopulateMissionLists();
            _States.LavishEvent_QuestorState();

            // If you are at the controls enough to change states... assume that panic needs to do nothing
            //_questor.panicstatereset = true; //this cannot be reset when the index changes, as that happens during natural state changes, this needs to be a mouse event
        }

        private void BehaviorComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            //Logging.Log("QuestorUI","BehaviorComboBoxChanged: Current QuestorState is: [" + _States.CurrentQuestorState + "]",Logging.White);
            if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior)
            {
                _States.CurrentCombatMissionBehaviorState = (CombatMissionsBehaviorState)Enum.Parse(typeof(CombatMissionsBehaviorState), BehaviorComboBox.Text);
                _States.LavishEvent_QuestorCombatMissionsBehaviorState();
            }

            if (_States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior)
            {
                _States.CurrentDedicatedBookmarkSalvagerBehaviorState = (DedicatedBookmarkSalvagerBehaviorState)Enum.Parse(typeof(DedicatedBookmarkSalvagerBehaviorState), BehaviorComboBox.Text);
                _States.LavishEvent_QuestorDedicatedBookmarkSalvagerBehaviorState();
            }

            if (_States.CurrentQuestorState == QuestorState.CombatHelperBehavior)
            {
                _States.CurrentCombatHelperBehaviorState = (CombatHelperBehaviorState)Enum.Parse(typeof(CombatHelperBehaviorState), BehaviorComboBox.Text);
            }

            if (_States.CurrentQuestorState == QuestorState.DirectionalScannerBehavior)
            {
                _States.CurrentDirectionalScannerBehaviorState = (DirectionalScannerBehaviorState)Enum.Parse(typeof(DirectionalScannerBehaviorState), BehaviorComboBox.Text);
            }

            //if (_States.CurrentQuestorState == QuestorState.DebugInventoryTree)
            //{
            //    _States.CurrentDe =
            //        (DebugHangarsState)
            //        Enum.Parse(typeof(DebugHangarsState), BehaviorComboBox.Text);
            //}

            if (_States.CurrentQuestorState == QuestorState.DebugHangarsBehavior)
            {
                _States.CurrentDebugHangarBehaviorState = (DebugHangarsBehaviorState)Enum.Parse(typeof(DebugHangarsBehaviorState), BehaviorComboBox.Text);
            }

            if (_States.CurrentQuestorState == QuestorState.Mining)
            {
                _States.CurrentMiningState = (MiningState)Enum.Parse(typeof(MiningState), BehaviorComboBox.Text);
            }

            try
            {
                AgentNameData.Text = Cache.Instance.CurrentAgentText;
                AgentEffectiveStandingsData.Text = Cache.Instance.AgentEffectiveStandingtoMeText;

                //DeclinedTimeData.Text = Cache.Instance.CurrentAgent.DeclineTimer;
                //
                // greylist info
                //
                MinAgentGreyListStandingsData.Text = Math.Round(Settings.Instance.MinAgentGreyListStandings, 2).ToString(CultureInfo.InvariantCulture);
                LastGreylistedMissionDeclinedData.Text = Cache.Instance.LastGreylistMissionDeclined;
                greylistedmissionsdeclineddata.Text = Cache.Instance.GreyListedMissionsDeclined.ToString(CultureInfo.InvariantCulture);

                //
                // blacklist info
                //
                MinAgentBlackListStandingsData.Text = Math.Round(Settings.Instance.MinAgentBlackListStandings, 2).ToString(CultureInfo.InvariantCulture);
                LastBlacklistedMissionDeclinedData.Text = Cache.Instance.LastBlacklistMissionDeclined;
                blacklistedmissionsdeclineddata.Text = Cache.Instance.BlackListedMissionsDeclined.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                //if we get an exception here ignore it as it should not effect anything, the GUI is only displaying data collected and processed elsewhere
                if (Settings.Instance.DebugExceptions || (Settings.Instance.DebugUI)) Logging.Log("QuestorUI", "Exception was [" + ex.Message + "]", Logging.Teal);
            }
        }

        private void PanicStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentPanicState = (PanicState)Enum.Parse(typeof(PanicState), PanicStateComboBox.Text);

            // If you are at the controls enough to change states... assume that panic needs to do nothing
            //_questor.panicstatereset = true; //this cannot be reset when the index changes, as that happens during natural state changes, this needs to be a mouse event
            _States.LavishEvent_QuestorPanicState();
        }

        private void CombatStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentCombatState = (CombatState)Enum.Parse(typeof(CombatState), CombatStateComboBox.Text);
            _States.LavishEvent_QuestorCombatState();
        }

        private void DronesStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentDroneState = (DroneState)Enum.Parse(typeof(DroneState), DronesStateComboBox.Text);
            _States.LavishEvent_QuestorDronesState();
        }

        private void CleanupStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentCleanupState = (CleanupState)Enum.Parse(typeof(CleanupState), CleanupStateComboBox.Text);
        }

        private void LocalWatchStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentLocalWatchState = (LocalWatchState)Enum.Parse(typeof(LocalWatchState), LocalWatchStateComboBox.Text);
        }

        private void SalvageStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentSalvageState = (SalvageState)Enum.Parse(typeof(SalvageState), SalvageStateComboBox.Text);
        }

        private void StorylineStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentStorylineState = (StorylineState)Enum.Parse(typeof(StorylineState), StorylineStateComboBox.Text);
        }

        private void ArmStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentArmState = (ArmState)Enum.Parse(typeof(ArmState), ArmStateComboBox.Text);
        }

        private void UnloadStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentUnloadLootState = (UnloadLootState)Enum.Parse(typeof(UnloadLootState), UnloadStateComboBox.Text);
        }

        private void TravelerStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentTravelerState = (TravelerState)Enum.Parse(typeof(TravelerState), TravelerStateComboBox.Text);
            _States.LavishEvent_QuestorTravelerState();
        }

        private void AgentInteractionStateComboBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            _States.CurrentAgentInteractionState = (AgentInteractionState)Enum.Parse(typeof(AgentInteractionState), AgentInteractionStateComboBox.Text);
        }

        private void TxtExtConsoleTextChanged(object sender, EventArgs e)
        {
        }

        private void AutoStartCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            Settings.Instance.AutoStart = AutoStartCheckBox.Checked;
            _States.LavishEvent_QuestorAutoStartState();
        }

        private void ButtonOpenCharacterXMLClick(object sender, EventArgs e)
        {
            if (File.Exists(Settings.Instance.SettingsPath))
            {
                Logging.Log("QuestorUI", "Launching [" + Settings.Instance.SettingsPath + "]", Logging.White);
                System.Diagnostics.Process.Start(Settings.Instance.SettingsPath);
            }
            else
            {
                Logging.Log("QuestorUI", "Unable to open [" + Settings.Instance.SettingsPath + "] file not found", Logging.Orange);
            }
        }

        private void ButtonOpenSchedulesXMLClick(object sender, EventArgs e)
        {
            string schedulesXmlPath = Path.Combine(Settings.Instance.Path, "Schedules.xml");
            if (File.Exists(schedulesXmlPath))
            {
                Logging.Log("QuestorUI", "Launching [" + schedulesXmlPath + "]", Logging.White);
                System.Diagnostics.Process.Start(schedulesXmlPath);
            }
            else
            {
                Logging.Log("QuestorUI", "Unable to open [" + schedulesXmlPath + "] file not found", Logging.Orange);
            }
        }

        private void ButtonQuestormanagerClick(object sender, EventArgs e)
        {
            string questorManagerPath = Path.Combine(Settings.Instance.Path, "QuestorManager.exe");
            if (File.Exists(questorManagerPath))
            {
                if (Settings.Instance.UseInnerspace)
                {
                    Logging.Log("QuestorUI", "Launching [ dotnet QuestorManager QuestorManager ]", Logging.White);
                    LavishScript.ExecuteCommand("dotnet QuestorManager QuestorManager");
                }
                else
                {
                    Logging.Log("QuestorUI", "Launching [ dotnet QuestorManager QuestorManager ] - fix me",
                                Logging.White);
                }
            }
            else
            {
                Logging.Log("QuestorUI", "Unable to launch QuestorManager from [" + questorManagerPath + "] file not found", Logging.Orange);
            }
        }

        private void ButtonQuestorSettingsXMLClick(object sender, EventArgs e)
        {
            string questorSettingsPath = Path.Combine(Settings.Instance.Path, "QuestorSettings.exe");
            if (File.Exists(questorSettingsPath))
            {
                Logging.Log("QuestorUI", "Launching [" + Settings.Instance.Path + "\\QuestorSettings.exe" + "]",
                            Logging.White);
                System.Diagnostics.Process.Start(Settings.Instance.Path + "\\QuestorSettings.exe");
            }
            else
            {
                Logging.Log("QuestorUI", "Unable to launch QuestorSettings from [" + questorSettingsPath + "] file not found", Logging.Orange);
            }
        }

        private void ExitWhenIdleCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            Cache.Instance.ExitWhenIdle = ExitWhenIdleCheckBox.Checked;
            AutoStartCheckBox.Checked = false;
            Settings.Instance.AutoStart = false;
            _States.LavishEvent_QuestorExitWhenIdleState();
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            Cache.Instance.StopSessionAfterMissionNumber = (int)numericUpDown1.Value;
        }

        private void ReloadAllClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "ReloadAll button was pressed: changing QuestorState to ReloadAll- when done reloading it shoud return to the configured behavior", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugReloadAll;
        }

        private void OutOfAmmoClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "OutOfAmmo button was pressed: changing CombatState to OutOfAmmo", Logging.Teal);
            _States.CurrentCombatState = CombatState.OutOfAmmo;
        }

        private void BtnOpenShipsHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open ShipsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenShipsHangar;
        }

        private void BtnStackShipsHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack ShipsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackShipsHangar;
        }

        private void BtnOpenFreightContainerClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open Loot Container button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenLootContainer;
        }

        private void BtnStackFreightContainerClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack Loot Container button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackLootContainer;
        }

        private void BtnOpenCorpAmmoHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open Corp AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenCorpAmmoHangar;
        }

        private void BtnOpenCorpLootHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open Corp LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenCorpLootHangar;
        }

        private void BtnStackCorpAmmoHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack Corp AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackCorpAmmoHangar;
        }

        private void BtnStackCorpLootHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack Corp LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackCorpLootHangar;
        }

        private void BtnOpenAmmoHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenAmmoHangar;
        }

        private void BtnOpenLootHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenLootHangar;
        }

        private void BtnStackAmmoHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackAmmoHangar;
        }

        private void BtnStackLootHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackLootHangar;
        }

        private void BttnCloseLootContainerClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close LootContainer button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseLootContainer;
        }

        private void BttnCloseShipsHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close ShipsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseShipsHangar;
        }

        private void BttnCloseItemsHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close ItemsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseItemsHangar;
        }

        private void BttnCloseCorpAmmoHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close CorpAmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseCorpAmmoHangar;
        }

        private void BttnCloseCorpLootHangarClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close CorpLootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseCorpLootHangar;
        }

        private void BttnCloseAmmoHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseAmmoHangar;
        }

        private void BttnCloseLootHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseLootHangar;
        }

        private void BttnCloseAllInventoryWindowsClick(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close ALL Inventory Windows button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseAllInventoryWindows;
        }

        private void btnReadyItemsHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open ItemsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.ReadyItemsHangar;
        }

        private void btnStackItemsHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack ItemsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackItemsHangar;
        }

        private void bttnCloseItemsHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close ItemsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseItemsHangar;
        }

        private void btnOpenShipsHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open ShipsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenShipsHangar;
        }

        private void btnStackShipsHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack ShipsHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackShipsHangar;
        }

        private void bttnCloseShipsHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close ShipsHangar button was pressed - Closing All inventoryWindows", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseShipsHangar;
        }

        private void btnOpenFreightContainer_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open LootContainer button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenLootContainer;
        }

        private void btnStackFreightContainer_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack LootContainer button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackLootContainer;
        }

        private void bttnCloseLootContainer_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close LootContainer button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseLootContainer;
        }

        private void btnOpenCorpAmmoHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open CorpAmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenCorpAmmoHangar;
        }

        private void btnStackCorpAmmoHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack CorpAmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackCorpAmmoHangar;
        }

        private void bttnCloseCorpAmmoHangar_Click_1(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close CorpAmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseCorpAmmoHangar;
        }

        private void btnOpenCorpLootHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open CorpLootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenCorpLootHangar;
        }

        private void btnStackCorpLootHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack CorpLootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackCorpLootHangar;
        }

        private void bttnCloseCorpLootHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close CorpLootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseCorpLootHangar;
        }

        private void btnOpenAmmoHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenAmmoHangar;
        }

        private void btnStackAmmoHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackAmmoHangar;
        }

        private void bttnCloseAmmoHangar_Click_1(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseLootHangar;
        }

        private void btnOpenLootHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenLootHangar;
        }

        private void btnStackLootHangar_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack LootHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackLootHangar;
        }

        private void bttnCloseLootHangar_Click_1(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close AmmoHangar button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseAmmoHangar;
        }

        private void bttnQueryAmmoHangarID_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Get AmmoHangarID button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.GetAmmoHangarID;
        }

        private void bttnQueryLootHangarID_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Get LootHangarID button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.GetLootHangarID;
        }

        private void bttnOpenCargoHold_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Open Cargo Hold button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenCargoHold;
        }

        private void bttnStackCargoHold_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Stack Cargo Hold button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.StackCargoHold;
        }

        private void bttnCloseCargoHold_Click(object sender, EventArgs e)
        {
            Cache.Instance.Paused = false;
            Logging.Log("QuestorUI", "Close Cargo Hold button was pressed", Logging.Teal);
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.CloseCargoHold;
        }

        private void bttnMaintainConsoleLogs_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Starting Logging.MaintainConsoleLogs() Routine.", Logging.Debug);
            Logging.MaintainConsoleLogs();
        }

        private void btnSendTestEmail_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "This Button Sends a test email to the configured email address", Logging.Debug);
            Email.SendEmail("Someone Pushed the Test Email Button","This is Just a Test Email from Questor");
        }

        private void bttnResizeEVEWindow_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Running Innerspace command: timedcommand 150 WindowCharacteristics -size " + 1280 + "x" + 960, Logging.White);
            LavishScript.ExecuteCommand("timedcommand 150 WindowCharacteristics -size " + 1280 + "x" + 960);
            Logging.Log("QuestorUI", "Running Innerspace command: timedcommand 200 WindowCharacteristics -pos " + 0 + "," + 0, Logging.White);
            LavishScript.ExecuteCommand("timedcommand 200 WindowCharacteristics -pos " + 0 + "," + 0);
                        
        }

        private void bttnLogEntitiesOnGrid_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Starting Statistics.EntityStatistics(Cache.Instance.Entities); Routine.", Logging.Debug);
            Statistics.EntityStatistics(Cache.Instance.Entities);
        }

        private void bttnLogContainersOnGrid_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Statistics.EntityStatistics(Cache.Instance.Containers); Routine.", Logging.Debug);
            Statistics.EntityStatistics(Cache.Instance.Containers);
        }

        private void bttnOpenOreHold_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Open OreHold button was pressed", Logging.Debug);
            Cache.Instance.OpenOreHold("OpenOreHold");
        }

        private void bttnOpenInventory_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Open Inventory button was pressed", Logging.Debug);
            //Cache.Instance.OpenInventoryWindow("OpenInventory");

            Cache.Instance.Paused = false;
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.OpenInventory;
        }

        private void bttnListInvTree_Click(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "List InvTree button was pressed", Logging.Debug);
            //Cache.Instance.ListInvTree("ListInvTree");

            Cache.Instance.Paused = false;
            _States.CurrentQuestorState = QuestorState.DebugHangarsBehavior;
            _States.CurrentDebugHangarBehaviorState = DebugHangarsBehaviorState.ListInvTree;
        }

        private void brnSetStopTimetoNow_Click_1(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Setting: Cache.Instance.StopTime to [" + DateTime.Now + "] - the next Questor.Idle state should see and process this.", Logging.Debug);
            Cache.Instance.StopTimeSpecified = true;
            Cache.Instance.NextTimeCheckAction = DateTime.UtcNow;
            Cache.Instance.StopTime = DateTime.Now; //(use local time not UTC time because this is Arm schedule related timer)
        }

        private void btnSetQuestorQuittingFlag_Click_1(object sender, EventArgs e)
        {
            Logging.Log("QuestorUI", "Setting: Cache.Instance.SessionState to [Quitting] - the next Questor.Idle state should see and process this.", Logging.Debug);
            Cache.Instance.SessionState = "Quitting";
        }
    }
}