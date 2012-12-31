
using Questor.Modules.Caching;

namespace Questor.Modules.States
{
    using LavishScriptAPI;
    using Questor.Modules.Lookup;

    public class _States
    {
        public static void LavishEvent_QuestorIdle()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorIdleEvent = LavishScript.Events.RegisterEvent("QuestorIdle");
                LavishScript.Events.ExecuteEvent(QuestorIdleEvent, "");
            }
        }

        public static void LavishEvent_QuestorState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorStateEvent = LavishScript.Events.RegisterEvent("QuestorState");
                LavishScript.Events.ExecuteEvent(QuestorStateEvent, _States.CurrentQuestorState.ToString());
            }
        }

        public static void LavishEvent_SkillQueueHasRoom()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint SkillQueueHasRoomEvent = LavishScript.Events.RegisterEvent("SkillQueueHasRoom");
                LavishScript.Events.ExecuteEvent(SkillQueueHasRoomEvent, "");
            }
        }

        public static void LavishEvent_QuestorCombatMissionsBehaviorState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorCombatMissionsBehaviorStateEvent = LavishScript.Events.RegisterEvent("QuestorCombatMissionsBehaviorState");
                LavishScript.Events.ExecuteEvent(QuestorCombatMissionsBehaviorStateEvent, _States.CurrentCombatMissionBehaviorState.ToString());
            }
        }

        public static void LavishEvent_QuestorDedicatedBookmarkSalvagerBehaviorState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorDedicatedBookmarkSalvagerBehaviorStateEvent = LavishScript.Events.RegisterEvent("QuestorDedicatedBookmarkSalvagerBehaviorState");
                LavishScript.Events.ExecuteEvent(QuestorDedicatedBookmarkSalvagerBehaviorStateEvent, _States.CurrentDedicatedBookmarkSalvagerBehaviorState.ToString());
            }
        }

        public static void LavishEvent_QuestorAutoStartState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorAutoStartStateEvent = LavishScript.Events.RegisterEvent("QuestorAutoStartState");
                LavishScript.Events.ExecuteEvent(QuestorAutoStartStateEvent, Settings.Instance.AutoStart.ToString());
            }
        }

        public static void LavishEvent_QuestorExitWhenIdleState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorExitWhenIdleStateEvent = LavishScript.Events.RegisterEvent("QuestorExitWhenIdleState");
                LavishScript.Events.ExecuteEvent(QuestorExitWhenIdleStateEvent, Cache.Instance.ExitWhenIdle.ToString());
            }
        }

        public static void LavishEvent_QuestorDisable3DState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorDisable3DStateEvent = LavishScript.Events.RegisterEvent("QuestorDisable3DState");
                LavishScript.Events.ExecuteEvent(QuestorDisable3DStateEvent, Settings.Instance.Disable3D.ToString());
            }
        }

        public static void LavishEvent_QuestorPanicState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorPanicStateEvent = LavishScript.Events.RegisterEvent("QuestorPanicState");
                LavishScript.Events.ExecuteEvent(QuestorPanicStateEvent, _States.CurrentPanicState.ToString());
            }
        }

        public static void LavishEvent_QuestorPausedState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorPausedStateEvent = LavishScript.Events.RegisterEvent("QuestorPausedState");
                LavishScript.Events.ExecuteEvent(QuestorPausedStateEvent, Cache.Instance.Paused.ToString());
            }
        }

        public static void LavishEvent_QuestorDronesState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorDronesStateEvent = LavishScript.Events.RegisterEvent("QuestorDronesState");
                LavishScript.Events.ExecuteEvent(QuestorDronesStateEvent, _States.CurrentDroneState.ToString());
            }
        }

        public static void LavishEvent_QuestorCombatState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorCombatStateEvent = LavishScript.Events.RegisterEvent("QuestorCombatState");
                LavishScript.Events.ExecuteEvent(QuestorCombatStateEvent, _States.CurrentCombatState.ToString());
            }
        }

        public static void LavishEvent_QuestorTravelerState()
        {
            if (Settings.Instance.UseInnerspace)
            {
                uint QuestorTravelerStateEvent = LavishScript.Events.RegisterEvent("QuestorTravelerState");
                LavishScript.Events.ExecuteEvent(QuestorTravelerStateEvent, _States.CurrentTravelerState.ToString());
            }
        }

        //public static void LavishEvent_QuestorCurrentSolarSystem()
        //{
        //    if (Settings.Instance.UseInnerspace)
        //    {
        //        uint QuestorCurrentSolarSystemEvent = LavishScript.Events.RegisterEvent("QuestorCurrentSolarSystem");
        //        LavishScript.Events.ExecuteEvent(QuestorCurrentSolarSystemEvent, );
        //    }
        //}

        //public static void LavishEvent_QuestorisInSpace()
        //{
        //    if (Settings.Instance.UseInnerspace)
        //    {
        //        uint QuestorIsInSpaceEvent = LavishScript.Events.RegisterEvent("QuestorIsInSpace");
        //        LavishScript.Events.ExecuteEvent(QuestorIsInSpaceEvent, Cache.Instance.InSpace.ToString());
        //    }
        //}

        public static QuestorState CurrentQuestorState { get; set; }

        public static DroneState CurrentDroneState { get; set; }

        public static CleanupState CurrentCleanupState { get; set; }

        public static LocalWatchState CurrentLocalWatchState { get; set; }

        public static SalvageState CurrentSalvageState { get; set; }

        public static ScoopState CurrentScoopState { get; set; }

        public static PanicState CurrentPanicState { get; set; }

        public static CombatState CurrentCombatState { get; set; }

        public static MiningState CurrentMiningState { get; set; }

        public static TravelerState CurrentTravelerState { get; set; }

        public static CombatMissionsBehaviorState CurrentCombatMissionBehaviorState { get; set; }

        public static DedicatedBookmarkSalvagerBehaviorState CurrentDedicatedBookmarkSalvagerBehaviorState { get; set; }

        public static CombatHelperBehaviorState CurrentCombatHelperBehaviorState { get; set; }

        public static DirectionalScannerBehaviorState CurrentDirectionalScannerBehaviorState { get; set; }

        public static DebugBehaviorState CurrentDebugBehaviorState { get; set; }

        public static DebugHangarsBehaviorState CurrentDebugHangarBehaviorState { get; set; }

        public static CourierMissionCtrlState CurrentCourierMissionCtrlState { get; set; }

        public static CombatMissionCtrlState CurrentCombatMissionCtrlState { get; set; }

        //public static ActionState CurrentActionState { get; set; }
        public static AgentInteractionState CurrentAgentInteractionState { get; set; }

        public static ArmState CurrentArmState { get; set; }

        public static BuyState CurrentBuyState { get; set; }

        public static BuyLPIState CurrentBuyLPIState { get; set; }

        public static DebugModuleState CurrentDebugModuleState { get; set; }

        public static DropState CurrentDropState { get; set; }

        public static GrabState CurrentGrabState { get; set; }

        public static ScanInteractionState CurrentScanInteractionState { get; set; }

        public static SellState CurrentSellState { get; set; }

        public static SwitchShipState CurrentSwitchShipState { get; set; }

        public static UnloadLootState CurrentUnloadLootState { get; set; }

        public static ValueDumpState CurrentValueDumpState { get; set; }

        public static StorylineState CurrentStorylineState { get; set; }
    }
}