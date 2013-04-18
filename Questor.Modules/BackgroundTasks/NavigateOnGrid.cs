
namespace Questor.Modules.BackgroundTasks
{
    using System;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using System.Linq;
    using DirectEve;

    public class NavigateOnGrid
    {
        public static DateTime AvoidBumpingThingsTimeStamp = Cache.Instance.StartTime;
        public static int SafeDistanceFromStructureMultiplier = 1;
        public static bool AvoidBumpingThingsWarningSent = false;

        public static void AvoidBumpingThings(EntityCache thisBigObject, string module)
        {
            if (Settings.Instance.AvoidBumpingThings)
            {
                //if It has not been at least 60 seconds since we last session changed do not do anything
                if (Cache.Instance.InStation || !Cache.Instance.InSpace || Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || (Cache.Instance.InSpace && Cache.Instance.LastSessionChange.AddSeconds(60) < DateTime.UtcNow))
                    return;
                //
                // if we are "too close" to the bigObject move away... (is orbit the best thing to do here?)
                //
                if (Cache.Instance.ClosestStargate.Distance > 9000 || Cache.Instance.ClosestStation.Distance > 5000)
                {
                    //EntityCache thisBigObject = Cache.Instance.BigObjects.FirstOrDefault();
                    if (thisBigObject != null)
                    {
                        if (thisBigObject.Distance >= (int)Distance.TooCloseToStructure)
                        {
                            //we are no longer "too close" and can proceed.
                            AvoidBumpingThingsTimeStamp = DateTime.UtcNow;
                            SafeDistanceFromStructureMultiplier = 1;
                            AvoidBumpingThingsWarningSent = false;
                        }
                        else
                        {
                            if (DateTime.UtcNow > Cache.Instance.NextOrbit)
                            {
                                if (DateTime.UtcNow > AvoidBumpingThingsTimeStamp.AddSeconds(30))
                                {
                                    if (SafeDistanceFromStructureMultiplier <= 4)
                                    {
                                        //
                                        // for simplicities sake we reset this timestamp every 30 sec until the multiplier hits 5 then it should stay static until we are not "too close" anymore
                                        //
                                        AvoidBumpingThingsTimeStamp = DateTime.UtcNow;
                                        SafeDistanceFromStructureMultiplier++;
                                    }

                                    if (DateTime.UtcNow > AvoidBumpingThingsTimeStamp.AddMinutes(5) && !AvoidBumpingThingsWarningSent)
                                    {
                                        Logging.Log("NavigateOnGrid", "We are stuck on a object and have been trying to orbit away from it for over 5 min", Logging.Orange);
                                        AvoidBumpingThingsWarningSent = true;
                                    }

                                    if (DateTime.UtcNow > AvoidBumpingThingsTimeStamp.AddMinutes(15))
                                    {
                                        Cache.Instance.CloseQuestorCMDLogoff = false;
                                        Cache.Instance.CloseQuestorCMDExitGame = true;
                                        Cache.Instance.ReasonToStopQuestor = "navigateOnGrid: We have been stuck on an object for over 15 min";
                                        Logging.Log("ReasonToStopQuestor", Cache.Instance.ReasonToStopQuestor, Logging.Yellow);
                                        Cache.Instance.SessionState = "Quitting";
                                    }
                                }
                                thisBigObject.Orbit((int)Distance.SafeDistancefromStructure * SafeDistanceFromStructureMultiplier);
                                Logging.Log(module, ": initiating Orbit of [" + thisBigObject.Name + "] orbiting at [" + ((int)Distance.SafeDistancefromStructure * SafeDistanceFromStructureMultiplier) + "]", Logging.White);
                            }
                            return;
                            //we are still too close, do not continue through the rest until we are not "too close" anymore
                        }
                    }
                }
            }
        }

