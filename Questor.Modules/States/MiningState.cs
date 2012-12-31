namespace Questor.Modules.States
{
    public enum MiningState
    {
        Default,
        Idle,
        Cleanup,
        Start,
        Arm,
        GotoBelt,
        Mine,
        MineAsteroid,
        GotoBase,
        UnloadLoot,
        Error,
        Paused,
        Panic,
    }
}