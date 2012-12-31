// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor.Modules.Activities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using System.Globalization;
    using Questor.Modules.BackgroundTasks;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.States;
    using global::Questor.Modules.Combat;
    using Questor.Modules.Caching;

    //using System.Reflection;

    public class CombatMissionCtrl
    {
        private DateTime? _clearPocketTimeout;
        private static int _currentAction;

        private double _lastX;
        private double _lastY;
        private double _lastZ;
        private static List<Actions.Action> _pocketActions;
        private bool _waiting;
        private DateTime _waitingSince;
        private DateTime _moveToNextPocket = DateTime.MaxValue;

        private bool _targetNull;

        public long AgentId { get; set; }

        public CombatMissionCtrl()
        {
            _targetNull = false;
            _pocketActions = new List<Actions.Action>();
        }

        public string Mission { get; set; }

        private void Nextaction()
        {
            // make sure all approach / orbit / align timers are reset (why cant we wait them out in the next action!?)
            Cache.Instance.NextApproachAction = DateTime.UtcNow;
            Cache.Instance.NextOrbit = DateTime.UtcNow;
            Cache.Instance.NextAlign = DateTime.UtcNow;
            // now that we have completed this action revert OpenWrecks to false
            Cache.Instance.OpenWrecks = false;
            Cache.Instance.MissionLoot = false;
            _currentAction++;
        }

        private void BookmarkPocketForSalvaging()
        {
            // Nothing to loot
            if (Cache.Instance.UnlootedContainers.Count() < Settings.Instance.MinimumWreckCount)
            {
                // If Settings.Instance.LootEverything is false we may leave behind a lot of unlooted containers.
                // This scenario only happens when all wrecks are within tractor range and you have a salvager
                // ( typically only with a Golem ).  Check to see if there are any cargo containers in space.  Cap
                // boosters may cause an unneeded salvage trip but that is better than leaving millions in loot behind.
                if (DateTime.UtcNow > Cache.Instance.NextBookmarkPocketAttempt)
                {
                    Cache.Instance.NextBookmarkPocketAttempt = DateTime.UtcNow.AddSeconds(Time.Instance.BookmarkPocketRetryDelay_seconds);
                    if (!Settings.Instance.LootEverything && Cache.Instance.Containers.Count() < Settings.Instance.MinimumWreckCount)
                    {
                        Logging.Log("CombatMissionCtrl", "No bookmark created because the pocket has [" + Cache.Instance.Containers.Count() + "] wrecks/containers and the minimum is [" + Settings.Instance.MinimumWreckCount + "]", Logging.Teal);
                    }
                    else if (Settings.Instance.LootEverything)
                    {
                        Logging.Log("CombatMissionCtrl", "No bookmark created because the pocket has [" + Cache.Instance.UnlootedContainers.Count() + "] wrecks/containers and the minimum is [" + Settings.Instance.MinimumWreckCount + "]", Logging.Teal);
                    }
                }
            }
            else
            {
                // Do we already have a bookmark?
                List<DirectBookmark> bookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.BookmarkPrefix + " ");
                DirectBookmark bookmark = bookmarks.FirstOrDefault(b => Cache.Instance.DistanceFromMe(b.X ?? 0, b.Y ?? 0, b.Z ?? 0) < (int)Distance.OnGridWithMe);
                if (bookmark != null)
                {
                    Logging.Log("CombatMissionCtrl", "Pocket already bookmarked for salvaging [" + bookmark.Title + "]", Logging.Teal);
                }
                else
                {
                    // No, create a bookmark
                    string label = string.Format("{0} {1:HHmm}", Settings.Instance.BookmarkPrefix, DateTime.UtcNow);
                    Logging.Log("CombatMissionCtrl", "Bookmarking pocket for salvaging [" + label + "]", Logging.Teal);
                    Cache.Instance.CreateBookmark(label);
                }
            }
        }

        private void DoneAction()
        {
            // Tell the drones module to retract drones
            Cache.Instance.IsMissionPocketDone = true;
            Cache.Instance.UseDrones = Settings.Instance.UseDrones;

            // We do not switch to "done" status if we still have drones out
            if (Cache.Instance.ActiveDrones.Any())
                return;

            // Add bookmark (before we're done)
            if (Settings.Instance.CreateSalvageBookmarks)
                BookmarkPocketForSalvaging();

            _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.Done;
        }

        private void LogWhatIsOnGridAction(Actions.Action action)
        {

            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Log Entities on Grid.", Logging.Teal);
            if (!Statistics.EntityStatistics(Cache.Instance.Entities)) return;
            Nextaction();
        }

        private void ActivateAction(Actions.Action action)
        {
            bool optional;
            if (!bool.TryParse(action.GetParameterValue("optional"), out optional))
            {
                optional = false;
            }

            string target = action.GetParameterValue("target");

            // No parameter? Although we should not really allow it, assume its the acceleration gate :)
            if (string.IsNullOrEmpty(target))
            {
                target = "Acceleration Gate";
            }

            IEnumerable<EntityCache> targets = Cache.Instance.EntitiesByName(target).ToList();
            if (!targets.Any())
            {
                if (!_waiting)
                {
                    Logging.Log("CombatMissionCtrl", "Activate: Can't find [" + target + "] to activate! Waiting 30 seconds before giving up", Logging.Teal);
                    _waitingSince = DateTime.UtcNow;
                    _waiting = true;
                }
                else if (_waiting)
                {
                    if (DateTime.UtcNow.Subtract(_waitingSince).TotalSeconds > Time.Instance.NoGateFoundRetryDelay_seconds)
                    {
                        Logging.Log("CombatMissionCtrl",
                                    "Activate: After 30 seconds of waiting the gate is still not on grid: CombatMissionCtrlState.Error",
                                    Logging.Teal);
                        if (optional) //if this action has the optional paramater defined as true then we are done if we cant find the gate
                        {
                            DoneAction();
                        }
                        else
                        {
                            _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.Error;
                        }
                    }
                }
                return;
            }

            //if (closest.Distance <= (int)Distance.CloseToGateActivationRange) // if your distance is less than the 'close enough' range, default is 7000 meters
            EntityCache closest = targets.OrderBy(t => t.Distance).First();
            if (closest.Distance <= (int)Distance.GateActivationRange)
            {
                if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "if (closest.Distance [" + closest.Distance + "] <= (int)Distance.GateActivationRange [" + (int)Distance.GateActivationRange + "])", Logging.Green);

                // Tell the drones module to retract drones
                Cache.Instance.IsMissionPocketDone = true;

                // We cant activate if we have drones out
                if (Cache.Instance.ActiveDrones.Any())
                {
                    if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "if (Cache.Instance.ActiveDrones.Any())", Logging.Green);
                    return;
                }

                //
                // this is a bad idea for a speed tank, we ought to somehow cache the object they are orbiting/approaching, etc
                // this seemingly slowed down the exit from certain missions for me for 2-3min as it had a command to orbit some random object
                // after the "done" command
                //
                if (closest.Distance < -10100)
                {
                    if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "if (closest.Distance < -10100)", Logging.Green);

                    if (DateTime.UtcNow > Cache.Instance.NextOrbit)
                    {
                        closest.Orbit(1000);
                        Logging.Log("CombatMissionCtrl", "Activate: We are too close to [" + closest.Name + "] Initiating orbit", Logging.Orange);
                    }
                    return;
                }

                if (closest.Distance >= -10100)
                {
                    if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "if (closest.Distance >= -10100)", Logging.Green);

                    // Add bookmark (before we activate)
                    if (Settings.Instance.CreateSalvageBookmarks)
                    {
                        BookmarkPocketForSalvaging();
                    }

                    if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "Activate: Reload before moving to next pocket", Logging.Teal);
                    if (!Combat.ReloadAll(Cache.Instance.MyShip)) return;
                    if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "Activate: Done reloading", Logging.Teal);

                    if (DateTime.UtcNow > Cache.Instance.NextActivateAction)
                    {
                        Logging.Log("CombatMissionCtrl", "Activate: [" + closest.Name + "] Move to next pocket after reload command and change state to 'NextPocket'", Logging.Green);
                        closest.Activate();

                        // Do not change actions, if NextPocket gets a timeout (>2 mins) then it reverts to the last action
                        _moveToNextPocket = DateTime.UtcNow;
                        _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.NextPocket;
                    }
                    return;
                }
                if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "------------------", Logging.Green);
                return;
            }

            if (closest.Distance < (int)Distance.WarptoDistance) //else if (closest.Distance < (int)Distance.WarptoDistance) //if we are inside warpto distance then approach
            {
                if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "if (closest.Distance < (int)Distance.WarptoDistance)", Logging.Green);

                // Move to the target
                if (DateTime.UtcNow > Cache.Instance.NextApproachAction)
                {
                    if (Cache.Instance.IsOrbiting || Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id || Cache.Instance.MyShip.Velocity < 100)
                    {
                        Logging.Log("CombatMissionCtrl.Activate", "Approaching target [" + closest.Name + "][ID: " + Cache.Instance.MaskedID(closest.Id) + "][" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
                        closest.Approach();
                        return;
                    }

                    if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "Cache.Instance.IsOrbiting [" + Cache.Instance.IsOrbiting + "] Cache.Instance.MyShip.Velocity [" + Cache.Instance.MyShip.Velocity + "]", Logging.Green);
                    if (Settings.Instance.DebugActivateGate) if (Cache.Instance.Approaching != null) Logging.Log("CombatMissionCtrl", "Cache.Instance.Approaching.Id [" + Cache.Instance.Approaching.Id + "][closest.Id: " + closest.Id + "]", Logging.Green);
                    if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "------------------", Logging.Green);
                    return;
                }

                if (Cache.Instance.IsOrbiting || Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)
                {
                    Logging.Log("CombatMissionCtrl", "Activate: Delaying approach for: [" + Math.Round(Cache.Instance.NextApproachAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "] seconds", Logging.Teal);
                    return;
                }

                if (Settings.Instance.DebugActivateGate) Logging.Log("CombatMissionCtrl", "------------------", Logging.Green);
                return;
            }

            if (closest.Distance > (int)Distance.WarptoDistance)//we must be outside warpto distance, but we are likely in a deadspace so align to the target
            {
                // We cant warp if we have drones out - but we are aligning not warping so we do not care
                //if (Cache.Instance.ActiveDrones.Count() > 0)
                //    return;

                if (DateTime.UtcNow > Cache.Instance.NextAlign)
                {
                    // Only happens if we are asked to Activate something that is outside Distance.CloseToGateActivationRange (default is: 6k)
                    Logging.Log("CombatMissionCtrl", "Activate: AlignTo: [" + closest.Name + "] This only happens if we are asked to Activate something that is outside [" + Distance.CloseToGateActivationRange + "]", Logging.Teal);
                    closest.AlignTo();
                    return;
                }

                Logging.Log("CombatMissionCtrl", "Activate: Unable to align: Next Align in [" + Cache.Instance.NextAlign.Subtract(DateTime.UtcNow).TotalSeconds + "] seconds", Logging.Teal);
                return;
            }

            Logging.Log("CombatMissionCtrl", "Activate: Error: [" + closest.Name + "] at [" + closest.Distance + "] is not within jump distance, within warpable distance or outside warpable distance, (!!!), retrying action.", Logging.Teal);
            return;
        }

        private void ClearAggroAction(Actions.Action action)
        {
            if (!Cache.Instance.NormalApproach) Cache.Instance.NormalApproach = true;

            // Get lowest range
            double range = Cache.Instance.MaxRange;
            int DistanceToClear;
            if (!int.TryParse(action.GetParameterValue("distance"), out DistanceToClear))
            {
                DistanceToClear = (int)range;
            }

            if (DistanceToClear != 0 && DistanceToClear != -2147483648 && DistanceToClear != 2147483647)
            {
                range = Math.Min(Cache.Instance.MaxRange, DistanceToClear);
            }

            // Is there a priority target out of range?
            EntityCache target = Cache.Instance.PriorityTargets.OrderBy(t => t.Distance).FirstOrDefault(t => !(Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()) && !Cache.Instance.TargetedBy.Any(w => w.IsWarpScramblingMe || w.IsNeutralizingMe || w.IsWebbingMe)));
            if (target == null)
            {
                _targetNull = true;
            }
            else
            {
                _targetNull = false;
            }

            // Or is there a target out of range that is targeting us?
            target = target ?? Cache.Instance.TargetedBy.Where(t => !t.IsSentry && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            if (Settings.Instance.KillSentries)
            {
                target = target ?? Cache.Instance.Entities.Where(t => !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            }

            int targetedby = Cache.Instance.TargetedBy.Count(t => !t.IsSentry && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()));

            if (target != null)
            {
                // Reset timeout
                _clearPocketTimeout = null;

                // Lock target if within weapons range
                if (target.Distance < range)
                {
                    //panic handles adding any priority targets and combat will prefer to kill any priority targets
                    if (_targetNull && targetedby == 0 && DateTime.UtcNow > Cache.Instance.NextReload)
                    {
                        if (!Combat.ReloadAll(target)) return;
                    }

                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (target.IsTarget || target.IsTargeting) //This target is already targeted no need to target it again
                        {
                            //noop
                        }
                        else if (!Cache.Instance.IgnoreTargets.Contains(target.Name.Trim()))
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            if (!target.LockTarget())
                            {
                                //
                                // if we cant lock the target wtf do we do?
                                // It is probably best to simply wait 10 sec and try again, the combat module will be clearing things 
                                // and the salvage behavior will be clearing wrecks
                                //
                            }
                        }
                        else if (Cache.Instance.IgnoreTargets.Contains(target.Name.Trim()))
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "We have attempted to target an NPC that is on the ignore list (why?) Name [" + target.Name + "][" + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                        }
                    }
                }
                NavigateOnGrid.NavigateIntoRange(target, "CombatMissionCtrl." + _pocketActions[_currentAction]);

                if (target.Distance > range) //target is not in range...
                {
                    if (DateTime.UtcNow > Cache.Instance.NextReload)
                    {
                        //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"ReloadAll: Reload weapons",Logging.teal);
                        if (!Combat.ReloadAll(target)) return;
                    }
                }
                return;
            }

            // Do we have a timeout?  No, set it to now + 5 seconds
            if (!_clearPocketTimeout.HasValue)
                _clearPocketTimeout = DateTime.UtcNow.AddSeconds(5);

            // Are we in timeout?
            if (DateTime.UtcNow < _clearPocketTimeout.Value)
                return;

            // We have cleared the Pocket, perform the next action \o/ - reset the timers that we had set for actions...
            Nextaction();

            // Reset timeout
            _clearPocketTimeout = null;
        }

        private void ClearPocketAction(Actions.Action action)
        {
            if (!Cache.Instance.NormalApproach)
                Cache.Instance.NormalApproach = true;

            // Get lowest range
            double range = Cache.Instance.MaxRange;
            int DistanceToClear;
            if (!int.TryParse(action.GetParameterValue("distance"), out DistanceToClear))
                DistanceToClear = (int)range;

            if (DistanceToClear != 0 && DistanceToClear != -2147483648 && DistanceToClear != 2147483647)
            {
                range = Math.Min(Cache.Instance.MaxRange, DistanceToClear);
            }

            //panic handles adding any priority targets and combat will prefer to kill any priority targets

            EntityCache target = null;

            // Or is there a target that is targeting us?
            target = target ?? Cache.Instance.TargetedBy.Where(t => !t.IsSentry && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();

            // Or is there any target?
            target = target ?? Cache.Instance.Entities.Where(t => !t.IsSentry && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            if (Settings.Instance.KillSentries)
            {
                target = target ?? Cache.Instance.Entities.Where(t => !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();
            }
            if (target == null)
                _targetNull = true;
            else
                _targetNull = false;

            int targetedby = Cache.Instance.TargetedBy.Count(t => !t.IsSentry && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()));

            if (target != null)
            {
                // Reset timeout
                _clearPocketTimeout = null;

                // Lock target if within weapons range
                if (target.Distance < range)
                {
                    //panic handles adding any priority targets and combat will prefer to kill any priority targets
                    if (_targetNull && targetedby == 0 && DateTime.UtcNow > Cache.Instance.NextReload)
                    {
                        if (!Combat.ReloadAll(target)) return;
                    }

                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (target.IsTarget || target.IsTargeting || target.IsActiveTarget || !target.IsValid) //This target is already targeted no need to target it again
                        {
                            //noop
                        }
                        else if (!Cache.Instance.IgnoreTargets.Contains(target.Name.Trim()))
                        {
                            if (target.LockTarget())
                            {
                                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            }
                        }

                        if (Cache.Instance.IgnoreTargets.Contains(target.Name.Trim()))
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "We have attempted to target an NPC that is on the ignore list (why?) Name [" + target.Name + "][" + target.Id + "][" + target.Distance + "]", Logging.Teal);
                        }
                    }
                }
                NavigateOnGrid.NavigateIntoRange(target, "CombatMissionCtrl." + _pocketActions[_currentAction]);

                if (target.Distance > range) //target is not in range...
                {
                    if (DateTime.UtcNow > Cache.Instance.NextReload)
                    {
                        //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"ReloadAll: Reload weapons",Logging.teal);
                        if (!Combat.ReloadAll(target)) return;
                    }
                }
                return;
            }

            // Do we have a timeout?  No, set it to now + 5 seconds
            if (!_clearPocketTimeout.HasValue)
                _clearPocketTimeout = DateTime.UtcNow.AddSeconds(5);

            // Are we in timeout?
            if (DateTime.UtcNow < _clearPocketTimeout.Value)
                return;

            // We have cleared the Pocket, perform the next action \o/ - reset the timers that we had set for actions...
            Nextaction();

            // Reset timeout
            _clearPocketTimeout = null;
        }

        private void ClearWithinWeaponsRangeOnlyAction(Actions.Action action)
        {
            // Get lowest range
            double DistanceToConsiderTargets = Cache.Instance.MaxRange;

            int DistanceToClear;
            if (!int.TryParse(action.GetParameterValue("distance"), out DistanceToClear))
                DistanceToClear = (int)DistanceToConsiderTargets;

            if (DistanceToClear != 0 && DistanceToClear != -2147483648 && DistanceToClear != 2147483647)
            {
                DistanceToConsiderTargets = Math.Min(Cache.Instance.MaxRange, DistanceToClear);
            }

            //
            // try to find priority targets to kill first (by definition they'd already be targeting us)
            //
            EntityCache target = null;
            if (Settings.Instance.SpeedTank)
                target = Cache.Instance.PriorityTargets.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < DistanceToConsiderTargets && !(Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()) && !Cache.Instance.TargetedBy.Any(w => w.IsWarpScramblingMe || w.IsNeutralizingMe || w.IsWebbingMe)));
            else
                target = Cache.Instance.PriorityTargets.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < DistanceToConsiderTargets && !(Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()) && !Cache.Instance.TargetedBy.Any(w => w.IsWarpScramblingMe || w.IsNeutralizingMe)));

            //
            // if we have no target yet is there a target within DistanceToConsiderTargets that is targeting us?
            //
            target = target ?? Cache.Instance.TargetedBy.Where(t => t.Distance < DistanceToConsiderTargets && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();

            // Or is there any target within DistanceToConsiderTargets?
            target = target ?? Cache.Instance.Entities.Where(t => t.Distance < DistanceToConsiderTargets && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();

            if (target != null)
            {
                // Reset timeout
                _clearPocketTimeout = null;

                // Lock priority target if within weapons range
                if (target.Distance < Cache.Instance.MaxRange)
                {
                    //panic handles adding any priority targets and combat will prefer to kill any priority targets
                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (target.IsTarget || target.IsTargeting) //This target is already targeted no need to target it again
                        {
                            //noop
                        }
                        else
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            target.LockTarget();
                        }
                    }
                    return;
                }
            }

            // Do we have a timeout?  No, set it to now + 5 seconds
            if (!_clearPocketTimeout.HasValue)
                _clearPocketTimeout = DateTime.UtcNow.AddSeconds(5);

            // Are we in timeout?
            if (DateTime.UtcNow < _clearPocketTimeout.Value)
                return;

            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "is complete: no more targets in weapons range", Logging.Teal);
            Nextaction();

            // Reset timeout
            _clearPocketTimeout = null;
        }

        private void ClearWithinWeaponsRangewAggroOnlyAction(Actions.Action action)
        {
            // Get lowest range
            double DistanceToConsiderTargets = Cache.Instance.MaxRange;

            int DistanceToClear;
            if (!int.TryParse(action.GetParameterValue("distance"), out DistanceToClear))
                DistanceToClear = (int)DistanceToConsiderTargets;

            if (DistanceToClear != 0 && DistanceToClear != -2147483648 && DistanceToClear != 2147483647)
            {
                DistanceToConsiderTargets = Math.Min(Cache.Instance.MaxRange, DistanceToClear);
            }

            EntityCache target = Cache.Instance.PriorityTargets.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < DistanceToConsiderTargets && !(Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()) && !Cache.Instance.TargetedBy.Any(w => w.IsWarpScramblingMe || w.IsNeutralizingMe || w.IsWebbingMe)));

            // Or is there a target within DistanceToConsiderTargets that is targeting us?
            target = target ?? Cache.Instance.TargetedBy.Where(t => t.Distance < DistanceToConsiderTargets && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim())).OrderBy(t => t.Distance).FirstOrDefault();

            if (target != null)
            {
                // Reset timeout
                _clearPocketTimeout = null;

                // Lock priority target if within weapons range
                if (target.Distance < Cache.Instance.MaxRange)
                {
                    //panic handles adding any priority targets and combat will prefer to kill any priority targets
                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (target.IsTarget || target.IsTargeting) //This target is already targeted no need to target it again
                        {
                            //noop
                        }
                        else
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            target.LockTarget();
                        }
                    }
                    return;
                }
            }

            // Do we have a timeout?  No, set it to now + 5 seconds
            if (!_clearPocketTimeout.HasValue)
                _clearPocketTimeout = DateTime.UtcNow.AddSeconds(5);

            // Are we in timeout?
            if (DateTime.UtcNow < _clearPocketTimeout.Value)
                return;

            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "is complete: no more targets that are targeting us", Logging.Teal);
            Nextaction();

            // Reset timeout
            _clearPocketTimeout = null;
        }

        private void OrbitEntityAction(Actions.Action action)
        {
            if (Cache.Instance.NormalApproach)
                Cache.Instance.NormalApproach = false;

            string target = action.GetParameterValue("target");

            bool notTheClosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out notTheClosest))
                notTheClosest = false;

            // No parameter? Although we should not really allow it, assume its the acceleration gate :)
            if (string.IsNullOrEmpty(target))
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "No Entity Specified to orbit: skipping OrbitEntity Action", Logging.Teal);
                Nextaction();
                return;
            }
               
            IEnumerable<EntityCache> targets = Cache.Instance.EntitiesByNamePart(target).ToList();
            if (!targets.Any())
            {
                // Unlike activate, no target just means next action
                _currentAction++;
                return;
            }

            EntityCache closest = targets.OrderBy(t => t.Distance).First();

            if (notTheClosest)
            {
                closest = targets.OrderByDescending(t => t.Distance).First();
            }

            // Move to the target
            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Setting [" + closest.Name + "][ID: " + Cache.Instance.MaskedID(closest.Id) + "][" + Math.Round(closest.Distance / 1000, 0) + "k away as the Orbit Target]", Logging.Teal);
            closest.Orbit(Cache.Instance.OrbitDistance);
            Nextaction();
        }

        private void MoveToBackgroundAction(Actions.Action action)
        {
            if (Cache.Instance.NormalApproach)
                Cache.Instance.NormalApproach = false;

            int DistanceToApproach;
            if (!int.TryParse(action.GetParameterValue("distance"), out DistanceToApproach))
                DistanceToApproach = (int)Distance.GateActivationRange;

            string target = action.GetParameterValue("target");

            // No parameter? Although we should not really allow it, assume its the acceleration gate :)
            if (string.IsNullOrEmpty(target))
                target = "Acceleration Gate";

            IEnumerable<EntityCache> targets = Cache.Instance.EntitiesByName(target).ToList();
            if (!targets.Any())
            {
                // Unlike activate, no target just means next action
                _currentAction++;
                return;
            }

            EntityCache closest = targets.OrderBy(t => t.Distance).First();

            // Move to the target
            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Approaching target [" + closest.Name + "][ID: " + Cache.Instance.MaskedID(closest.Id) + "][" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
            closest.Approach(DistanceToApproach);
            Nextaction();
        }

        private void MoveToAction(Actions.Action action)
        {
            if (Cache.Instance.NormalApproach)
                Cache.Instance.NormalApproach = false;

            string target = action.GetParameterValue("target");

            // No parameter? Although we should not really allow it, assume its the acceleration gate :)
            if (string.IsNullOrEmpty(target))
                target = "Acceleration Gate";

            int DistanceToApproach;
            if (!int.TryParse(action.GetParameterValue("distance"), out DistanceToApproach))
                DistanceToApproach = (int)Distance.GateActivationRange;

            bool stopWhenTargeted;
            if (!bool.TryParse(action.GetParameterValue("StopWhenTargeted"), out stopWhenTargeted))
                stopWhenTargeted = false;

            bool stopWhenAggressed;
            if (!bool.TryParse(action.GetParameterValue("StopWhenAggressed"), out stopWhenAggressed))
                stopWhenAggressed = false;

            IEnumerable<EntityCache> targets = Cache.Instance.EntitiesByName(target).ToList();
            if (!targets.Any())
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "no entities found named [" + target + "] proceeding to next action", Logging.Teal);
                Nextaction();
                return;
            }

            EntityCache closest = targets.OrderBy(t => t.Distance).First();

            if (stopWhenTargeted)
            {
                if (Cache.Instance.TargetedBy != null && Cache.Instance.TargetedBy.Any())
                {
                    if (Cache.Instance.Approaching != null)
                    {
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                        Cache.Instance.Approaching = null;
                        Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Stop ship, we have been targeted and are [" + DistanceToApproach + "] from [ID: " +
                                    closest.Name + "][" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
                    }
                }
            }

            if (stopWhenAggressed)
            {
                if (Cache.Instance.Aggressed.Any(t => !t.IsSentry))
                {
                    if (Cache.Instance.Approaching != null)
                    {
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                        Cache.Instance.Approaching = null;
                        Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Stop ship, we have been targeted and are [" + DistanceToApproach + "] from [ID: " +
                                    closest.Name + "][" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
                    }
                }
            }

            if (closest.Distance < DistanceToApproach) // if we are inside the range that we are supposed to approach assume we are done
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "We are [" + Math.Round(closest.Distance, 0) + "] from a [" + target + "] we do not need to go any further", Logging.Teal);
                Nextaction();

                if (Cache.Instance.Approaching != null)
                {
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                    Cache.Instance.Approaching = null;
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Stop ship, we are [" + DistanceToApproach + "] from [ID: " + closest.Name + "][" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
                }

                //if (Settings.Instance.SpeedTank)
                //{
                //    //this should at least keep speed tanked ships from going poof if a mission XML uses moveto
                //    closest.Orbit(Cache.Instance.OrbitDistance);
                //    Logging.Log("CombatMissionCtrl","MoveTo: Initiating orbit after reaching target")
                //}
                return;
            }

            if (closest.Distance < (int)Distance.WarptoDistance) // if we are inside warptorange you need to approach (you cant warp from here)
            {
                if (Settings.Instance.DebugMoveTo) Logging.Log("CombatMissionCtrl.MoveTo", "if (closest.Distance < (int)Distance.WarptoDistance)] -  NextApproachAction [" + Cache.Instance.NextApproachAction + "]", Logging.Teal);

                // Move to the target

                if (Settings.Instance.DebugMoveTo) if (Cache.Instance.Approaching == null) Logging.Log("CombatMissionCtrl.MoveTo", "if (Cache.Instance.Approaching == null)", Logging.Teal);
                if (Settings.Instance.DebugMoveTo) if (Cache.Instance.Approaching != null) Logging.Log("CombatMissionCtrl.MoveTo", "Cache.Instance.Approaching.Id [" + Cache.Instance.Approaching.Id + "]", Logging.Teal);
                if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id || Cache.Instance.MyShip.Velocity < 100)
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Approaching target [" + closest.Name + "][ID: " + Cache.Instance.MaskedID(closest.Id) + "][" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
                    closest.Approach();
                    return;
                }
                if (Settings.Instance.DebugMoveTo) if (Cache.Instance.Approaching != null) Logging.Log("CombatMissionCtrl.MoveTo", "-----------", Logging.Teal);
                return;
            }

            if (DateTime.UtcNow > Cache.Instance.NextAlign)
            {
                // Probably never happens
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Aligning to target [" + closest.Name + "][ID: " + Cache.Instance.MaskedID(closest.Id) + "][" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
                closest.AlignTo();
                return;
            }
            if (Settings.Instance.DebugMoveTo) Logging.Log("CombatMissionCtrl.MoveTo", "Nothing to do. Next Approach [" + Cache.Instance.NextApproachAction + " ] NextAlign [" + Cache.Instance.NextAlign + "]", Logging.Teal);
            return;
        }

        private void WaitUntilTargeted(Actions.Action action)
        {
            IEnumerable<EntityCache> targetedBy = Cache.Instance.TargetedBy;
            if (targetedBy != null && targetedBy.Any())
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "We have been targeted!", Logging.Teal);

                // We have been locked, go go go ;)
                _waiting = false;
                Nextaction();
                return;
            }

            // Default timeout is 30 seconds
            int timeout;
            if (!int.TryParse(action.GetParameterValue("timeout"), out timeout))
                timeout = 30;

            if (_waiting)
            {
                if (DateTime.UtcNow.Subtract(_waitingSince).TotalSeconds < timeout)
                    return;

                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Nothing targeted us within [ " + timeout + "sec]!", Logging.Teal);

                // Nothing has targeted us in the specified timeout
                _waiting = false;
                Nextaction();
                return;
            }

            // Start waiting
            _waiting = true;
            _waitingSince = DateTime.UtcNow;
        }

        private void DebuggingWait(Actions.Action action)
        {
            // Default timeout is 1200 seconds
            int timeout;
            if (!int.TryParse(action.GetParameterValue("timeout"), out timeout))
                timeout = 1200;

            if (_waiting)
            {
                if (DateTime.UtcNow.Subtract(_waitingSince).TotalSeconds < timeout)
                    return;

                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Nothing targeted us within [ " + timeout + "sec]!", Logging.Teal);

                // Nothing has targeted us in the specified timeout
                _waiting = false;
                Nextaction();
                return;
            }

            // Start waiting
            _waiting = true;
            _waitingSince = DateTime.UtcNow;
        }

        private void AggroOnlyAction(Actions.Action action)
        {
            if (Cache.Instance.NormalApproach)
                Cache.Instance.NormalApproach = false;

            bool ignoreAttackers;
            if (!bool.TryParse(action.GetParameterValue("ignoreattackers"), out ignoreAttackers))
                ignoreAttackers = false;

            bool breakOnAttackers;
            if (!bool.TryParse(action.GetParameterValue("breakonattackers"), out breakOnAttackers))
                breakOnAttackers = false;

            bool notTheClosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out notTheClosest))
                notTheClosest = false;

            int numberToIgnore;
            if (!int.TryParse(action.GetParameterValue("numbertoignore"), out numberToIgnore))
                numberToIgnore = 0;

            List<string> targetNames = action.GetParameterValues("target");

            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "No targets defined!", Logging.Teal);
                Nextaction();
                return;
            }

            IEnumerable<EntityCache> targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).ToList();
            if (targets.Count() == numberToIgnore)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "All targets gone " + targetNames.Aggregate((current, next) => current + "[" + next + "]"), Logging.Teal);

                // We killed it/them !?!?!? :)
                Nextaction();
                return;
            }

            if (Cache.Instance.Aggressed.Any(t => !t.IsSentry && targetNames.Contains(t.Name)))
            {
                // We are being attacked, break the kill order
                if (Cache.Instance.RemovePriorityTargets(targets))
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Done with AggroOnly: We have aggro.", Logging.Teal);

                foreach (EntityCache target in Cache.Instance.Targets.Where(e => targets.Any(t => t.Id == e.Id)))
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Unlocking [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away] due to aggro being obtained", Logging.Teal);
                    target.UnlockTarget();
                    return;
                }
                Nextaction();
                return;
            }

            if (!ignoreAttackers || breakOnAttackers)
            {
                // Apparently we are busy, wait for combat to clear attackers first
                IEnumerable<EntityCache> targetedBy = Cache.Instance.TargetedBy;
                if (targetedBy != null && targetedBy.Count(t => !t.IsSentry && t.Distance < Cache.Instance.WeaponRange) > 0)
                    return;
            }

            EntityCache closest = targets.OrderBy(t => t.Distance).First();

            if (notTheClosest)
                closest = targets.OrderByDescending(t => t.Distance).First();

            //panic handles adding any priority targets and combat will prefer to kill any priority targets
            if (Cache.Instance.PriorityTargets.All(pt => pt.Id != closest.Id))
            {
                //Adds the target we want to kill to the priority list so that combat.cs will kill it (especially if it is an LCO this is important)
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Adding [" + closest.Name + "][ID: " + closest.Id + "] as a priority target", Logging.Teal);
                Cache.Instance.AddPriorityTargets(new[] { closest }, Priority.PriorityKillTarget);
            }
        }

        private void KillAction(Actions.Action action)
        {
            if (Cache.Instance.NormalApproach)
                Cache.Instance.NormalApproach = false;

            bool ignoreAttackers;
            if (!bool.TryParse(action.GetParameterValue("ignoreattackers"), out ignoreAttackers))
            {
                ignoreAttackers = false;   
            }

            bool breakOnAttackers;
            if (!bool.TryParse(action.GetParameterValue("breakonattackers"), out breakOnAttackers))
            {
                breakOnAttackers = false;
            }

            bool notTheClosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out notTheClosest))
            {
                notTheClosest = false;
            }

            int numberToIgnore;
            if (!int.TryParse(action.GetParameterValue("numbertoignore"), out numberToIgnore))
            {
                numberToIgnore = 0;
            }

            List<string> targetNames = action.GetParameterValues("target");

            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "No targets defined in kill action!", Logging.Teal);
                Nextaction();
                return;
            }

            IEnumerable<EntityCache> targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).ToList();
            if (targets.Count() == numberToIgnore)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "All targets killed " + targetNames.Aggregate((current, next) => current + "[" + next + "]"), Logging.Teal);

                // We killed it/them !?!?!? :)
                Nextaction();
                return;
            }

            if (breakOnAttackers && Cache.Instance.TargetedBy.Any(t => !t.IsSentry && t.Distance < Cache.Instance.WeaponRange))
            {
                // We are being attacked, break the kill order
                if (Cache.Instance.RemovePriorityTargets(targets)) Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Breaking off kill order, new spawn has arrived!", Logging.Teal);

                foreach (EntityCache entity in Cache.Instance.Targets.Where(e => targets.Any(t => t.Id == e.Id)))
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Unlocking [" + entity.Name + "][ID: " + Cache.Instance.MaskedID(entity.Id) + "][" + Math.Round(entity.Distance / 1000, 0) + "k away] due to kill order being put on hold", Logging.Teal);
                    entity.UnlockTarget();
                }
            }

            if (!ignoreAttackers || breakOnAttackers)
            {
                // Apparently we are busy, wait for combat to clear attackers first
                IEnumerable<EntityCache> targetedBy = Cache.Instance.TargetedBy;
                if (targetedBy != null && targetedBy.Count(t => !t.IsSentry && t.Distance < Cache.Instance.WeaponRange) > 0)
                {
                    return;
                }
            }

            EntityCache target = targets.OrderBy(t => t.Distance).First();
            int targetedby = Cache.Instance.TargetedBy.Count(t => !t.IsSentry && !t.IsEntityIShouldLeaveAlone && !t.IsContainer && t.IsNpc && t.CategoryId == (int)CategoryID.Entity && t.GroupId != (int)Group.LargeCollidableStructure && !Cache.Instance.IgnoreTargets.Contains(t.Name.Trim()));
            if (target != null)
            {
                // Reset timeout
                _clearPocketTimeout = null;

                // Are we approaching the active (out of range) target?
                // Wait for it (or others) to get into range

                // Lock priority target if within weapons range

                if (notTheClosest) target = targets.OrderByDescending(t => t.Distance).First();

                if (target.Distance < Cache.Instance.MaxRange)
                {
                    //panic handles adding any priority targets and combat will prefer to kill any priority targets
                    if (Cache.Instance.PriorityTargets.All(pt => pt.Id != target.Id))
                    {
                        //Adds the target we want to kill to the priority list so that combat.cs will kill it (especially if it is an LCO this is important)
                        Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Adding [" + target.Name + "][ID: " + target.Id + "] as a priority target", Logging.Teal);
                        Cache.Instance.AddPriorityTargets(new[] { target }, Priority.PriorityKillTarget);
                    }
                    if (_targetNull && targetedby == 0 && DateTime.UtcNow > Cache.Instance.NextReload)
                    {
                        //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"Reload if [" + _targetNull + "] && [" + targetedby + "] == 0 AND [" + Math.Round(target.Distance, 0) + "] < [" + Cache.Instance.MaxRange + "]", Logging.teal);
                        if (!Combat.ReloadAll(target)) return;
                    }

                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (!(target.IsTarget || target.IsTargeting)) //This target is not targeted and need to target it
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            target.LockTarget();
                        }
                    }
                }
                NavigateOnGrid.NavigateIntoRange(target, "CombatMissionCtrl." + _pocketActions[_currentAction]);
                if (target.Distance > Cache.Instance.MaxRange)
                {
                    if (DateTime.UtcNow > Cache.Instance.NextReload)
                    {
                        //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"ReloadAll: Reload weapons", Logging.teal);
                        if (!Combat.ReloadAll(target)) return;
                    }
                }
                return;
            }
        }

        private void KillOnceAction(Actions.Action action)
        {
            if (Cache.Instance.NormalApproach) Cache.Instance.NormalApproach = false;

            bool notTheClosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out notTheClosest))
            {
                notTheClosest = false;
            }

            int numberToIgnore;
            if (!int.TryParse(action.GetParameterValue("numbertoignore"), out numberToIgnore))
            {
                numberToIgnore = 0;
            }

            List<string> targetNames = action.GetParameterValues("target");

            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "No targets defined in kill action!", Logging.Orange);
                Nextaction();
                return;
            }

            IEnumerable<EntityCache> targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).ToList();
            if (targets.Count() == numberToIgnore)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "All targets killed " + targetNames.Aggregate((current, next) => current + "[" + next + "]"), Logging.Teal);

                // We killed it/them !?!?!? :)
                Nextaction();
                return;
            }

            EntityCache target = targets.OrderBy(t => t.Distance).First();

            if (target != null)
            {
                // Reset timeout
                _clearPocketTimeout = null;

                // Are we approaching the active (out of range) target?
                // Wait for it (or others) to get into range

                // Lock priority target if within weapons range

                if (notTheClosest) target = targets.OrderByDescending(t => t.Distance).First();

                if (target.Distance < Cache.Instance.MaxRange)
                {
                    //panic handles adding any priority targets and combat will prefer to kill any priority targets
                    if (Cache.Instance.PriorityTargets.All(pt => pt.Id != target.Id))
                    {
                        //Adds the target we want to kill to the priority list so that combat.cs will kill it (especially if it is an LCO this is important)
                        Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Adding [" + target.Name + "][ID: " + target.Id + "] as a priority target", Logging.Teal);
                        Cache.Instance.AddPriorityTargets(new[] { target }, Priority.PriorityKillTarget);
                    }
                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (!(target.IsTarget || target.IsTargeting)) //This target is not targeted and need to target it
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            target.LockTarget();

                            // the target has been added to the priority targets list and has been targeted.
                            // this should ensure that the combat module (and/or the next action) kills the target.
                            Nextaction();
                            return;
                        }
                    }
                }
                NavigateOnGrid.NavigateIntoRange(target, "CombatMissionCtrl." + _pocketActions[_currentAction]);
                return;
            }
        }

        private void UseDrones(Actions.Action action)
        {
            bool usedrones;
            if (!bool.TryParse(action.GetParameterValue("use"), out usedrones))
            {
                usedrones = true;
            }

            if (!usedrones)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Disable launch of drones", Logging.Teal);
                Cache.Instance.UseDrones = false;
            }
            else
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Enable launch of drones", Logging.Teal);
                Cache.Instance.UseDrones = Settings.Instance.UseDrones;
            }
            Nextaction();
            return;
        }

        private void KillClosestByNameAction(Actions.Action action)
        {
            bool notTheClosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out notTheClosest))
            {
                notTheClosest = false;
            }

            if (Cache.Instance.NormalApproach) Cache.Instance.NormalApproach = false;

            List<string> targetNames = action.GetParameterValues("target");

            // No parameter? Ignore kill action
            if (targetNames.Count == 0)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "No targets defined!", Logging.Teal);
                Nextaction();
                return;
            }

            //IEnumerable<EntityCache> targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name));
            EntityCache target = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).OrderBy(t => t.Distance).First();
            if (notTheClosest) target = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name)).OrderByDescending(t => t.Distance).First();

            if (target != null)
            {
                if (target.Distance < Cache.Instance.MaxRange)
                {
                    if (Cache.Instance.PriorityTargets.All(pt => pt.Id != target.Id))
                    {
                        Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Adding [" + target.Name + "][ID: " + target.Id + "] as a priority target", Logging.Teal);
                        Cache.Instance.AddPriorityTargets(new[] { target }, Priority.PriorityKillTarget);
                    }

                    if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                    {
                        if (!(target.IsTarget || target.IsTargeting))

                        //This target is not targeted and need to target it
                        {
                            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            target.LockTarget();

                            // the target has been added to the priority targets list and has been targeted.
                            // this should ensure that the combat module (and/or the next action) kills the target.
                            Nextaction();
                            return;
                        }
                    }
                }
                NavigateOnGrid.NavigateIntoRange(target, "CombatMissionCtrl." + _pocketActions[_currentAction]);
            }
            else
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "All targets killed, not valid anymore ", Logging.Teal);

                // We killed it/them !?!?!? :)
                Nextaction();
                return;
            }
        }

        private void KillClosestAction(Actions.Action action)
        {
            if (Cache.Instance.NormalApproach) Cache.Instance.NormalApproach = false;

            bool notTheClosest;
            if (!bool.TryParse(action.GetParameterValue("notclosest"), out notTheClosest))
            {
                notTheClosest = false;
            }

            //IEnumerable<EntityCache> targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name));
            EntityCache target = Cache.Instance.Entities.OrderBy(t => t.Distance).First();
            if (notTheClosest) target = Cache.Instance.Entities.OrderByDescending(t => t.Distance).First();

            if (!target.IsValid)
            {
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "All targets killed, not valid anymore ", Logging.Teal);

                // We killed it/them !?!?!? :)
                Nextaction();
                return;
            }

            if (target.Distance < Cache.Instance.MaxRange)
            {
                if (Cache.Instance.PriorityTargets.All(pt => pt.Id != target.Id))
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Adding [" + target.Name + "][ID: " + target.Id + "] as a priority target", Logging.Teal);
                    Cache.Instance.AddPriorityTargets(new[] { target }, Priority.PriorityKillTarget);
                }

                if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets > 0)
                {
                    if (!(target.IsTarget || target.IsTargeting))

                    //This target is not targeted and need to target it
                    {
                        Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Targeting [" + target.Name + "][ID: " + target.Id + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                        target.LockTarget();

                        // the target has been added to the priority targets list and has been targeted.
                        // this should ensure that the combat module (and/or the next action) kills the target.
                        Nextaction();
                        return;
                    }
                }
            }
            NavigateOnGrid.NavigateIntoRange(target, "CombatMissionCtrl." + _pocketActions[_currentAction]);
        }

        private void DropItemAction(Actions.Action action)
        {
            Cache.Instance.DropMode = true;
            var items = action.GetParameterValues("item");
            var target = action.GetParameterValue("target");

            int quantity;
            if (!int.TryParse(action.GetParameterValue("quantity"), out quantity))
                quantity = 1;

            var done = items.Count == 0;

            IEnumerable<EntityCache> targets = Cache.Instance.EntitiesByName(target).ToList();
            if (!targets.Any())
            {
                Logging.Log("MissionController.DropItem", "No target name: " + targets, Logging.Orange);

                // now that we have completed this action revert OpenWrecks to false
                Cache.Instance.DropMode = false;
                Nextaction();
                return;
            }

            var closest = targets.OrderBy(t => t.Distance).First();
            if (closest.Distance > (int)Distance.SafeScoopRange)
            {
                if (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)
                {
                    if (DateTime.UtcNow > Cache.Instance.NextApproachAction)
                    {
                        Logging.Log("MissionController.DropItem", "Approaching target [" + closest.Name + "][ID: " + Cache.Instance.MaskedID(closest.Id) + "] which is at [" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.White);
                        closest.Approach(1000);
                    }
                }
            }
            else
            {
                if (!done)
                {
                    if (DateTime.UtcNow > Cache.Instance.NextOpenContainerInSpaceAction)
                    {
                        var cargo = Cache.Instance.DirectEve.GetShipsCargo();

                        if (closest.CargoWindow == null)
                        {
                            Logging.Log("MissionController.DropItem", "Open Cargo", Logging.White);
                            closest.OpenCargo();
                            Cache.Instance.NextOpenContainerInSpaceAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(4, 6));
                            return;
                        }

                        // Get the container that is associated with the cargo container
                        var container = Cache.Instance.DirectEve.GetContainer(closest.Id);

                        var itemsToMove = cargo.Items.FirstOrDefault(i => i.TypeName.ToLower() == items.FirstOrDefault().ToLower());
                        if (itemsToMove != null)
                        {
                            Logging.Log("MissionController.DropItem", "Moving Items: " + items.FirstOrDefault() + " from cargo ship to " + container.TypeName, Logging.White);
                            container.Add(itemsToMove, quantity);

                            done = container.Items.Any(i => i.TypeName.ToLower() == items.FirstOrDefault().ToLower() && (i.Quantity >= quantity));
                            Cache.Instance.NextOpenContainerInSpaceAction = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(4, 6));
                        }
                        else
                        {
                            Logging.Log("MissionController.DropItem", "Error not found Items", Logging.White);
                            Cache.Instance.DropMode = false;
                            Nextaction();
                            return;
                        }
                    }
                }
                else
                {
                    Logging.Log("MissionController.DropItem", "We are done", Logging.White);

                    // now that we've completed this action revert OpenWrecks to false
                    Cache.Instance.DropMode = false;
                    Nextaction();
                    return;
                }
            }
        }

        private void LootItemAction(Actions.Action action)
        {
            try
            {
                Cache.Instance.MissionLoot = true;
                List<string> items = action.GetParameterValues("item");
                List<string> targetNames = action.GetParameterValues("target");

                // if we are not generally looting we need to re-enable the opening of wrecks to
                // find this LootItems we are looking for
                Cache.Instance.OpenWrecks = true;

                int quantity;
                if (!int.TryParse(action.GetParameterValue("quantity"), out quantity))
                {
                    quantity = 1;
                }

                bool done = items.Count == 0;
                if (!done)
                {
                    if (!Cache.Instance.OpenCargoHold("CombatMissionCtrl.LootItemAction")) return;
                    if (Cache.Instance.CargoHold.Window.IsReady)
                    {
                        if (Cache.Instance.CargoHold.Items.Any(i => (items.Contains(i.TypeName) && (i.Quantity >= quantity))))
                        {
                            done = true;
                        }
                    }
                }

                if (done)
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "We are done looting - we have the item(s)", Logging.Teal);

                    // now that we have completed this action revert OpenWrecks to false
                    Cache.Instance.OpenWrecks = false;
                    Cache.Instance.MissionLoot = false;
                    _currentAction++;
                    return;
                }

                IOrderedEnumerable<EntityCache> containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Distance);

                //IOrderedEnumerable<EntityCache> containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Id);
                //IOrderedEnumerable<EntityCache> containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderByDescending(e => e.Id);
                if (!containers.Any())
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "We are done looting - no containers left to loot", Logging.Teal);

                    _currentAction++;
                    return;
                }

                EntityCache container = containers.FirstOrDefault(c => targetNames.Contains(c.Name)) ?? containers.FirstOrDefault();
                if (container != null && (container.Distance > (int)Distance.SafeScoopRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != container.Id)))
                {
                    if (DateTime.UtcNow > Cache.Instance.NextApproachAction && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != container.Id))
                    {
                        Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Approaching target [" + container.Name + "][ID: " + Cache.Instance.MaskedID(container.Id) + "] which is at [" + Math.Round(container.Distance / 1000, 0) + "k away]", Logging.Teal);
                        container.Approach();
                    }
                }
            }
            catch (Exception exception)
            {
                Logging.Log("CombatMissionCtrl.LootItemAction","Exception logged was [" + exception +  "]",Logging.Teal);    
            }
        }

        //
        // this action still needs some TLC - currently broken (unimplemented)
        //