        public static void OrbitGateorTarget(EntityCache target, string module)
        {
            if (DateTime.UtcNow > Cache.Instance.NextOrbit)
            {
                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "OrbitGateorTarget Started", Logging.White);
                if (Cache.Instance.OrbitDistance == 0)
                {
                    Cache.Instance.OrbitDistance = 2000;
                }

                if (target.Distance + Cache.Instance.OrbitDistance < Cache.Instance.MaxRange - 5000)
                {
                    if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "if (target.Distance + Cache.Instance.OrbitDistance < Cache.Instance.MaxRange - 5000)", Logging.White);
                    //Logging.Log("CombatMissionCtrl." + _pocketActions[_currentAction] ,"StartOrbiting: Target in range");
                    if (!Cache.Instance.IsApproachingOrOrbiting)
                    {
                        if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("CombatMissionCtrl.NavigateIntoRange", "We are not approaching nor orbiting", Logging.Teal);

                        //
                        // Prefer to orbit the last structure defined in 
                        // Cache.Instance.OrbitEntityNamed
                        //
                        EntityCache structure = null;
                        if (!string.IsNullOrEmpty(Cache.Instance.OrbitEntityNamed))
                        {
                            structure = Cache.Instance.Entities.Where(i => i.Name.Contains(Cache.Instance.OrbitEntityNamed)).OrderBy(t => t.Distance).FirstOrDefault();
                        }

                        if (structure == null)
                        {
                            structure = Cache.Instance.Entities.Where(i => i.Name.Contains("Gate")).OrderBy(t => t.Distance).FirstOrDefault();    
                        }
                        
                        if (Settings.Instance.OrbitStructure && structure != null)
                        {
                            structure.Orbit(Cache.Instance.OrbitDistance);
                            Logging.Log(module, "Initiating Orbit [" + structure.Name + "][at " + Math.Round((double)Cache.Instance.OrbitDistance/1000, 0) + "k][ID: " + Cache.Instance.MaskedID(structure.Id) + "]", Logging.Teal);
                            return;
                        }

                        //
                        // OrbitStructure is false
                        //
                        if (Settings.Instance.SpeedTank)
                        {
                            target.Orbit(Cache.Instance.OrbitDistance);
                            Logging.Log(module, "Initiating Orbit [" + target.Name + "][at " + Math.Round((double)Cache.Instance.OrbitDistance/1000,0) + "/][ID: " + Cache.Instance.MaskedID(target.Id) + "]", Logging.Teal);
                            return;
                        }

                        //
                        // OrbitStructure is false
                        // Speedtank is false
                        //
                        if (Cache.Instance.MyShipEntity.Velocity < 300) //this will spam a bit until we know what "mode" our activeship is when aligning
                        {
                            if (Settings.Instance.WeaponGroupId == (int)Group.HybridWeapon ||
                                Settings.Instance.WeaponGroupId == (int)Group.ProjectileWeapon)
                            {
                                if (DateTime.UtcNow > Cache.Instance.NextAlign)
                                {
                                    Cache.Instance.Star.AlignTo();
                                    Logging.Log(module, "Aligning to the Star so we might possibly hit [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][ActiveShip.Entity.Mode:[" + Cache.Instance.DirectEve.ActiveShip.Entity.Mode + "]", Logging.Teal);
                                    return;
                                }
                            }
                        }
                    }
                }
                else
                {
                    Logging.Log(module, "Out of range. ignoring orbit around structure.", Logging.Teal);
                    target.Orbit(Cache.Instance.OrbitDistance);
                    Logging.Log(module, "Initiating Orbit [" + target.Name + "][at " + Math.Round((double)Cache.Instance.OrbitDistance / 1000, 0) + "k][ID: " + Cache.Instance.MaskedID(target.Id) + "]", Logging.Teal);
                    Cache.Instance.NextOrbit = DateTime.UtcNow.AddSeconds(90);
                    return;
                }
                return;
            }
        }

        public static void NavigateIntoRange(EntityCache target, string module)
        {
            if (Cache.Instance.InWarp || Cache.Instance.InStation)
                return;

            if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange Started", Logging.White);

            if (Cache.Instance.OrbitDistance != Settings.Instance.OrbitDistance)
            {
                if (Cache.Instance.OrbitDistance == 0)
                {
                    Cache.Instance.OrbitDistance = Settings.Instance.OrbitDistance;
                    Logging.Log("CombatMissionCtrl", "Using default orbit distance: " + Cache.Instance.OrbitDistance + " (as the custom one was 0)", Logging.Teal);
                }

                //else
                //    Logging.Log("CombatMissionCtrl", "Using custom orbit distance: " + Cache.Instance.OrbitDistance, Logging.teal);
            }

            //if (Cache.Instance.OrbitDistance != 0)
            //    Logging.Log("CombatMissionCtrl", "Orbit Distance is set to: " + (Cache.Instance.OrbitDistance / 1000).ToString(CultureInfo.InvariantCulture) + "k", Logging.teal);

            NavigateOnGrid.AvoidBumpingThings(Cache.Instance.BigObjectsandGates.FirstOrDefault(), "NavigateOnGrid: NavigateIntoRange");

            if (Settings.Instance.SpeedTank)
            {
                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: speedtank: orbitdistance is [" + Cache.Instance.OrbitDistance + "]", Logging.White);
                OrbitGateorTarget(target, module);
                return;
            }
            else //if we are not speed tanking then check optimalrange setting, if that is not set use the less of targeting range and weapons range to dictate engagement range
            {
                if (DateTime.UtcNow > Cache.Instance.NextApproachAction)
                {
                    //if optimalrange is set - use it to determine engagement range
                    if (Cache.Instance.OptimalRange != 0)
                    {
                        if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: OptimalRange [ " + Cache.Instance.OptimalRange + "] Current Distance to [" + target.Name + "] is [" + Math.Round(target.Distance / 1000, 0) + "]", Logging.White);

                        if (target.Distance > Cache.Instance.OptimalRange + (int)Distance.OptimalRangeCushion && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
                            if (target.IsNPCFrigate)
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + Math.Round(target.Distance / 1000, 0) + "]", Logging.White);
                                OrbitGateorTarget(target, module);
                                return;
                            }

                            target.Approach(Cache.Instance.OptimalRange);
                            Logging.Log(module, "Using Optimal Range: Approaching target [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            return;
                        }

                        if (target.Distance <= Cache.Instance.OptimalRange)
                        {
                            if ((target.IsNPCFrigate) && (Cache.Instance.Approaching == null || Cache.Instance.MyShipEntity.Velocity < 100))
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.White);
                                OrbitGateorTarget(target, module);
                                return;
                            }

                            if (Cache.Instance.Approaching != null && Cache.Instance.MyShipEntity.Velocity != 0)
                            {
                                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                                Cache.Instance.Approaching = null;
                                Logging.Log(module, "Using Optimal Range: Stop ship, target at [" + Math.Round(target.Distance / 1000, 0) + "k away] is inside optimal", Logging.Teal);
                                return;
                            }
                        }
                    }
                    else if (!Cache.Instance.InMission && Cache.Instance.OptimalRange != Settings.Instance.OptimalRange)
                    {
                        Cache.Instance.OptimalRange = Settings.Instance.OptimalRange;
                        return;
                    }
                    else //if optimalrange is not set use MaxRange (shorter of weapons range and targeting range)
                    {
                        if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: using MaxRange [" + Cache.Instance.MaxRange + "] target is [" + target.Name + "][" + target.Distance + "]", Logging.White);

                        if (target.Distance > Cache.Instance.MaxRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                        {
                            if (target.IsNPCFrigate)
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.White);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                            target.Approach((int)(Cache.Instance.WeaponRange * 0.8d));
                            Logging.Log(module, "Using Weapons Range * 0.8d [" + Math.Round(Cache.Instance.WeaponRange * 0.8d / 1000, 0) + " k]: Approaching target [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                            return;
                        }

                        //I think when approach distance will be reached ship will be stopped so this is not needed
                        if (target.Distance <= Cache.Instance.MaxRange - 5000 && Cache.Instance.Approaching != null)
                        {
                            if (target.IsNPCFrigate)
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.White);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                            if (Cache.Instance.MyShipEntity.Velocity != 0) Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdStopShip);
                            Cache.Instance.Approaching = null;
                            Logging.Log(module, "Using Weapons Range: Stop ship, target is more than 5k inside weapons range", Logging.Teal);
                            return;
                        }

                        if (target.Distance <= Cache.Instance.MaxRange && Cache.Instance.Approaching == null)
                        {
                            if (target.IsNPCFrigate)
                            {
                                if (Settings.Instance.DebugNavigateOnGrid) Logging.Log("NavigateOnGrid", "NavigateIntoRange: target is NPC Frigate [" + target.Name + "][" + target.Distance + "]", Logging.White);
                                OrbitGateorTarget(target, module);
                                return;
                            }
                        }
                    }
                    return;
                }
            }
        }

        public static void NavigateToObject(EntityCache target, string module)  //this needs to accept a distance parameter....
        {
            if (Settings.Instance.SpeedTank)
            {   //this should be only executed when no specific actions
                if (DateTime.UtcNow > Cache.Instance.NextOrbit)
                {
                    if (target.Distance + Cache.Instance.OrbitDistance < Cache.Instance.MaxRange)
                    {
                        Logging.Log(module, "StartOrbiting: Target in range", Logging.Teal);
                        if (!Cache.Instance.IsApproachingOrOrbiting)
                        {
                            Logging.Log("CombatMissionCtrl.NavigateToObject", "We are not approaching nor orbiting", Logging.Teal);
                            bool orbitStructure = Settings.Instance.OrbitStructure;
                            var structure = Cache.Instance.Entities.Where(i => i.GroupId == (int)Group.LargeColidableStructure || i.Name.Contains("Gate") || i.Name.Contains("Beacon")).OrderBy(t => t.Distance).ThenBy(t => t.Distance).FirstOrDefault();

                            if (orbitStructure && structure != null)
                            {
                                structure.Orbit(Cache.Instance.OrbitDistance);
                                Logging.Log(module, "Initiating Orbit [" + structure.Name + "][ID: " + Cache.Instance.MaskedID(structure.Id) + "]", Logging.Teal);
                            }
                            else
                            {
                                target.Orbit(Cache.Instance.OrbitDistance);
                                Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "]", Logging.Teal);
                            }
                            return;
                        }
                    }
                    else
                    {
                        Logging.Log(module, "Possible out of range. ignoring orbit around structure", Logging.Teal);
                        target.Orbit(Cache.Instance.OrbitDistance);
                        Logging.Log(module, "Initiating Orbit [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "]", Logging.Teal);
                        return;
                    }
                }
            }
            else //if we are not speed tanking then check optimalrange setting, if that is not set use the less of targeting range and weapons range to dictate engagement range
            {
                if (DateTime.UtcNow > Cache.Instance.NextApproachAction)
                {
                    //if optimalrange is set - use it to determine engagement range
                    //
                    // this assumes that both optimal range and missile boats both want to be within 5k of the object they asked us to navigate to
                    //
                    if (target.Distance > Cache.Instance.MaxRange && (Cache.Instance.Approaching == null || Cache.Instance.Approaching.Id != target.Id))
                    {
                        target.Approach((int)(Distance.SafeDistancefromStructure));
                        Logging.Log(module, "Using SafeDistanceFromStructure: Approaching target [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                    }
                    return;
                }
            }
        }
    }
}