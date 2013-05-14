// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor.Modules.Combat
{
    using System;
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;

    /// <summary>
    ///   The drones class will manage any and all drone related combat
    /// </summary>
    /// <remarks>
    ///   Drones will always work their way from lowest value target to highest value target and will only attack entities (not structures)
    /// </remarks>
    public class Drones
    {
        private double _armorPctTotal;
        private int _lastDroneCount;
        private DateTime _lastEngageCommand;
        private DateTime _lastRecallCommand;

        private int _recallCount;
        private DateTime _lastLaunch;
        private DateTime _lastRecall;

        private long _lastTarget;
        private DateTime _launchTimeout;
        private int _launchTries;
        private double _shieldPctTotal;
        private double _structurePctTotal;
        public bool Recall; //false
        public bool WarpScrambled; //false
        private DateTime _nextDroneAction = DateTime.UtcNow;
        private DateTime _nextWarpScrambledWarning = DateTime.MinValue;

        private void GetDamagedDrones()
        {
            foreach (EntityCache drone in Cache.Instance.ActiveDrones)
            {
                if (Settings.Instance.DebugDroneHealth) Logging.Log("Drones: GetDamagedDrones", "Health[" + drone.Health + "]" + "S[" + Math.Round(drone.ShieldPct, 3) + "]" + "A[" + Math.Round(drone.ArmorPct, 3) + "]" + "H[" + Math.Round(drone.StructurePct, 3) + "][ID" + drone.Id + "]", Logging.White);
            }
            Cache.Instance.DamagedDrones = Cache.Instance.ActiveDrones.Where(d => d.Health < Settings.Instance.BelowThisHealthLevelRemoveFromDroneBay);
        }

        private double GetShieldPctTotal()
        {
            if (!Cache.Instance.ActiveDrones.Any())
                return 0;

            return Cache.Instance.ActiveDrones.Sum(d => d.ShieldPct);
        }

        private double GetArmorPctTotal()
        {
            if (!Cache.Instance.ActiveDrones.Any())
                return 0;

            return Cache.Instance.ActiveDrones.Sum(d => d.ArmorPct);
        }

        private double GetStructurePctTotal()
        {
            if (!Cache.Instance.ActiveDrones.Any())
                return 0;

            return Cache.Instance.ActiveDrones.Sum(d => d.StructurePct);
        }

        /// <summary>
        ///   Engage the target
        /// </summary>
        private void EngageTarget()
        {
            if (Settings.Instance.DebugDrones) Logging.Log("Drones.EngageTarget", "Entering EngageTarget()", Logging.Debug);
                    
            // Find the first active weapon's target
            TargetingCache.CurrentDronesTarget = Cache.Instance.EntityById(_lastTarget);
            
            // Return best possible low value target
            Cache.Instance.GetBestDroneTarget(Settings.Instance.DroneControlRange, !Cache.Instance.DronesKillHighValueTargets, "Drones", Cache.Instance.potentialCombatTargets.ToList());

            if (Cache.Instance.PreferredDroneTarget != null && Cache.Instance.PreferredDroneTarget.Distance < Settings.Instance.DroneControlRange)
            {
                if (Settings.Instance.DebugDrones) Logging.Log("Drones.EngageTarget", "if (Cache.Instance.PreferredDroneTarget != null && Cache.Instance.PreferredDroneTarget.Distance < Settings.Instance.DroneControlRange)", Logging.Debug);
                EntityCache target = Cache.Instance.PreferredDroneTarget;

                // Nothing to engage yet, probably retargeting
                if (target == null)
                {
                    if (Settings.Instance.DebugDrones) Logging.Log("Drones.EngageTarget", "if (target == null)", Logging.Debug);
                    return;
                }

                
                if (target.IsBadIdea && !target.IsAttacking)
                {
                    if (Settings.Instance.DebugDrones) Logging.Log("Drones.EngageTarget", "if (target.IsBadIdea && !target.IsAttacking) return;", Logging.Debug);
                    return;
                }

                // Is our current target still the same and is the last Engage command no longer then 15s ago?
                if (_lastTarget == target.Id && DateTime.UtcNow.Subtract(_lastEngageCommand).TotalSeconds < 15)
                {
                    if (Settings.Instance.DebugDrones) Logging.Log("Drones.EngageTarget", "if (_lastTarget == target.Id && DateTime.UtcNow.Subtract(_lastEngageCommand).TotalSeconds < 15)", Logging.Debug);
                    return;
                }

                // Are we still actively shooting at the target?
                bool mustEngage = false;
                foreach (EntityCache drone in Cache.Instance.ActiveDrones)
                {
                    mustEngage |= drone.FollowId != target.Id;
                }

                if (!mustEngage)
                {
                    if (Settings.Instance.DebugDrones) Logging.Log("Drones.EngageTarget", "if (!mustEngage)", Logging.Debug);
                    return;
                }

                // Is the last target our current active target?
                if (target.IsActiveTarget)
                {
                    // Save target id (so we do not constantly switch)
                    _lastTarget = target.Id;

                    // Engage target
                    Logging.Log("Drones", "Engaging [ " + Cache.Instance.ActiveDrones.Count() + " ] drones on [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "]" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Magenta);
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdDronesEngage);
                    _lastEngageCommand = DateTime.UtcNow;
                }
                else // Make the target active
                {
                    target.MakeActiveTarget();
                    Logging.Log("Drones", "[" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away] is now the target for drones", Logging.Magenta);
                }

                return;
            }
            
            if (Settings.Instance.DebugDrones) Logging.Log("Drones.EngageTarget", "Cache.Instance.PreferredDroneTarget = null", Logging.Debug);
            return;
        }

        public void ProcessState()
        {
            if (_nextDroneAction > DateTime.UtcNow) return;
            
            _nextDroneAction = DateTime.UtcNow.AddMilliseconds(400);

            if (Cache.Instance.InStation ||                             // There is really no combat in stations (yet)
                !Cache.Instance.InSpace ||                              // if we are not in space yet, wait...
                Cache.Instance.DirectEve.ActiveShip.Entity == null ||   // What? No ship entity?
                Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || // There is no combat when cloaked
                !Cache.Instance.UseDrones                               // if UseDrones is false
                )
            {
                _States.CurrentDroneState = DroneState.Idle;
                return;
            }

            if (Cache.Instance.MyShipEntity.IsShipWithNoDroneBay)
            {
                _States.CurrentDroneState = DroneState.Idle;
                return;
            }

            if (!Cache.Instance.ActiveDrones.Any() && Cache.Instance.InWarp)
            {
                Cache.Instance.RemoveDronePriorityTargets(Cache.Instance.DronePriorityTargets);            
                _States.CurrentDroneState = DroneState.Idle;
                return;
            }

            switch (_States.CurrentDroneState)
            {
                case DroneState.WaitingForTargets:

                    // Are we in the right state ?
                    if (Cache.Instance.ActiveDrones.Any())
                    {
                        // Apparently not, we have drones out, go into fight mode
                        _States.CurrentDroneState = DroneState.Fighting;
                        break;
                    }

                    // Should we launch drones?
                    bool launch = true;

                    // Always launch if we're scrambled
                    if (!Cache.Instance.DronePriorityTargets.Any(pt => pt.IsWarpScramblingMe))
                    {
                        launch &= Cache.Instance.UseDrones;

                        // Are we done with this mission pocket?
                        launch &= !Cache.Instance.IsMissionPocketDone;

                        // If above minimums
                        launch &= Cache.Instance.DirectEve.ActiveShip.ShieldPercentage >= Settings.Instance.DroneMinimumShieldPct;
                        launch &= Cache.Instance.DirectEve.ActiveShip.ArmorPercentage >= Settings.Instance.DroneMinimumArmorPct;
                        launch &= Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage >= Settings.Instance.DroneMinimumCapacitorPct;

                        // yes if there are targets to kill
                        launch &= Cache.Instance.TargetedBy.Count(e => !e.IsSentry && e.CategoryId == (int)CategoryID.Entity && e.IsNpc && !e.IsContainer && e.GroupId != (int)Group.LargeColidableStructure && e.Distance < Settings.Instance.DroneControlRange) > 0;

                        if (_States.CurrentQuestorState != QuestorState.CombatMissionsBehavior)
                        {
                            launch &= Cache.Instance.Entities.Count(e => !e.IsSentry && !e.IsBadIdea && e.CategoryId == (int)CategoryID.Entity && e.IsNpc && !e.IsContainer && e.GroupId != (int)Group.LargeColidableStructure && e.Distance < Settings.Instance.DroneControlRange) > 0;
                        }

                        // If drones get aggro'd within 30 seconds, then wait (5 * _recallCount + 5) seconds since the last recall
                        if (_lastLaunch < _lastRecall && _lastRecall.Subtract(_lastLaunch).TotalSeconds < 30)
                        {
                            if (_lastRecall.AddSeconds(5 * _recallCount + 5) < DateTime.UtcNow)
                            {
                                // Increase recall count and allow the launch
                                _recallCount++;

                                // Never let _recallCount go above 5
                                if (_recallCount > 5)
                                    _recallCount = 5;
                            }
                            else
                            {
                                // Do not launch the drones until the delay has passed
                                launch = false;
                            }
                        }
                        else // Drones have been out for more then 30s
                            _recallCount = 0;
                    }

                    if (launch)
                    {
                        // Reset launch tries
                        _launchTries = 0;
                        _lastLaunch = DateTime.UtcNow;
                        _States.CurrentDroneState = DroneState.Launch;
                    }
                    break;

                case DroneState.Launch:

                    // Launch all drones
                    Recall = false;
                    _launchTimeout = DateTime.UtcNow;
                    Cache.Instance.DirectEve.ActiveShip.LaunchAllDrones();
                    _States.CurrentDroneState = DroneState.Launching;
                    break;

                case DroneState.Launching:

                    // We haven't launched anything yet, keep waiting
                    if (!Cache.Instance.ActiveDrones.Any())
                    {
                        if (DateTime.UtcNow.Subtract(_launchTimeout).TotalSeconds > 10)
                        {
                            // Relaunch if tries < 5
                            if (_launchTries < 5)
                            {
                                _launchTries++;
                                _States.CurrentDroneState = DroneState.Launch;
                                break;
                            }

                            _States.CurrentDroneState = DroneState.OutOfDrones;
                        }
                        break;
                    }

                    // Are we done launching?
                    if (_lastDroneCount == Cache.Instance.ActiveDrones.Count())
                    {
                        Logging.Log("Drones", "[" + Cache.Instance.ActiveDrones.Count() + "] Drones Launched", Logging.Magenta);
                        _States.CurrentDroneState = DroneState.Fighting;
                    }
                    break;

                case DroneState.OutOfDrones:

                    if (Cache.Instance.UseDrones && Settings.Instance.CharacterMode == "CombatMissions" && _States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.ExecuteMission)
                    {
                        if (Statistics.Instance.OutOfDronesCount >= 3)
                        {
                            Logging.Log("Drones", "We are Out of Drones! AGAIN - Headed back to base to stay!", Logging.Red);
                            _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.GotoBase;
                            Statistics.Instance.MissionCompletionErrors = 10; //this effectively will stop questor in station so we do not try to do this mission again, this needs human intervention if we have lots this many drones
                            Statistics.Instance.OutOfDronesCount++;
                        }

                        Logging.Log("Drones","We are Out of Drones! - Headed back to base to Re-Arm",Logging.Red);
                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.GotoBase;
                        Statistics.Instance.OutOfDronesCount++;
                        return;
                    }

                    TargetingCache.CurrentDronesTarget = null;
                    break;

                case DroneState.Fighting:

                    // Should we recall our drones? This is a possible list of reasons why we should

                    if (!Cache.Instance.ActiveDrones.Any())
                    {
                        Logging.Log("Drones", "Apparently we have lost all our drones", Logging.Orange);
                        Recall = true;
                    }
                    else
                    {
                        if (Cache.Instance.DronePriorityTargets.Any(pt => pt.IsWarpScramblingMe))
                        {
                            EntityCache WarpScrambledBy = Cache.Instance.Targets.OrderBy(d => d.Distance).FirstOrDefault(pt => pt.IsWarpScramblingMe);
                            if (WarpScrambledBy != null && DateTime.UtcNow > _nextWarpScrambledWarning)
                            {
                                _nextWarpScrambledWarning = DateTime.UtcNow.AddSeconds(20);
                                Logging.Log("Drones", "We are scrambled by: [" + Logging.White + WarpScrambledBy.Name + Logging.Orange + "][" + Logging.White + Math.Round(WarpScrambledBy.Distance, 0) + Logging.Orange + "][" + Logging.White + WarpScrambledBy.Id + Logging.Orange + "]",
                                            Logging.Orange);
                                Recall = false;
                                WarpScrambled = true;
                            }
                        }
                        else
                        {
                            //Logging.Log("Drones: We are not warp scrambled at the moment...");
                            WarpScrambled = false;
                        }
                    }

                    if (!Recall)
                    {
                        // Are we done (for now) ?
                        if (
                            Cache.Instance.TargetedBy.Count(e => !e.IsSentry 
                                                               && e.IsNpc 
                                                               && e.Distance < Settings.Instance.DroneControlRange) == 0)
                        {
                            Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones because no NPC is targeting us within dronerange", Logging.Magenta);
                            Recall = true;
                        }

                        if (!Recall & (Cache.Instance.IsMissionPocketDone) && !WarpScrambled)
                        {
                            Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones because we are done with this pocket.", Logging.Magenta);
                            Recall = true;
                        }
                        else if (!Recall & (_shieldPctTotal > GetShieldPctTotal()))
                        {
                            Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones because drones have lost some shields! [Old: " +
                                        _shieldPctTotal.ToString("N2") + "][New: " + GetShieldPctTotal().ToString("N2") +
                                        "]", Logging.Magenta);
                            Recall = true;
                        }
                        else if (!Recall & (_armorPctTotal > GetArmorPctTotal()))
                        {
                            Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones because drones have lost some armor! [Old:" +
                                        _armorPctTotal.ToString("N2") + "][New: " + GetArmorPctTotal().ToString("N2") +
                                        "]", Logging.Magenta);
                            Recall = true;
                        }
                        else if (!Recall & (_structurePctTotal > GetStructurePctTotal()))
                        {
                            Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones because drones have lost some structure! [Old:" +
                                        _structurePctTotal.ToString("N2") + "][New: " +
                                        GetStructurePctTotal().ToString("N2") + "]", Logging.Magenta);
                            Recall = true;
                        }
                        else if (!Recall & (Cache.Instance.ActiveDrones.Count() < _lastDroneCount))
                        {
                            // Did we lose a drone? (this should be covered by total's as well though)
                            Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones because we have lost a drone! [Old:" + _lastDroneCount +
                                        "][New: " + Cache.Instance.ActiveDrones.Count() + "]", Logging.Orange);
                            Recall = true;
                        }
                        else if (!Recall)
                        {
                            // Default to long range recall
                            int lowShieldWarning = Settings.Instance.LongRangeDroneRecallShieldPct;
                            int lowArmorWarning = Settings.Instance.LongRangeDroneRecallArmorPct;
                            int lowCapWarning = Settings.Instance.LongRangeDroneRecallCapacitorPct;

                            if (Cache.Instance.ActiveDrones.Average(d => d.Distance) <
                                (Settings.Instance.DroneControlRange / 2d))
                            {
                                lowShieldWarning = Settings.Instance.DroneRecallShieldPct;
                                lowArmorWarning = Settings.Instance.DroneRecallArmorPct;
                                lowCapWarning = Settings.Instance.DroneRecallCapacitorPct;
                            }

                            if (Cache.Instance.DirectEve.ActiveShip.ShieldPercentage < lowShieldWarning && !WarpScrambled)
                            {
                                Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to shield [" +
                                            Math.Round(Cache.Instance.DirectEve.ActiveShip.ShieldPercentage, 0) + "%] below [" +
                                            lowShieldWarning + "%] minimum", Logging.Orange);
                                Recall = true;
                            }
                            else if (Cache.Instance.DirectEve.ActiveShip.ArmorPercentage < lowArmorWarning && !WarpScrambled)
                            {
                                Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to armor [" +
                                            Math.Round(Cache.Instance.DirectEve.ActiveShip.ArmorPercentage, 0) + "%] below [" +
                                            lowArmorWarning + "%] minimum", Logging.Orange);
                                Recall = true;
                            }
                            else if (Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage < lowCapWarning && !WarpScrambled)
                            {
                                Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to capacitor [" +
                                            Math.Round(Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage, 0) + "%] below [" +
                                            lowCapWarning + "%] minimum", Logging.Orange);
                                Recall = true;
                            }
                            else if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior && !WarpScrambled)
                            {
                                if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.GotoBase && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to gotobase state", Logging.Orange);
                                    Recall = true;
                                }
                                else if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.GotoMission && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to gotomission state", Logging.Orange);
                                    Recall = true;
                                }
                                else if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Panic && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to panic state", Logging.Orange);
                                    Recall = true;
                                }
                            }
                            else if (_States.CurrentQuestorState == QuestorState.CombatHelperBehavior && !WarpScrambled)
                            {
                                if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.Panic && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to panic state", Logging.Orange);
                                    Recall = true;
                                }
                                else if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.GotoBase && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to panic state", Logging.Orange);
                                    Recall = true;
                                }
                            }
                            else if (_States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior && !WarpScrambled)
                            {
                                if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.GotoBase && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to gotobase state", Logging.Orange);
                                    Recall = true;
                                }
                                else if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.Panic && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to panic state", Logging.Orange);
                                    Recall = true;
                                }
                                else if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.GotoNearestStation && !WarpScrambled)
                                {
                                    Logging.Log("Drones", "Recalling [ " + Cache.Instance.ActiveDrones.Count() + " ] drones due to GotoNearestStation state", Logging.Orange);
                                    Recall = true;
                                }
                            }
                        }
                    }

                    // Recall or engage
                    if (Recall)
                    {
                        Statistics.Instance.DroneRecalls++;
                        _States.CurrentDroneState = DroneState.Recalling;
                    }
                    else
                    {
                        if (Settings.Instance.DebugDrones) Logging.Log("Drones.Fighting", "EngageTarget(); - before", Logging.Debug);
                    
                        EngageTarget();

                        if (Settings.Instance.DebugDrones) Logging.Log("Drones.Fighting", "EngageTarget(); - after", Logging.Debug);
                        // We lost a drone and did not recall, assume panicking and launch (if any) additional drones
                        if (Cache.Instance.ActiveDrones.Count() < _lastDroneCount)
                        {
                            _States.CurrentDroneState = DroneState.Launch;
                        }
                    }
                    break;

                case DroneState.Recalling:

                    // Are we done?
                    if (!Cache.Instance.ActiveDrones.Any())
                    {
                        _lastRecall = DateTime.UtcNow;
                        Recall = false;
                        TargetingCache.CurrentDronesTarget = null;
                        _nextDroneAction = DateTime.UtcNow.AddSeconds(3);
                        _States.CurrentDroneState = DroneState.WaitingForTargets;
                        break;
                    }

                    // Give recall command every 15 seconds
                    if (DateTime.UtcNow.Subtract(_lastRecallCommand).TotalSeconds > 15)
                    {
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdDronesReturnToBay);
                        _lastRecallCommand = DateTime.UtcNow;
                    }
                    break;

                case DroneState.Idle:

                    //
                    // below is the reasons we will start the combat state(s) - if the below is not met do nothing
                    //
                    if (Cache.Instance.InSpace &&
                        Cache.Instance.DirectEve.ActiveShip.Entity != null &&
                        !Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked &&
                        Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != Settings.Instance.CombatShipName &&
                        Cache.Instance.UseDrones &&
                        !Cache.Instance.InWarp)
                    {
                        _States.CurrentDroneState = DroneState.WaitingForTargets;
                        return;
                    }
                    TargetingCache.CurrentDronesTarget = null;
                    break;
            }

            // Update health values
            _shieldPctTotal = GetShieldPctTotal();
            _armorPctTotal = GetArmorPctTotal();
            _structurePctTotal = GetStructurePctTotal();
            _lastDroneCount = Cache.Instance.ActiveDrones.Count();
            GetDamagedDrones();
        }
    }
}