/*
        private void SalvageAction(Actions.Action action)
        {
            Cache.Instance.MissionLoot = true;
            List<string> items = action.GetParameterValues("item");
            List<string> targetNames = action.GetParameterValues("target");
            // if we are not generally looting we need to re-enable the opening of wrecks to
            // find this LootItems we are looking for
            Cache.Instance.OpenWrecks = true;

            //
            // when the salvage action is 'done' we will be able to open the "target"
            //
            bool done = items.Count == 0;
            if (!done)
            {
                DirectContainer cargo = Cache.Instance.DirectEve.GetShipsCargo();
                // We assume that the ship's cargo will be opened somewhere else
                if (cargo.Window.IsReady)
                    done |= cargo.Items.Any(i => (items.Contains(i.TypeName)));
            }
            if (done)
            {
                Logging.Log("CombatMission." + _pocketActions[_currentAction], "We are done looting", Logging.Teal);
                // now that we have completed this action revert OpenWrecks to false
                Cache.Instance.OpenWrecks = false;
                Cache.Instance.MissionLoot = false;
                _currentAction++;
                return;
            }

            IOrderedEnumerable<EntityCache> containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderBy(e => e.Distance);
            if (!containers.Any())
            {
                Logging.Log("CombatMission." + _pocketActions[_currentAction], "We are done looting", Logging.Teal);

                _currentAction++;
                return;
            }

            EntityCache closest = containers.LastOrDefault(c => targetNames.Contains(c.Name)) ?? containers.LastOrDefault();
            if (closest != null && (closest.Distance > (int)Distance.SafeScoopRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id)))
            {
                if (DateTime.UtcNow > Cache.Instance.NextApproachAction && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != closest.Id))
                {
                    Logging.Log("CombatMission." + _pocketActions[_currentAction], "Approaching target [" + closest.Name + "][ID: " + closest.Id + "] which is at [" + Math.Round(closest.Distance / 1000, 0) + "k away]", Logging.Teal);
                    closest.Approach();
                }
            }
        }
*/

        private void LootAction(Actions.Action action)
        {
            List<string> items = action.GetParameterValues("item");
            List<string> targetNames = action.GetParameterValues("target");

            // if we are not generally looting we need to re-enable the opening of wrecks to
            // find this LootItems we are looking for
            Cache.Instance.OpenWrecks = true;
            if (!Settings.Instance.LootEverything)
            {
                bool done = items.Count == 0;
                if (!done)
                {
                    DirectContainer cargo = Cache.Instance.DirectEve.GetShipsCargo();

                    // We assume that the ship's cargo will be opened somewhere else
                    if (cargo.Window.IsReady)
                        done |= cargo.Items.Any(i => items.Contains(i.TypeName));
                }
                if (done)
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "LootEverything:  We are done looting", Logging.Teal);

                    // now that we are done with this action revert OpenWrecks to false
                    Cache.Instance.OpenWrecks = false;

                    _currentAction++;
                    return;
                }
            }

            // unlock targets count
            Cache.Instance.MissionLoot = true;

            IOrderedEnumerable<EntityCache> containers = Cache.Instance.Containers.Where(e => !Cache.Instance.LootedContainers.Contains(e.Id)).OrderByDescending(e => e.Id);
            if (!containers.Any())
            {
                // lock targets count
                Cache.Instance.MissionLoot = false;
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "We are done looting", Logging.Teal);

                // now that we are done with this action revert OpenWrecks to false
                Cache.Instance.OpenWrecks = false;

                _currentAction++;
                return;
            }

            EntityCache container = containers.FirstOrDefault(c => targetNames.Contains(c.Name)) ?? containers.LastOrDefault();
            if (container != null && (container.Distance > (int)Distance.SafeScoopRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != container.Id)))
            {
                if (DateTime.UtcNow > Cache.Instance.NextApproachAction)
                {
                    Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Approaching target [" + container.Name + "][ID: " + Cache.Instance.MaskedID(container.Id) + "][" + Math.Round(container.Distance / 1000, 0) + "k away]", Logging.Teal);
                    container.Approach();
                }
            }
        }

        private void IgnoreAction(Actions.Action action)
        {
            bool clear;
            if (!bool.TryParse(action.GetParameterValue("clear"), out clear))
                clear = false;

            //List<string> removehighestbty = action.GetParameterValues("RemoveHighestBty");
            //List<string> addhighestbty = action.GetParameterValues("AddHighestBty");

            List<string> add = action.GetParameterValues("add");
            List<string> remove = action.GetParameterValues("remove");

            //string targetNames = action.GetParameterValue("target");

            //int distancetoapp;
            //if (!int.TryParse(action.GetParameterValue("distance"), out distancetoapp))
            //    distancetoapp = 1000;

            //IEnumerable<EntityCache> targets = Cache.Instance.Entities.Where(e => targetNames.Contains(e.Name));
            // EntityCache target = targets.OrderBy(t => t.Distance).First();

            //IEnumerable<EntityCache> targetsinrange = Cache.Instance.Entities.Where(b => Cache.Instance.DistanceFromEntity(b.X ?? 0, b.Y ?? 0, b.Z ?? 0,target) < distancetoapp);
            //IEnumerable<EntityCache> targetsoutofrange = Cache.Instance.Entities.Where(b => Cache.Instance.DistanceFromEntity(b.X ?? 0, b.Y ?? 0, b.Z ?? 0, target) < distancetoapp);

            if (clear)
                Cache.Instance.IgnoreTargets.Clear();
            else
            {
                add.ForEach(a => Cache.Instance.IgnoreTargets.Add(a.Trim()));
                remove.ForEach(a => Cache.Instance.IgnoreTargets.Remove(a.Trim()));
            }
            Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Updated ignore list", Logging.Teal);
            if (Cache.Instance.IgnoreTargets.Any())
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Currently ignoring: " + Cache.Instance.IgnoreTargets.Aggregate((current, next) => current + "[" + next + "]"), Logging.Teal);
            else
                Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction], "Your ignore list is empty", Logging.Teal);
            _currentAction++;
        }

        private void PerformAction(Actions.Action action)
        {
            switch (action.State)
            {
                case ActionState.LogWhatIsOnGrid:
                    LogWhatIsOnGridAction(action);
                    break;

                case ActionState.Activate:
                    ActivateAction(action);
                    break;

                case ActionState.ClearPocket:
                    ClearPocketAction(action);
                    break;

                case ActionState.ClearAggro:
                    ClearAggroAction(action);
                    break;

                case ActionState.SalvageBookmark:
                    BookmarkPocketForSalvaging();

                    _currentAction++;
                    break;

                case ActionState.Done:
                    DoneAction();
                    break;

                case ActionState.Kill:
                    KillAction(action);
                    break;

                case ActionState.KillOnce:
                    KillOnceAction(action);
                    break;

                case ActionState.UseDrones:
                    UseDrones(action);
                    break;

                case ActionState.AggroOnly:
                    AggroOnlyAction(action);
                    break;

                case ActionState.KillClosestByName:
                    KillClosestByNameAction(action);
                    break;

                case ActionState.KillClosest:
                    KillClosestAction(action);
                    break;

                case ActionState.MoveTo:
                    MoveToAction(action);
                    break;

                case ActionState.OrbitEntity:
                    OrbitEntityAction(action);
                    break;

                case ActionState.MoveToBackground:
                    MoveToBackgroundAction(action);
                    break;

                case ActionState.ClearWithinWeaponsRangeOnly:
                    ClearWithinWeaponsRangeOnlyAction(action);
                    break;

                case ActionState.ClearWithinWeaponsRangewAggroOnly:
                    ClearWithinWeaponsRangewAggroOnlyAction(action);
                    break;

                //case ActionState.Salvage:
                //    SalvageAction(action);
                //    break;

                //case ActionState.Analyze:
                //    AnalyzeAction(action);
                //    break;

                case ActionState.Loot:
                    LootAction(action);
                    break;

                case ActionState.LootItem:
                    LootItemAction(action);
                    break;

                //case ActionState.PutItem:
                //    PutItemAction(action);
                //    break;

                case ActionState.DropItem:
                    DropItemAction(action);
                    break;

                case ActionState.Ignore:
                    IgnoreAction(action);
                    break;

                case ActionState.WaitUntilTargeted:
                    WaitUntilTargeted(action);
                    break;

                case ActionState.DebuggingWait:
                    DebuggingWait(action);
                    break;
            }
        }

        public void ProcessState()
        {
            // There is really no combat in stations (yet)
            if (Cache.Instance.InStation)
                return;

            // if we are not in space yet, wait...
            if (!Cache.Instance.InSpace)
                return;

            // What? No ship entity?
            if (Cache.Instance.DirectEve.ActiveShip.Entity == null)
                return;

            // There is no combat when cloaked
            if (Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked)
                return;

            switch (_States.CurrentCombatMissionCtrlState)
            {
                case CombatMissionCtrlState.Idle:
                    break;

                case CombatMissionCtrlState.Done:
                    Statistics.WritePocketStatistics();

                    if (!Cache.Instance.NormalApproach)
                        Cache.Instance.NormalApproach = true;

                    Cache.Instance.IgnoreTargets.Clear();
                    break;

                case CombatMissionCtrlState.Error:
                    break;

                case CombatMissionCtrlState.Start:
                    Cache.Instance.PocketNumber = 0;

                    // Update statistic values
                    Cache.Instance.WealthatStartofPocket = Cache.Instance.DirectEve.Me.Wealth;
                    Statistics.Instance.StartedPocket = DateTime.UtcNow;

                    // Reload the items needed for this mission from the XML file
                    Cache.Instance.RefreshMissionItems(AgentId);

                    // Update x/y/z so that NextPocket wont think we are there yet because its checking (very) old x/y/z cords
                    _lastX = Cache.Instance.DirectEve.ActiveShip.Entity.X;
                    _lastY = Cache.Instance.DirectEve.ActiveShip.Entity.Y;
                    _lastZ = Cache.Instance.DirectEve.ActiveShip.Entity.Z;

                    _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.LoadPocket;
                    break;

                case CombatMissionCtrlState.LoadPocket:
                    _pocketActions.Clear();
                    _pocketActions.AddRange(Cache.Instance.LoadMissionActions(AgentId, Cache.Instance.PocketNumber, true));

                    //
                    // LogStatistics();
                    //
                    if (_pocketActions.Count == 0)
                    {
                        // No Pocket action, load default actions
                        Logging.Log("CombatMissionCtrl", "No mission actions specified, loading default actions", Logging.Orange);

                        // Wait for 30 seconds to be targeted
                        _pocketActions.Add(new Actions.Action { State = ActionState.WaitUntilTargeted });
                        _pocketActions[0].AddParameter("timeout", "15");

                        // Clear the Pocket
                        _pocketActions.Add(new Actions.Action { State = ActionState.ClearPocket });

                        // Is there a gate?
                        if (Cache.Instance.AccelerationGates != null && Cache.Instance.AccelerationGates.Any())
                        {
                            // Activate it (Activate action also moves to the gate)
                            _pocketActions.Add(new Actions.Action { State = ActionState.Activate });
                            _pocketActions[_pocketActions.Count - 1].AddParameter("target", "Acceleration Gate");
                        }
                        else // No, were done
                            _pocketActions.Add(new Actions.Action { State = ActionState.Done });
                    }

                    Logging.Log("-", "-----------------------------------------------------------------", Logging.Teal);
                    Logging.Log("-", "-----------------------------------------------------------------", Logging.Teal);
                    Logging.Log("CombatMissionCtrl", "Mission Timer Currently At: [" + Math.Round(DateTime.UtcNow.Subtract(Statistics.Instance.StartedMission).TotalMinutes, 0) + "] min", Logging.Teal);

                    //if (Cache.Instance.OptimalRange != 0)
                    //    Logging.Log("Optimal Range is set to: " + (Cache.Instance.OrbitDistance / 1000).ToString(CultureInfo.InvariantCulture) + "k");
                    Logging.Log("CombatMissionCtrl", "Max Range is currently: " + (Cache.Instance.MaxRange / 1000).ToString(CultureInfo.InvariantCulture) + "k", Logging.Teal);
                    Logging.Log("-", "-----------------------------------------------------------------", Logging.Teal);
                    Logging.Log("-", "-----------------------------------------------------------------", Logging.Teal);
                    Logging.Log("CombatMissionCtrl", "Pocket [" + Cache.Instance.PocketNumber + "] loaded, executing the following actions", Logging.Orange);
                    var pocketActionCount = 1;
                    foreach (Actions.Action a in _pocketActions)
                    {
                        Logging.Log("CombatMissionCtrl", "Action [ " + pocketActionCount + " ] " + a, Logging.Teal);
                        pocketActionCount++;
                    }
                    Logging.Log("-", "-----------------------------------------------------------------", Logging.Teal);
                    Logging.Log("-", "-----------------------------------------------------------------", Logging.Teal);

                    // Reset pocket information
                    _currentAction = 0;
                    Cache.Instance.IsMissionPocketDone = false;
                    Cache.Instance.IgnoreTargets.Clear();
                    Statistics.PocketObjectStatistics(Cache.Instance.Objects.ToList());
                    _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.ExecutePocketActions;
                    break;

                case CombatMissionCtrlState.ExecutePocketActions:
                    if (_currentAction >= _pocketActions.Count)
                    {
                        // No more actions, but we're not done?!?!?!
                        Logging.Log("CombatMissionCtrl", "We're out of actions but did not process a 'Done' or 'Activate' action", Logging.Red);

                        _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.Error;
                        break;
                    }

                    Actions.Action action = _pocketActions[_currentAction];
                    if (action.ToString() != Cache.Instance.CurrentPocketAction)
                    {
                        Cache.Instance.CurrentPocketAction = action.ToString();
                    }
                    int currentAction = _currentAction;
                    PerformAction(action);

                    if (currentAction != _currentAction)
                    {
                        Logging.Log("CombatMissionCtrl", "Finished Action." + action, Logging.Yellow);

                        if (_currentAction < _pocketActions.Count)
                        {
                            action = _pocketActions[_currentAction];
                            Logging.Log("CombatMissionCtrl", "Starting Action." + action, Logging.Yellow);
                        }
                    }

                    if (Settings.Instance.DebugStates)
                        Logging.Log("CombatMissionCtrl", "Action.State = " + action, Logging.Teal);
                    break;

                case CombatMissionCtrlState.NextPocket:
                    double distance = Cache.Instance.DistanceFromMe(_lastX, _lastY, _lastZ);
                    if (distance > (int)Distance.NextPocketDistance)
                    {
                        Logging.Log("CombatMissionCtrl", "We have moved to the next Pocket [" + Math.Round(distance / 1000, 0) + "k away]", Logging.Green);

                        // If we moved more then 100km, assume next Pocket
                        Cache.Instance.PocketNumber++;
                        _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.LoadPocket;
                        Statistics.WritePocketStatistics();
                    }
                    else if (DateTime.UtcNow.Subtract(_moveToNextPocket).TotalMinutes > 2)
                    {
                        Logging.Log("CombatMissionCtrl", "We have timed out, retry last action", Logging.Orange);

                        // We have reached a timeout, revert to ExecutePocketActions (e.g. most likely Activate)
                        _States.CurrentCombatMissionCtrlState = CombatMissionCtrlState.ExecutePocketActions;
                    }
                    break;
            }

            double newX = Cache.Instance.DirectEve.ActiveShip.Entity.X;
            double newY = Cache.Instance.DirectEve.ActiveShip.Entity.Y;
            double newZ = Cache.Instance.DirectEve.ActiveShip.Entity.Z;

            // For some reason x/y/z returned 0 sometimes
            if (newX != 0 && newY != 0 && newZ != 0)
            {
                // Save X/Y/Z so that NextPocket can check if we actually went to the next Pocket :)
                _lastX = newX;
                _lastY = newY;
                _lastZ = newZ;
            }
        }
    }
}