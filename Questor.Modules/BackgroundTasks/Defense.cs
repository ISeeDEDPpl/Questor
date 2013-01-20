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
    using System.Collections.Generic;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.Logging;
    using DirectEve;

    public class Defense
    {
        private DateTime _lastSessionChange = Cache.Instance.StartTime;

        private DateTime _lastPulse = DateTime.UtcNow;
        private int _trackingLinkScriptAttempts;
        private int _sensorBoosterScriptAttempts;
        private int _sensorDampenerScriptAttempts;
        private int _trackingComputerScriptAttempts;
        private int _trackingDisruptorScriptAttempts;
        private int _ancillaryShieldBoosterAttempts;
        private int _capacitorInjectorAttempts;

        private int ModuleNumber { get; set; }

        private static readonly Dictionary<long, DateTime> NextScriptReload = new Dictionary<long, DateTime>();

        private bool LoadthisScript(DirectItem scriptToLoad, ModuleCache module)
        {
            if (scriptToLoad != null)
            {
                if (module.IsReloadingAmmo || module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline)
                    return false;

                // We have enough ammo loaded
                if (module.Charge != null && module.Charge.TypeId == scriptToLoad.TypeId && module.CurrentCharges == module.MaxCharges)
                {
                    Logging.Log("LoadthisScript", "module is already loaded with the script we wanted", Logging.Teal);
                    NextScriptReload[module.ItemId] = DateTime.UtcNow.AddSeconds(15); //mark this weapon as reloaded... by the time we need to reload this timer will have aged enough...
                    return false;
                }

                // We are reloading, wait 15
                if (NextScriptReload.ContainsKey(module.ItemId) && DateTime.UtcNow < NextScriptReload[module.ItemId].AddSeconds(15))
                {
                    Logging.Log("LoadthisScript", "module was reloaded recently... skipping", Logging.Teal);
                    return false;
                }
                NextScriptReload[module.ItemId] = DateTime.UtcNow.AddSeconds(15);

                // Reload or change ammo
                if (module.Charge != null && module.Charge.TypeId == scriptToLoad.TypeId)
                {
                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastLoggingAction).TotalSeconds > 10)
                    {
                        Cache.Instance.LastLoggingAction = DateTime.UtcNow;
                    }
                    Logging.Log("Defense", "Reloading [" + module.TypeId + "] with [" + scriptToLoad.TypeName + "][TypeID: " + scriptToLoad.TypeId + "]", Logging.Teal);
                    module.ReloadAmmo(scriptToLoad);
                    return true;
                }
                
                if (DateTime.UtcNow.Subtract(Cache.Instance.LastLoggingAction).TotalSeconds > 10)
                {
                    Cache.Instance.LastLoggingAction = DateTime.UtcNow;
                }
                Logging.Log("Defense", "Changing [" + module.TypeId + "] with [" + scriptToLoad.TypeName + "][TypeID: " + scriptToLoad.TypeId + "]", Logging.Teal);
                module.ChangeAmmo(scriptToLoad);
                return true;
            }
            Logging.Log("LoadthisScript", "script to load was NULL!", Logging.Teal);
            return false;
        }

        private void ActivateOnce()
        {
            //if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "spam", Logging.White);
            if (DateTime.UtcNow < Cache.Instance.NextActivateSupportModules) //if we just did something wait a fraction of a second
                return;

            ModuleNumber = 0;
            foreach (ModuleCache module in Cache.Instance.Modules)
            {
                if (!module.IsActivatable)
                    continue;

                //if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "Found Activatable Module [typeid: " + module.TypeId + "][groupID: " + module.GroupId +  "]", Logging.White);

                if (module.GroupId == (int)Group.TrackingDisruptor ||
                    module.GroupId == (int)Group.TrackingComputer ||
                    module.GroupId == (int)Group.TrackingLink ||
                    module.GroupId == (int)Group.SensorBooster ||
                    module.GroupId == (int)Group.SensorDampener ||
                    module.GroupId == (int)Group.CapacitorInjector ||
                    module.GroupId == (int)Group.AncillaryShieldBooster)
                {
                    //if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "---Found mod that could take a script [typeid: " + module.TypeId + "][groupID: " + module.GroupId + "][module.CurrentCharges [" + module.CurrentCharges + "]", Logging.White);
                    if (module.CurrentCharges < module.MaxCharges)
                    {
                        if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "Found Activatable Module with no charge[typeID:" + module.TypeId + "]", Logging.White);
                        DirectItem scriptToLoad;

                        if (module.GroupId == (int)Group.TrackingDisruptor && _trackingDisruptorScriptAttempts < 5)
                        {
                            _trackingDisruptorScriptAttempts++;
                            if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "TrackingDisruptor Found", Logging.White);
                            scriptToLoad = Cache.Instance.CheckCargoForItem(Settings.Instance.TrackingDisruptorScript, 1);

                            // this needs a counter and an abort after 10 tries or so... or itll keep checking the cargo for a script that may not exist
                            // every second we are in space!
                            if (scriptToLoad != null)
                            {
                                if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "TrackingDisruptor Found", Logging.White);
                                if (module.IsActive)
                                {
                                    module.Click();
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddSeconds(2);
                                    return;
                                }

                                if (module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline)
                                {
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                                    ModuleNumber++;
                                    continue;
                                }

                                if (!LoadthisScript(scriptToLoad, module))
                                {
                                    ModuleNumber++;
                                    continue;
                                }
                                return;
                            }
                            ModuleNumber++;
                            continue;
                        }

                        if (module.GroupId == (int)Group.TrackingComputer && _trackingComputerScriptAttempts < 5)
                        {
                            _trackingComputerScriptAttempts++;
                            if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "TrackingComputer Found", Logging.White);
                            DirectItem TrackingComputerScript = Cache.Instance.CheckCargoForItem(Settings.Instance.TrackingComputerScript, 1);
                            
                            EntityCache EntityTrackingDisruptingMe = Cache.Instance.TargetedBy.FirstOrDefault(t => t.IsTrackingDisruptingMe);
                            if (EntityTrackingDisruptingMe != null || TrackingComputerScript == null)
                            {
                                TrackingComputerScript = Cache.Instance.CheckCargoForItem((int)TypeID.OptimalRangeScript, 1);
                            }

                            scriptToLoad = TrackingComputerScript;
                            if (scriptToLoad != null)
                            {
                                if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "Script Found for TrackingComputer", Logging.White);
                                if (module.IsActive)
                                {
                                    module.Click();
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddSeconds(2);
                                    return;
                                }

                                if (module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline)
                                {
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                                    ModuleNumber++;
                                    continue;
                                }

                                if (!LoadthisScript(scriptToLoad, module))
                                {
                                    ModuleNumber++;
                                    continue;
                                }
                                return;
                            }
                            ModuleNumber++;
                            continue;
                        }

                        if (module.GroupId == (int)Group.TrackingLink && _trackingLinkScriptAttempts < 5)
                        {
                            _trackingLinkScriptAttempts++;
                            if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "TrackingLink Found", Logging.White);
                            scriptToLoad = Cache.Instance.CheckCargoForItem(Settings.Instance.TrackingLinkScript, 1);
                            if (scriptToLoad != null)
                            {
                                if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "Script Found for TrackingLink", Logging.White);
                                if (module.IsActive)
                                {
                                    module.Click();
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddSeconds(2);
                                    return;
                                }

                                if (module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline)
                                {
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                                    ModuleNumber++;
                                    continue;
                                }

                                if (!LoadthisScript(scriptToLoad, module))
                                {
                                    ModuleNumber++;
                                    continue;
                                }
                                return;
                            }
                            ModuleNumber++;
                            continue;
                        }

                        if (module.GroupId == (int)Group.SensorBooster && _sensorBoosterScriptAttempts < 5)
                        {
                            _sensorBoosterScriptAttempts++;
                            if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "SensorBooster Found", Logging.White);
                            scriptToLoad = Cache.Instance.CheckCargoForItem(Settings.Instance.SensorBoosterScript, 1);
                            if (scriptToLoad != null)
                            {
                                if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "Script Found for SensorBooster", Logging.White);
                                if (module.IsActive)
                                {
                                    module.Click();
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddSeconds(2);
                                    return;
                                }

                                if (module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline)
                                {
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                                    ModuleNumber++;
                                    continue;
                                }

                                if (!LoadthisScript(scriptToLoad, module))
                                {
                                    ModuleNumber++;
                                    continue;
                                }
                                return;
                            }
                            ModuleNumber++;
                            continue;
                        }

                        if (module.GroupId == (int)Group.SensorDampener && _sensorDampenerScriptAttempts < 5)
                        {
                            _sensorDampenerScriptAttempts++;
                            if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "SensorDampener Found", Logging.White);
                            scriptToLoad = Cache.Instance.CheckCargoForItem(Settings.Instance.SensorDampenerScript, 1);
                            if (scriptToLoad != null)
                            {
                                if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "Script Found for SensorDampener", Logging.White);
                                if (module.IsActive)
                                {
                                    module.Click();
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddSeconds(2);
                                    return;
                                }

                                if (module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline)
                                {
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                                    ModuleNumber++;
                                    continue;
                                }

                                if (!LoadthisScript(scriptToLoad, module))
                                {
                                    ModuleNumber++;
                                    continue;
                                }
                                return;
                            }
                            ModuleNumber++;
                            continue;
                        }

                        if (module.GroupId == (int)Group.AncillaryShieldBooster)
                        {
                            _ancillaryShieldBoosterAttempts++;
                            if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "ancillaryShieldBooster Found", Logging.White);
                            scriptToLoad = Cache.Instance.CheckCargoForItem(Settings.Instance.AncillaryShieldBoosterScript, 1);
                            if (scriptToLoad != null)
                            {
                                if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "CapBoosterCharges Found for ancillaryShieldBooster", Logging.White);
                                if (module.IsActive)
                                {
                                    module.Click();
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(500);
                                    return;
                                }

                                bool inCombat = Cache.Instance.TargetedBy.Any();
                                if (module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline || (inCombat && module.CurrentCharges > 0))
                                {
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                                    ModuleNumber++;
                                    continue;
                                }

                                if (!LoadthisScript(scriptToLoad, module))
                                {
                                    ModuleNumber++;
                                    continue;
                                }
                                return;
                            }
                            ModuleNumber++;
                            continue;
                        }

                        if (module.GroupId == (int)Group.CapacitorInjector)
                        {
                            _capacitorInjectorAttempts++;
                            if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "capacitorInjector Found", Logging.White);
                            scriptToLoad = Cache.Instance.CheckCargoForItem(Settings.Instance.CapacitorInjectorScript, 1);
                            if (scriptToLoad != null)
                            {
                                if (Settings.Instance.DebugLoadScripts) Logging.Log("Defense", "CapBoosterCharges Found for capacitorInjector", Logging.White);
                                if (module.IsActive)
                                {
                                    module.Click();
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(500);
                                    return;
                                }

                                bool inCombat = Cache.Instance.TargetedBy.Any();
                                if (module.IsActive || module.IsDeactivating || module.IsChangingAmmo || module.InLimboState || module.IsGoingOnline || !module.IsOnline || (inCombat && module.CurrentCharges > 0))
                                {
                                    Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                                    ModuleNumber++;
                                    continue;
                                }

                                if (!LoadthisScript(scriptToLoad, module))
                                {
                                    ModuleNumber++;
                                    continue;
                                }
                            }
                            ModuleNumber++;
                            continue;
                        }
                    }
                }
                ModuleNumber++;
                Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                continue;
            }

            ModuleNumber = 0;
            foreach (ModuleCache module in Cache.Instance.Modules)
            {
                if (!module.IsActivatable)
                    continue;

                bool activate = false;
                activate |= module.GroupId == (int)Group.CloakingDevice;
                activate |= module.GroupId == (int)Group.ShieldHardeners;
                activate |= module.GroupId == (int)Group.DamageControl;
                activate |= module.GroupId == (int)Group.ArmorHardeners;
                activate |= module.GroupId == (int)Group.SensorBooster;
                activate |= module.GroupId == (int)Group.TrackingComputer;
                activate |= module.GroupId == (int)Group.ECCM;

                if (!activate)
                    continue;

                ModuleNumber++;

                if (module.IsActive | module.InLimboState)
                    continue;

                if (module.GroupId == (int)Group.CloakingDevice)
                {
                    //Logging.Log("Defense: This module has a typeID of: " + module.TypeId + " !!");
                    if (module.TypeId != 11578)  //11578 Covert Ops Cloaking Device - if you don't have a covert ops cloak try the next module
                    {
                        continue;
                    }
                    EntityCache stuffThatMayDecloakMe = Cache.Instance.Entities.Where(t => t.Name != Cache.Instance.DirectEve.Me.Name || t.IsBadIdea || t.IsContainer || t.IsNpc || t.IsPlayer).OrderBy(t => t.Distance).FirstOrDefault();
                    if (stuffThatMayDecloakMe != null && (stuffThatMayDecloakMe.Distance <= (int)Distance.SafeToCloakDistance)) //if their is anything within 2300m do not attempt to cloak
                    {
                        if ((int)stuffThatMayDecloakMe.Distance != 0)
                        {
                            //Logging.Log(Defense: StuffThatMayDecloakMe.Name + " is very close at: " + StuffThatMayDecloakMe.Distance + " meters");
                            continue;
                        }
                    }
                }

                //
                // at this point the module should be active but is not: activate it, set the delay and return. The process will resume on the next tick
                //
                module.Click();
                Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                if (Settings.Instance.DebugDefense) Logging.Log("Defense", "Defensive module activated: [" + ModuleNumber + "]", Logging.White);
                continue;
            }
            ModuleNumber = 0;
        }

        private void ActivateRepairModules()
        {
            //var watch = new Stopwatch();
            if (DateTime.UtcNow < Cache.Instance.NextRepModuleAction) //if we just did something wait a fraction of a second
                return;

            ModuleNumber = 0;
            foreach (ModuleCache module in Cache.Instance.Modules)
            {
                if (module.InLimboState)
                    continue;

                double perc;
                double cap;
                if (module.GroupId == (int)Group.ShieldBoosters || 
                    module.GroupId == (int)Group.AncillaryShieldBooster || 
                    module.GroupId == (int)Group.CapacitorInjector)
                {
                    ModuleNumber++;
                    perc = Cache.Instance.DirectEve.ActiveShip.ShieldPercentage;
                    cap = Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage;
                }
                else if (module.GroupId == (int)Group.ArmorRepairer)
                {
                    ModuleNumber++;
                    perc = Cache.Instance.DirectEve.ActiveShip.ArmorPercentage;
                    cap = Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage;
                }
                else
                    continue;

                // Module is either for Cap or Tank recharging, so we look at these seperated (or random things will happen, like cap recharging when we need to repair but cap is near max) 
                // Cap recharging
                bool inCombat = Cache.Instance.TargetedBy.Any();
                if (!module.IsActive && inCombat && cap < Settings.Instance.InjectCapPerc && module.GroupId == (int)Group.CapacitorInjector && module.CurrentCharges > 0)
                {
                    module.Click();
                    perc = Cache.Instance.DirectEve.ActiveShip.ShieldPercentage;
                    Logging.Log("Defense", "Cap: [" + Math.Round(cap, 0) + "%] Capacitor Booster: [" + ModuleNumber + "] activated", Logging.White);
                }

                // Shield/Armor recharging
                else if (!module.IsActive && ((inCombat && perc < Settings.Instance.ActivateRepairModules) || (!inCombat && perc < Settings.Instance.DeactivateRepairModules && cap > Settings.Instance.SafeCapacitorPct)))
                {
                    if (Cache.Instance.DirectEve.ActiveShip.ShieldPercentage < Cache.Instance.LowestShieldPercentageThisPocket)
                    {
                        Cache.Instance.LowestShieldPercentageThisPocket = Cache.Instance.DirectEve.ActiveShip.ShieldPercentage;
                        Cache.Instance.LowestShieldPercentageThisMission = Cache.Instance.DirectEve.ActiveShip.ShieldPercentage;
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                    }
                    if (Cache.Instance.DirectEve.ActiveShip.ArmorPercentage < Cache.Instance.LowestArmorPercentageThisPocket)
                    {
                        Cache.Instance.LowestArmorPercentageThisPocket = Cache.Instance.DirectEve.ActiveShip.ArmorPercentage;
                        Cache.Instance.LowestArmorPercentageThisMission = Cache.Instance.DirectEve.ActiveShip.ArmorPercentage;
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                    }
                    if (Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage < Cache.Instance.LowestCapacitorPercentageThisPocket)
                    {
                        Cache.Instance.LowestCapacitorPercentageThisPocket = Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage;
                        Cache.Instance.LowestCapacitorPercentageThisMission = Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage;
                        Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                    }
                    if ((Cache.Instance.UnlootedContainers != null) && Cache.Instance.WrecksThisPocket != Cache.Instance.UnlootedContainers.Count())
                        Cache.Instance.WrecksThisPocket = Cache.Instance.UnlootedContainers.Count();

                    if (module.GroupId == (int)Group.ShieldBoosters || module.GroupId == (int)Group.ArmorRepairer)
                        module.Click();

                    if (module.GroupId == (int)Group.AncillaryShieldBooster)
                    {
                        if (module.CurrentCharges > 0)
                        {
                            module.Click();
                        }
                    }
                    

                    Cache.Instance.StartedBoosting = DateTime.UtcNow;
                    Cache.Instance.NextRepModuleAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                    if (module.GroupId == (int)Group.ShieldBoosters || module.GroupId == (int)Group.AncillaryShieldBooster)
                    {
                        perc = Cache.Instance.DirectEve.ActiveShip.ShieldPercentage;
                        Logging.Log("Defense", "Shields: [" + Math.Round(perc, 0) + "%] Cap: [" + Math.Round(cap, 0) + "%] Shield Booster: [" + ModuleNumber + "] activated", Logging.White);
                    }
                    else if (module.GroupId == (int)Group.ArmorRepairer)
                    {
                        perc = Cache.Instance.DirectEve.ActiveShip.ArmorPercentage;
                        Logging.Log("Defense", "Armor: [" + Math.Round(perc, 0) + "%] Cap: [" + Math.Round(cap, 0) + "%] Armor Repairer: [" + ModuleNumber + "] activated", Logging.White);
                        int aggressiveEntities = Cache.Instance.Entities.Count(e => e.Distance < (int)Distance.OnGridWithMe && e.IsAttacking && e.IsPlayer);
                        if (aggressiveEntities == 0 && Cache.Instance.Entities.Count(e => e.Distance < (int)Distance.OnGridWithMe && e.IsStation) == 1)
                        {
                            Cache.Instance.NextDockAction = DateTime.UtcNow.AddSeconds(15);
                            Logging.Log("Defense", "Repairing Armor outside station with no aggro (yet): delaying docking for [15]seconds", Logging.White);
                        }
                    }

                    //Logging.Log("LowestShieldPercentage(pocket) [ " + Cache.Instance.lowest_shield_percentage_this_pocket + " ] ");
                    //Logging.Log("LowestArmorPercentage(pocket) [ " + Cache.Instance.lowest_armor_percentage_this_pocket + " ] ");
                    //Logging.Log("LowestCapacitorPercentage(pocket) [ " + Cache.Instance.lowest_capacitor_percentage_this_pocket + " ] ");
                    //Logging.Log("LowestShieldPercentage(mission) [ " + Cache.Instance.lowest_shield_percentage_this_mission + " ] ");
                    //Logging.Log("LowestArmorPercentage(mission) [ " + Cache.Instance.lowest_armor_percentage_this_mission + " ] ");
                    //Logging.Log("LowestCapacitorPercentage(mission) [ " + Cache.Instance.lowest_capacitor_percentage_this_mission + " ] ");
                    continue;
                }

                if (module.IsActive && (perc >= Settings.Instance.DeactivateRepairModules || module.GroupId == (int)Group.CapacitorInjector))
                {
                    module.Click();
                    Cache.Instance.NextRepModuleAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.DefenceDelay_milliseconds);
                    Cache.Instance.RepairCycleTimeThisPocket = Cache.Instance.RepairCycleTimeThisPocket + ((int)DateTime.UtcNow.Subtract(Cache.Instance.StartedBoosting).TotalSeconds);
                    Cache.Instance.RepairCycleTimeThisMission = Cache.Instance.RepairCycleTimeThisMission + ((int)DateTime.UtcNow.Subtract(Cache.Instance.StartedBoosting).TotalSeconds);
                    Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                    if (module.GroupId == (int)Group.ShieldBoosters || module.GroupId == (int)Group.CapacitorInjector)
                    {
                        perc = Cache.Instance.DirectEve.ActiveShip.ShieldPercentage;
                        Logging.Log("Defense", "Shields: [" + Math.Round(perc, 0) + "%] Cap: [" + Math.Round(cap, 0) + "%] Shield Booster: [" + ModuleNumber + "] deactivated [" + Math.Round(Cache.Instance.NextRepModuleAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "] sec reactivation delay", Logging.White);
                    }
                    else if (module.GroupId == (int)Group.ArmorRepairer)
                    {
                        perc = Cache.Instance.DirectEve.ActiveShip.ArmorPercentage;
                        Logging.Log("Defense", "Armor: [" + Math.Round(perc, 0) + "%] Cap: [" + Math.Round(cap, 0) + "%] Armor Repairer: [" + ModuleNumber + "] deactivated [" + Math.Round(Cache.Instance.NextRepModuleAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "] sec reactivation delay", Logging.White);
                    }

                    //Cache.Instance.repair_cycle_time_this_pocket = Cache.Instance.repair_cycle_time_this_pocket + ((int)watch.Elapsed);
                    //Cache.Instance.repair_cycle_time_this_mission = Cache.Instance.repair_cycle_time_this_mission + watch.Elapsed.TotalMinutes;
                    continue;
                }
            }
        }

        private void ActivateAfterburner()
        {
            if (DateTime.UtcNow < Cache.Instance.NextAfterburnerAction) //if we just did something wait a fraction of a second
                return;

            ModuleNumber = 0;
            foreach (ModuleCache module in Cache.Instance.Modules)
            {
                if (module.GroupId != (int)Group.Afterburner)
                    continue;

                ModuleNumber++;

                if (module.InLimboState)
                    continue;

                // Should we activate the module
                bool activate = Cache.Instance.IsApproachingOrOrbiting;
                activate &= !module.IsActive;

                // Should we deactivate the module?
                bool deactivate = !Cache.Instance.IsApproaching;
                deactivate &= module.IsActive;
                deactivate &= ((!Cache.Instance.Entities.Any(e => e.IsAttacking) && DateTime.UtcNow > Statistics.Instance.StartedPocket.AddSeconds(60)) || !Settings.Instance.SpeedTank);

                // This only applies when not speed tanking
                if (!Settings.Instance.SpeedTank && Cache.Instance.IsApproachingOrOrbiting)
                {
                    // Activate if target is far enough
                    activate &= Cache.Instance.Approaching.Distance > Settings.Instance.MinimumPropulsionModuleDistance;

                    // Deactivate if target is too close
                    deactivate |= Cache.Instance.Approaching.Distance < Settings.Instance.MinimumPropulsionModuleDistance;
                }

                // If we have less then x% cap, do not activate the module
                //Logging.Log("Defense: Current Cap [" + Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage + "]" + "Settings: minimumPropulsionModuleCapacitor [" + Settings.Instance.MinimumPropulsionModuleCapacitor + "]");
                activate &= Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage > Settings.Instance.MinimumPropulsionModuleCapacitor;
                deactivate |= Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage < Settings.Instance.MinimumPropulsionModuleCapacitor;

                if (activate && !module.IsActive)
                {
                    module.Click();
                    Cache.Instance.NextAfterburnerAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.AfterburnerDelay_milliseconds);
                }
                else if (deactivate && module.IsActive)
                {
                    module.Click();
                    Cache.Instance.NextAfterburnerAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.AfterburnerDelay_milliseconds);
                }
                return;
            }
        }

        public void ProcessState()
        {
            // Only pulse state changes every 1.5s
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < 500) //default: 500ms
                return;
            _lastPulse = DateTime.UtcNow;

            // Thank god stations are safe ! :)
            if (Cache.Instance.InStation)
            {
                _trackingLinkScriptAttempts = 0;
                _sensorBoosterScriptAttempts = 0;
                _sensorDampenerScriptAttempts = 0;
                _trackingComputerScriptAttempts = 0;
                _trackingDisruptorScriptAttempts = 0;
                return;
            }

            if (!Cache.Instance.InSpace)
            {
                _lastSessionChange = DateTime.UtcNow;
                return;
            }

            // What? No ship entity?
            if (Cache.Instance.DirectEve.ActiveShip.Entity == null || Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Capsule)
            {
                _lastSessionChange = DateTime.UtcNow;
                return;
            }

            if (DateTime.UtcNow.Subtract(_lastSessionChange).TotalSeconds < 7)
            {
                if (Settings.Instance.DebugDefense) Logging.Log("Defense", "we just completed a session change less than 7 seconds ago... waiting.", Logging.White);
                return;
            }

            // There is no better defense then being cloaked ;)
            if (Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked)
                return;

            // Cap is SO low that we should not care about hardeners/boosters as we are not being targeted anyhow
            if (Cache.Instance.DirectEve.ActiveShip.CapacitorPercentage < 10 && !Cache.Instance.TargetedBy.Any())
                return;

            ActivateOnce();
            ActivateRepairModules();

            // this effectively disables control of speed modules when paused, which is expected behavior
            if (Cache.Instance.Paused)
            {
                return;
            }

            if (Cache.Instance.InWarp)
            {
                _trackingLinkScriptAttempts = 0;
                _sensorBoosterScriptAttempts = 0;
                _sensorDampenerScriptAttempts = 0;
                _trackingComputerScriptAttempts = 0;
                _trackingDisruptorScriptAttempts = 0;
                return;
            }

            ActivateAfterburner();
        }
    }
}