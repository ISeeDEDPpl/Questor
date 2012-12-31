// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor.Modules.BackgroundTasks
{
    using System;
    using System.Linq;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.States;

    public class Panic
    {
        private readonly Random _random = new Random();

        private double _lastNormalX;
        private double _lastNormalY;
        private double _lastNormalZ;

        private DateTime _resumeTime;
        private DateTime _nextWarpScrambledWarning = DateTime.UtcNow;
        private DateTime _lastPulse;

        private DateTime _lastWarpScrambled = DateTime.UtcNow;
        private DateTime _lastPriorityTargetLogging = DateTime.UtcNow;
        private bool _delayedResume;
        private int _randomDelay;
        private int BookmarkMyWreckAttempts;

        //public bool InMission { get; set; }

        public void ProcessState()
        {
            // Only pulse state changes every 500ms
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < 500) //default: 500ms
                return;
            _lastPulse = DateTime.UtcNow;

            switch (_States.CurrentPanicState)
            {
                case PanicState.Idle:
                    //
                    // below is the reasons we will start the panic state(s) - if the below is not met do nothing
                    //
                    if (Cache.Instance.InSpace &&
                        Cache.Instance.DirectEve.ActiveShip.Entity != null &&
                        !Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked)
                    {
                        _States.CurrentPanicState = PanicState.Normal;
                        return;
                    }
                    break;

                case PanicState.Normal:
                    if (Cache.Instance.InStation)
                    {
                        _States.CurrentPanicState = PanicState.Idle;
                    }
                    if (Cache.Instance.DirectEve.ActiveShip.Entity != null)
                    {
                        _lastNormalX = Cache.Instance.DirectEve.ActiveShip.Entity.X;
                        _lastNormalY = Cache.Instance.DirectEve.ActiveShip.Entity.Y;
                        _lastNormalZ = Cache.Instance.DirectEve.ActiveShip.Entity.Z;
                    }
                    if (Cache.Instance.DirectEve.ActiveShip.Entity == null)
                        return;

                    if (DateTime.UtcNow < Cache.Instance.LastSessionChange.AddSeconds(10))
                        return;

                    if ((long)Cache.Instance.DirectEve.ActiveShip.StructurePercentage == 0) //if your hull is 0 you are dead or bugged, wait.
                        return;

                    if (!Cache.Instance.InMission && Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Capsule)
                    {
                        Logging.Log("Panic", "You are in a Capsule, you must have died :(", Logging.Red);
                        _States.CurrentPanicState = PanicState.StartPanicking;
                    }
                    else if (Cache.Instance.InMission && Cache.Instance.InSpace && Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage < Settings.Instance.MinimumCapacitorPct && Cache.Instance.DirectEve.ActiveShip.GroupId != 31)
                    {
                        // Only check for cap-panic while in a mission, not while doing anything else
                        Logging.Log("Panic", "Start panicking, capacitor [" + Math.Round(Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage, 0) + "%] below [" + Settings.Instance.MinimumCapacitorPct + "%] S[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ShieldPercentage, 0) + "%] A[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ArmorPercentage, 0) + "%] C[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage, 0) + "%]", Logging.Red);

                        //Questor.panic_attempts_this_mission;
                        Cache.Instance.PanicAttemptsThisMission++;
                        Cache.Instance.PanicAttemptsThisPocket++;
                        _States.CurrentPanicState = PanicState.StartPanicking;
                    }
                    else if (Cache.Instance.InSpace && Cache.Instance.DirectEve.ActiveShip.ShieldPercentage < Settings.Instance.MinimumShieldPct)
                    {
                        Logging.Log("Panic", "Start panicking, shield [" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ShieldPercentage, 0) + "%] below [" + Settings.Instance.MinimumShieldPct + "%] S[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ShieldPercentage, 0) + "%] A[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ArmorPercentage, 0) + "%] C[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage, 0) + "%]", Logging.Red);
                        Cache.Instance.PanicAttemptsThisMission++;
                        Cache.Instance.PanicAttemptsThisPocket++;
                        _States.CurrentPanicState = PanicState.StartPanicking;
                    }
                    else if (Cache.Instance.InSpace && Cache.Instance.DirectEve.ActiveShip.ArmorPercentage < Settings.Instance.MinimumArmorPct)
                    {
                        Logging.Log("Panic", "Start panicking, armor [" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ArmorPercentage, 0) + "%] below [" + Settings.Instance.MinimumArmorPct + "%] S[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ShieldPercentage, 0) + "%] A[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.ArmorPercentage, 0) + "%] C[" + Math.Round(Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage, 0) + "%]", Logging.Red);
                        Cache.Instance.PanicAttemptsThisMission++;
                        Cache.Instance.PanicAttemptsThisPocket++;
                        _States.CurrentPanicState = PanicState.StartPanicking;
                    }

                    BookmarkMyWreckAttempts = 1; // reset to 1 when we are known to not be in a pod anymore

                    _delayedResume = false;
                    if (Cache.Instance.InMission)
                    {
                        if (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Capsule)
                        {
                            Logging.Log("Panic", "You are in a Capsule, you must have died in a mission :(", Logging.Red);
                            _States.CurrentPanicState = PanicState.BookmarkMyWreck;
                        }

                        int frigates = Cache.Instance.EntitiesNotSelf.Count(e => e.IsFrigate && e.IsPlayer);
                        int cruisers = Cache.Instance.EntitiesNotSelf.Count(e => e.IsCruiser && e.IsPlayer);
                        int battlecruisers = Cache.Instance.EntitiesNotSelf.Count(e => e.IsBattlecruiser && e.IsPlayer);
                        int battleships = Cache.Instance.EntitiesNotSelf.Count(e => e.IsBattleship && e.IsPlayer);
                        if (Settings.Instance.FrigateInvasionLimit > 0 && frigates >= Settings.Instance.FrigateInvasionLimit)
                        {
                            _delayedResume = true;

                            Cache.Instance.PanicAttemptsThisMission++;
                            Cache.Instance.PanicAttemptsThisPocket++;
                            _States.CurrentPanicState = PanicState.StartPanicking;
                            Logging.Log("Panic", "Start panicking, mission invaded by [" + frigates + "] frigates", Logging.Red);
                        }

                        if (Settings.Instance.CruiserInvasionLimit > 0 && cruisers >= Settings.Instance.CruiserInvasionLimit)
                        {
                            _delayedResume = true;

                            Cache.Instance.PanicAttemptsThisMission++;
                            Cache.Instance.PanicAttemptsThisPocket++;
                            _States.CurrentPanicState = PanicState.StartPanicking;
                            Logging.Log("Panic", "Start panicking, mission invaded by [" + cruisers + "] cruisers", Logging.Red);
                        }

                        if (Settings.Instance.BattlecruiserInvasionLimit > 0 && battlecruisers >= Settings.Instance.BattlecruiserInvasionLimit)
                        {
                            _delayedResume = true;

                            Cache.Instance.PanicAttemptsThisMission++;
                            Cache.Instance.PanicAttemptsThisPocket++;
                            _States.CurrentPanicState = PanicState.StartPanicking;
                            Logging.Log("Panic", "Start panicking, mission invaded by [" + battlecruisers + "] battlecruisers", Logging.Red);
                        }

                        if (Settings.Instance.BattleshipInvasionLimit > 0 && battleships >= Settings.Instance.BattleshipInvasionLimit)
                        {
                            _delayedResume = true;

                            Cache.Instance.PanicAttemptsThisMission++;
                            Cache.Instance.PanicAttemptsThisPocket++;
                            _States.CurrentPanicState = PanicState.StartPanicking;
                            Logging.Log("Panic", "Start panicking, mission invaded by [" + battleships + "] battleships", Logging.Red);
                        }

                        if (_delayedResume)
                        {
                            _randomDelay = (Settings.Instance.InvasionRandomDelay > 0 ? _random.Next(Settings.Instance.InvasionRandomDelay) : 0);
                            _randomDelay += Settings.Instance.InvasionMinimumDelay;
                        }
                    }

                    if (Cache.Instance.InSpace)
                    {
                        Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsWarpScramblingMe), Priority.WarpScrambler);
                        if (Settings.Instance.SpeedTank)
                        {
                            Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsWebbingMe), Priority.Webbing);
                            Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsTargetPaintingMe), Priority.TargetPainting);
                        }
                        Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsNeutralizingMe), Priority.Neutralizing);
                        Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsJammingMe), Priority.Jamming);
                        Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsSensorDampeningMe), Priority.Dampening);
                        if (Cache.Instance.Modules.Any(m => m.IsTurret))
                            Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsTrackingDisruptingMe), Priority.TrackingDisrupting);

                        if (Math.Round(DateTime.UtcNow.Subtract(_lastPriorityTargetLogging).TotalMinutes) > 5)
                        {
                            _lastPriorityTargetLogging = DateTime.UtcNow;
                            foreach (EntityCache target in Cache.Instance.PriorityTargets)
                            {
                                Logging.Log("Panic.ListPriorityTargets", "[" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away] WARP[" + target.IsWarpScramblingMe + "] ECM[" + target.IsJammingMe + "] Damp[" + target.IsSensorDampeningMe + "] TP[" + target.IsTargetPaintingMe + "] NEUT[" + target.IsNeutralizingMe + "]", Logging.Teal);
                                continue;
                            }
                        }
                    }
                    break;

                // NOTE: The difference between Panicking and StartPanicking is that the bot will move to "Panic" state once in warp & Panicking
                //       and the bot wont go into Panic mode while still "StartPanicking"
                case PanicState.StartPanicking:
                case PanicState.Panicking:

                    // Add any warp scramblers to the priority list
                    Cache.Instance.AddPriorityTargets(Cache.Instance.TargetedBy.Where(t => t.IsWarpScramblingMe), Priority.WarpScrambler);

                    // Failsafe, in theory would/should never happen
                    if (_States.CurrentPanicState == PanicState.Panicking && Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                    {
                        // Resume is the only state that will make Questor revert to combat mode
                        _States.CurrentPanicState = PanicState.Resume;
                        return;
                    }

                    if (Cache.Instance.InStation)
                    {
                        Logging.Log("Panic", "Entered a station, lower panic mode", Logging.White);
                        _States.CurrentPanicState = PanicState.Panic;
                    }

                    // Once we have warped off 500km, assume we are "safer"
                    if (_States.CurrentPanicState == PanicState.StartPanicking && Cache.Instance.DistanceFromMe(_lastNormalX, _lastNormalY, _lastNormalZ) > (int)Distance.PanicDistanceToConsiderSafelyWarpedOff)
                    {
                        Logging.Log("Panic", "We've warped off:  Current ShipType: [" + Cache.Instance.DirectEve.ActiveShip.TypeName + "] Current ShipName [" + Cache.Instance.DirectEve.ActiveShip.GivenName + "]", Logging.White);
                        _States.CurrentPanicState = PanicState.Panicking;
                    }

                    // We leave the panicking state once we actually start warping off
                    EntityCache station = Cache.Instance.Stations.FirstOrDefault();
                    if (station != null && Cache.Instance.InSpace)
                    {
                        if (Cache.Instance.InWarp)
                            break;

                        if (station.Distance > (int)Distance.WarptoDistance)
                        {
                            NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjectsandGates.FirstOrDefault(),"Panic");
                            if (Cache.Instance.PriorityTargets.Any(pt => pt.IsWarpScramblingMe))
                            {
                                EntityCache WarpScrambledBy = Cache.Instance.PriorityTargets.FirstOrDefault(pt => pt.IsWarpScramblingMe);
                                if (WarpScrambledBy != null && DateTime.UtcNow > _nextWarpScrambledWarning)
                                {
                                    _nextWarpScrambledWarning = DateTime.UtcNow.AddSeconds(20);
                                    Logging.Log("Panic", "We are scrambled by: [" + Logging.White + WarpScrambledBy.Name + Logging.Orange + "][" + Logging.White + Math.Round(WarpScrambledBy.Distance, 0) + Logging.Orange + "][" + Logging.White + WarpScrambledBy.Id + Logging.Orange + "]",
                                                Logging.Orange);
                                    _lastWarpScrambled = DateTime.UtcNow;
                                }
                            }
                            else
                            {
                                if (DateTime.UtcNow > Cache.Instance.NextWarpTo || DateTime.UtcNow.Subtract(_lastWarpScrambled).TotalSeconds < Time.Instance.WarpScrambledNoDelay_seconds) //this will effectively spam warpto as soon as you are free of warp disruption if you were warp disrupted in the past 10 seconds)
                                {
                                    Logging.Log("Panic", "Warping to [" + station.Name + "][" + Math.Round((station.Distance / 1000) / 149598000, 2) + " AU away]", Logging.Red);
                                    Cache.Instance.IsMissionPocketDone = true;
                                    station.WarpToAndDock();
                                }
                                else
                                {
                                    Logging.Log("Panic", "Warping will be attempted again after [" + Math.Round(Cache.Instance.NextWarpTo.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.Red);
                                }

                                if (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Capsule)
                                {
                                    Logging.Log("Panic", "You are in a Capsule, you must have died :(", Logging.Red);
                                }
                            }
                        }
                        else
                        {
                            if (station.Distance < (int)Distance.DockingRange)
                            {
                                if (DateTime.UtcNow > Cache.Instance.NextDockAction)
                                {
                                    Logging.Log("Panic", "Docking with [" + station.Name + "][" + Math.Round((station.Distance / 1000) / 149598000, 2) + " AU away]", Logging.Red);
                                    station.Dock();
                                }
                                else if (Math.Round(Cache.Instance.NextUndockAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) > 2)
                                {
                                    Logging.Log("Panic", "Docking will be attempted in [" + Math.Round(Cache.Instance.NextUndockAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.Red);
                                }
                            }
                            else
                            {
                                if (DateTime.UtcNow > Cache.Instance.NextTravelerAction)
                                {
                                    if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != station.Id)
                                    {
                                        Logging.Log("Panic", "Approaching to [" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.Red);
                                        station.Approach();
                                        Cache.Instance.NextTravelerAction = DateTime.UtcNow.AddSeconds(Time.Instance.ApproachDelay_seconds);
                                    }
                                    else
                                    {
                                        Logging.Log("Panic", "Already Approaching to: [" + station.Name + "] which is [" + Math.Round(station.Distance / 1000, 0) + "k away]", Logging.Red);
                                    }
                                }
                                else
                                {
                                    Logging.Log("Panic", "Approaching has been delayed for [" + Math.Round(Cache.Instance.NextWarpTo.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.Red);
                                }
                            }
                        }
                        break;
                    }
                    else if (Cache.Instance.InSpace)
                    {
                        if (DateTime.UtcNow.Subtract(Cache.Instance.LastLoggingAction).TotalSeconds > 15)
                        {
                            Logging.Log("Panic", "No station found in local?", Logging.Red);
                        }
                    }

                    // What is this you say?  No star?
                    if (Cache.Instance.Star == null)
                        break;

                    if (Cache.Instance.Star.Distance > (int)Distance.WeCanWarpToStarFromHere)
                    {
                        if (Cache.Instance.InWarp)
                            break;

                        if (Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                        {
                            Logging.Log("Panic", "We are still warp scrambled!", Logging.Red); //This runs every 'tick' so we should see it every 1.5 seconds or so
                            _lastWarpScrambled = DateTime.UtcNow;
                        }
                        else if (DateTime.UtcNow > Cache.Instance.NextWarpTo | DateTime.UtcNow.Subtract(_lastWarpScrambled).TotalSeconds < 10) //this will effectively spam warpto as soon as you are free of warp disruption if you were warp disrupted in the past 10 seconds
                        {
                            Logging.Log("Panic", "Warping to [" + Cache.Instance.Star.Name + "][" + Math.Round((Cache.Instance.Star.Distance / 1000) / 149598000, 2) + " AU away]", Logging.Red);
                            Cache.Instance.Star.WarpTo();
                        }
                        else
                        {
                            Logging.Log("Panic", "Warping has been delayed for [" + Math.Round(Cache.Instance.NextWarpTo.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.Red);
                        }
                    }
                    else
                    {
                        Logging.Log("Panic", "At the star, lower panic mode", Logging.Red);
                        _States.CurrentPanicState = PanicState.Panic;
                    }
                    break;

                case PanicState.BookmarkMyWreck:
                    BookmarkMyWreckAttempts++;
                    if(Cache.Instance.Wrecks.Any(i => i.Name.Contains(Settings.Instance.CombatShipName)))
                    {
                        Cache.Instance.CreateBookmark("Wreck: " + Settings.Instance.CombatShipName);
                        _States.CurrentPanicState = PanicState.StartPanicking;
                        break;
                    }

                    if (BookmarkMyWreckAttempts++ > 3)
                    {
                        _States.CurrentPanicState = PanicState.StartPanicking;
                        break;
                    }

                    break;

                case PanicState.Panic:

                    // Do not resume until you're no longer in a capsule
                    if (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Capsule)
                        break;

                    if (Cache.Instance.InStation)
                    {
                        if (Settings.Instance.UseStationRepair)
                        {
                            if (!Cache.Instance.RepairItems("Repair Function")) break; //attempt to use repair facilities if avail in station
                        }
                        Logging.Log("Panic", "We're in a station, resume mission", Logging.Red);
                        _States.CurrentPanicState = _delayedResume ? PanicState.DelayedResume : PanicState.Resume;
                    }

                    bool isSafe = Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage > Settings.Instance.SafeCapacitorPct;
                    isSafe &= Cache.Instance.DirectEve.ActiveShip.ShieldPercentage > Settings.Instance.SafeShieldPct;
                    isSafe &= Cache.Instance.DirectEve.ActiveShip.ArmorPercentage > Settings.Instance.SafeArmorPct;
                    if (isSafe)
                    {
                        if (Cache.Instance.InSpace)
                        {
                            Cache.Instance.RepairAll = true;
                        }
                        Logging.Log("Panic", "We've recovered, resume mission", Logging.Red);
                        _States.CurrentPanicState = _delayedResume ? PanicState.DelayedResume : PanicState.Resume;
                    }

                    if (_States.CurrentPanicState == PanicState.DelayedResume)
                    {
                        Logging.Log("Panic", "Delaying resume for " + _randomDelay + " seconds", Logging.Red);
                        Cache.Instance.IsMissionPocketDone = false;
                        _resumeTime = DateTime.UtcNow.AddSeconds(_randomDelay);
                    }
                    break;

                case PanicState.DelayedResume:
                    if (DateTime.UtcNow > _resumeTime)
                        _States.CurrentPanicState = PanicState.Resume;
                    break;

                case PanicState.Resume:

                    // Don't do anything here
                    break;
            }
        }
    }
}