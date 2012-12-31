namespace Questor.Modules.States
{
    public enum DebugBehaviorState
    {
        Default,
        Idle,
        CombatHelper,
        Salvage,
        Arm,
        LocalWatch,
        DelayedGotoBase,
        GotoBase,
        UnloadLoot,
        GotoNearestStation,
        Error,
        Paused,
        Panic,
        Traveler,
        LogCombatTargets,
        LogDroneTargets,
        LogStationEntities,
        LogStargateEntities,
        LogAsteroidBelts,
        LogCansAndWrecks,
    }
}