// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

using Questor.Modules.BackgroundTasks;
using Questor.Modules.Caching;

namespace Questor.Modules.Combat
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using Questor.Modules.Logging;
    using Questor.Modules.Lookup;
    using Questor.Modules.States;

    /// <summary>
    ///   The combat class will target and kill any NPC that is targeting the questor.
    ///   It will also kill any NPC that is targeted but not aggressive  toward the questor.
    /// </summary>
    public class Combat
    {
        private readonly Dictionary<long, DateTime> _lastModuleActivation = new Dictionary<long, DateTime>();
        private static readonly Dictionary<long, DateTime> LastWeaponReload = new Dictionary<long, DateTime>();
        private bool _isJammed;
        private static int _weaponNumber;

        private int MaxCharges { get; set; }

        private DateTime _lastCombatProcessState;

        //private static DateTime _lastReloadAll;
        private static int _reloadAllIteration;

        private IEnumerable<EntityCache> highValueTargetsTargeted;
        private IEnumerable<EntityCache> lowValueTargetsTargeted;
        private int maxHighValueTarget;
        private int maxLowValueTarget;
        private int maxTotalTargets;

        public Combat()
        {
            maxLowValueTarget = Settings.Instance.MaximumLowValueTargets;
            maxHighValueTarget = Settings.Instance.MaximumHighValueTargets;
            maxTotalTargets = maxHighValueTarget + maxLowValueTarget;
        }


        /// <summary> Reload correct (tm) ammo for the NPC
        /// </summary>
        /// <param name = "weapon"></param>
        /// <param name = "entity"></param>
        /// <param name = "weaponNumber"></param>
        /// <returns>True if the (enough/correct) ammo is loaded, false if wrong/not enough ammo is loaded</returns>
        public static bool ReloadNormalAmmo(ModuleCache weapon, EntityCache entity, int weaponNumber)
        {
            if (Settings.Instance.WeaponGroupId == 53) return true;
            if (entity == null) return false;

            DirectContainer cargo = Cache.Instance.DirectEve.GetShipsCargo();

            // Get ammo based on damage type
            IEnumerable<Ammo> correctAmmo = Settings.Instance.Ammo.Where(a => a.DamageType == Cache.Instance.DamageType).ToList();

            // Check if we still have that ammo in our cargo
            IEnumerable<Ammo> correctAmmoIncargo = correctAmmo.Where(a => cargo.Items.Any(i => i.TypeId == a.TypeId && i.Quantity >= Settings.Instance.MinimumAmmoCharges)).ToList();

            //check if mission specific ammo is defined
            if (Cache.Instance.MissionAmmo.Count() != 0)
            {
                correctAmmoIncargo = Cache.Instance.MissionAmmo.Where(a => a.DamageType == Cache.Instance.DamageType).ToList();
            }

            // Check if we still have that ammo in our cargo
            correctAmmoIncargo = correctAmmoIncargo.Where(a => cargo.Items.Any(i => i.TypeId == a.TypeId && i.Quantity >= Settings.Instance.MinimumAmmoCharges)).ToList();
            if (Cache.Instance.MissionAmmo.Count() != 0)
            {
                correctAmmoIncargo = Cache.Instance.MissionAmmo;
            }

            // We are out of ammo! :(
            if (!correctAmmoIncargo.Any())
            {
                Logging.Log("Combat", "ReloadNormalAmmo: not enough [" + Cache.Instance.DamageType + "] ammo in cargohold: MinimumCharges: [" + Settings.Instance.MinimumAmmoCharges + "]", Logging.Orange);
                _States.CurrentCombatState = CombatState.OutOfAmmo;
                return false;
            }

            /******
            if (weapon.Charge != null)
            {
                IEnumerable<Ammo> areWeMissingAmmo = correctAmmo.Where(a => a.TypeId == weapon.Charge.TypeId);
                if (!areWeMissingAmmo.Any())
                {
                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastLoggingAction).TotalSeconds > 4)
                    {
                        Logging.Log("Combat", "ReloadNormalAmmo: We have ammo loaded that does not have a full reload available, checking cargo for other ammo", Logging.Orange);
                        Cache.Instance.LastLoggingAction = DateTime.UtcNow;
                        try
                        {
                            if (Settings.Instance.Ammo.Any())
                            {
                                DirectItem availableAmmo = cargo.Items.OrderByDescending(i => i.Quantity).Where(a => Settings.Instance.Ammo.Any(i => i.TypeId == a.TypeId)).ToList().FirstOrDefault();
                                if (availableAmmo != null)
                                {
                                    Cache.Instance.DamageType = Settings.Instance.Ammo.ToList().OrderByDescending(i => i.Quantity).Where(a => a.TypeId == availableAmmo.TypeId).ToList().FirstOrDefault().DamageType;
                                    Logging.Log("Combat", "ReloadNormalAmmo: found [" + availableAmmo.Quantity + "] units of  [" + availableAmmo.TypeName + "] changed DamageType to [" + Cache.Instance.DamageType.ToString() + "]", Logging.Orange);
                                    return false;
                                }

                                Logging.Log("Combat", "ReloadNormalAmmo: unable to find any alternate ammo in your cargo", Logging.teal);
                                _States.CurrentCombatState = CombatState.OutOfAmmo;
                                return false;
                            }
                        }
                        catch (Exception)
                        {
                            Logging.Log("Combat", "ReloadNormalAmmo: unable to find any alternate ammo in your cargo", Logging.teal);
                            _States.CurrentCombatState = CombatState.OutOfAmmo;
                        }
                        return false;
                    }
                }
            }
            *****/

            // Get the best possible ammo
            Ammo ammo = correctAmmoIncargo.FirstOrDefault();
            try
            {
                if (ammo != null && entity != null)
                {
                    ammo = correctAmmoIncargo.Where(a => a.Range > entity.Distance).OrderBy(a => a.Range).FirstOrDefault();
                }
            }
            catch (Exception exception)
            {
                Logging.Log("Combat", "ReloadNormalAmmo: Unable to find the correct ammo: waiting [" + exception + "]", Logging.Teal);
                return false;
            }

            // We do not have any ammo left that can hit targets at that range!
            if (ammo == null)
                return false;

            // We have enough ammo loaded
            if (weapon.Charge != null && weapon.Charge.TypeId == ammo.TypeId && weapon.CurrentCharges >= Settings.Instance.MinimumAmmoCharges)
            {
                LastWeaponReload[weapon.ItemId] = DateTime.UtcNow; //mark this weapon as reloaded... by the time we need to reload this timer will have aged enough...
                return true;
            }

            // Retry later, assume its ok now
            //if (!weapon.MatchingAmmo.Any())
            //{
            //    LastWeaponReload[weapon.ItemId] = DateTime.UtcNow; //mark this weapon as reloaded... by the time we need to reload this timer will have aged enough...
            //    return true;
            //}

            DirectItem charge = cargo.Items.FirstOrDefault(i => i.TypeId == ammo.TypeId && i.Quantity >= Settings.Instance.MinimumAmmoCharges);

            // This should have shown up as "out of ammo"
            if (charge == null)
                return false;

            // We are reloading, wait Time.ReloadWeaponDelayBeforeUsable_seconds (see time.cs)
            if (LastWeaponReload.ContainsKey(weapon.ItemId) && DateTime.UtcNow < LastWeaponReload[weapon.ItemId].AddSeconds(Time.Instance.ReloadWeaponDelayBeforeUsable_seconds))
                return true;
            LastWeaponReload[weapon.ItemId] = DateTime.UtcNow;

            try
            {
                // Reload or change ammo
                if (weapon.Charge != null && weapon.Charge.TypeId == charge.TypeId && !weapon.IsChangingAmmo)
                {
                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastLoggingAction).TotalSeconds > 10)
                    {
                        Cache.Instance.TimeSpentReloading_seconds = Cache.Instance.TimeSpentReloading_seconds + Time.Instance.ReloadWeaponDelayBeforeUsable_seconds;
                        Cache.Instance.LastLoggingAction = DateTime.UtcNow;
                    }
                    Logging.Log("Combat", "Reloading [" + weaponNumber + "] with [" + charge.TypeName + "][" + Math.Round((double)ammo.Range / 1000, 0) + "][TypeID: " + charge.TypeId + "]", Logging.Teal);
                    weapon.ReloadAmmo(charge);
                    weapon.ReloadTimeThisMission = weapon.ReloadTimeThisMission + Time.Instance.ReloadWeaponDelayBeforeUsable_seconds;
                    return false;
                }

                if (!weapon.IsChangingAmmo)
                {
                    if (DateTime.UtcNow.Subtract(Cache.Instance.LastLoggingAction).TotalSeconds > 10)
                    {
                        Cache.Instance.TimeSpentReloading_seconds = Cache.Instance.TimeSpentReloading_seconds + Time.Instance.ReloadWeaponDelayBeforeUsable_seconds;
                        Cache.Instance.LastLoggingAction = DateTime.UtcNow;
                    }

                    Logging.Log("Combat", "Changing [" + weaponNumber + "] with [" + charge.TypeName + "][" + Math.Round((double)ammo.Range / 1000, 0) + "][TypeID: " + charge.TypeId + "] so we can hit [" + entity.Name + "][" + Math.Round(entity.Distance / 1000, 0) + "k]", Logging.Teal);
                    weapon.ChangeAmmo(charge);
                    weapon.ReloadTimeThisMission = weapon.ReloadTimeThisMission + Time.Instance.ReloadWeaponDelayBeforeUsable_seconds;
                    return false;
                }

                if (weapon.IsChangingAmmo)
                {
                    Logging.Log("Combat", "Weapon [" + weaponNumber + "] is already reloading. waiting", Logging.Teal);
                    return false;
                }
            }
            catch (Exception exception)
            {
                Logging.Log("Combat.ReloadNormalAmmo", "Exception [" + exception + "]", Logging.Debug);
            }

            // Return true as we are reloading ammo, assume it is the correct ammo...
            return true;
        }

        public static bool ReloadEnergyWeaponAmmo(ModuleCache weapon, EntityCache entity, int weaponNumber)
        {
            DirectContainer cargo = Cache.Instance.DirectEve.GetShipsCargo();

            // Get ammo based on damage type
            IEnumerable<Ammo> correctAmmo = Settings.Instance.Ammo.Where(a => a.DamageType == Cache.Instance.DamageType).ToList();

            // Check if we still have that ammo in our cargo
            IEnumerable<Ammo> correctAmmoInCargo = correctAmmo.Where(a => cargo.Items.Any(i => i.TypeId == a.TypeId)).ToList();

            //check if mission specific ammo is defined
            if (Cache.Instance.MissionAmmo.Count() != 0)
            {
                correctAmmoInCargo = Cache.Instance.MissionAmmo.Where(a => a.DamageType == Cache.Instance.DamageType).ToList();
            }

            // Check if we still have that ammo in our cargo
            correctAmmoInCargo = correctAmmoInCargo.Where(a => cargo.Items.Any(i => i.TypeId == a.TypeId && i.Quantity >= Settings.Instance.MinimumAmmoCharges)).ToList();
            if (Cache.Instance.MissionAmmo.Count() != 0)
            {
                correctAmmoInCargo = Cache.Instance.MissionAmmo;
            }

            // We are out of ammo! :(
            if (!correctAmmoInCargo.Any())
            {
                Logging.Log("Combat", "ReloadEnergyWeapon: not enough [" + Cache.Instance.DamageType + "] ammo in cargohold: MinimumCharges: [" + Settings.Instance.MinimumAmmoCharges + "]", Logging.Orange);
                _States.CurrentCombatState = CombatState.OutOfAmmo;
                return false;
            }

            if (weapon.Charge != null)
            {
                IEnumerable<Ammo> areWeMissingAmmo = correctAmmoInCargo.Where(a => a.TypeId == weapon.Charge.TypeId);
                if (!areWeMissingAmmo.Any())
                {
                    Logging.Log("Combat", "ReloadEnergyWeaponAmmo: We have ammo loaded that does not have a full reload available in the cargo.", Logging.Orange);
                }
            }

            // Get the best possible ammo - energy weapons change ammo near instantly
            Ammo ammo = correctAmmoInCargo.Where(a => a.Range > (entity.Distance)).OrderBy(a => a.Range).FirstOrDefault(); //default

            // We do not have any ammo left that can hit targets at that range!
            if (ammo == null)
            {
                if (Settings.Instance.DebugReloadorChangeAmmo) Logging.Log("Combat", "ReloadEnergyWeaponAmmo: best possible ammo: [ ammo == null]", Logging.White);
                return false;
            }

            if (Settings.Instance.DebugReloadorChangeAmmo) Logging.Log("Combat", "ReloadEnergyWeaponAmmo: best possible ammo: [" + ammo.TypeId + "][" + ammo.DamageType + "]", Logging.White);
            if (Settings.Instance.DebugReloadorChangeAmmo) Logging.Log("Combat", "ReloadEnergyWeaponAmmo: best possible ammo: [" + entity.Name + "][" + Math.Round(entity.Distance / 1000, 0) + "]", Logging.White);

            DirectItem charge = cargo.Items.OrderBy(i => i.Quantity).FirstOrDefault(i => i.TypeId == ammo.TypeId);

            // We do not have any ammo left that can hit targets at that range!
            if (charge == null)
            {
                if (Settings.Instance.DebugReloadorChangeAmmo) Logging.Log("Combat", "ReloadEnergyWeaponAmmo: We do not have any ammo left that can hit targets at that range!", Logging.Orange);
                return false;
            }

            if (Settings.Instance.DebugReloadorChangeAmmo) Logging.Log("Combat", "ReloadEnergyWeaponAmmo: charge: [" + charge.TypeName + "][" + charge.TypeId + "]", Logging.White);

            // We have enough ammo loaded
            if (weapon.Charge != null && weapon.Charge.TypeId == ammo.TypeId)
            {
                if (Settings.Instance.DebugReloadorChangeAmmo) Logging.Log("Combat", "ReloadEnergyWeaponAmmo: We have Enough Ammo of that type Loaded Already", Logging.White);
                return true;
            }

            // We are reloading, wait at least 5 seconds
            if (LastWeaponReload.ContainsKey(weapon.ItemId) && DateTime.UtcNow < LastWeaponReload[weapon.ItemId].AddSeconds(5))
            {
                if (Settings.Instance.DebugReloadorChangeAmmo) Logging.Log("Combat", "ReloadEnergyWeaponAmmo: We are currently reloading: waiting", Logging.White);
                return false;
            }
            LastWeaponReload[weapon.ItemId] = DateTime.UtcNow;

            // Reload or change ammo
            if (weapon.Charge != null && weapon.Charge.TypeId == charge.TypeId)
            {
                Logging.Log("Combat", "Reloading [" + weaponNumber + "] with [" + charge.TypeName + "][" + Math.Round((double)ammo.Range / 1000, 0) + "][TypeID: " + charge.TypeId + "]", Logging.Teal);
                weapon.ReloadAmmo(charge);
                weapon.ReloadTimeThisMission = weapon.ReloadTimeThisMission + 1;
            }
            else
            {
                Logging.Log("Combat", "Changing [" + weaponNumber + "] with [" + charge.TypeName + "][" + Math.Round((double)ammo.Range / 1000, 0) + "][TypeID: " + charge.TypeId + "] so we can hit [" + entity.Name + "][" + Math.Round(entity.Distance / 1000, 0) + "k]", Logging.Teal);
                weapon.ChangeAmmo(charge);
                weapon.ReloadTimeThisMission = weapon.ReloadTimeThisMission + 1;
            }

            // Return false as we are reloading ammo
            return false;
        }

        /// <summary> Reload correct (tm) ammo for the NPC
        /// </summary>
        /// <param name = "weapon"></param>
        /// <param name = "entity"></param>
        /// <param name = "weaponNumber"></param>
        /// <returns>True if the (enough/correct) ammo is loaded, false if wrong/not enough ammo is loaded</returns>
        public static bool ReloadAmmo(ModuleCache weapon, EntityCache entity, int weaponNumber)
        {
            // We need the cargo bay open for both reload actions
            if (!Cache.Instance.OpenCargoHold("Combat: ReloadAmmo")) return false;

            return weapon.IsEnergyWeapon ? ReloadEnergyWeaponAmmo(weapon, entity, weaponNumber) : ReloadNormalAmmo(weapon, entity, weaponNumber);
        }

        public static bool ReloadAll(EntityCache entity)
        {
            _reloadAllIteration++;
            if (Settings.Instance.DebugReloadAll) Logging.Log("debug ReloadAll", "Entering reloadAll function (again) - it iterates through all weapon stacks [" + _reloadAllIteration + "]", Logging.White);
            if (_reloadAllIteration > 12)
            {
                if (Settings.Instance.DebugReloadAll) Logging.Log("debug ReloadAll:", "reset iteration counter", Logging.Orange);
                return true;
            }

            IEnumerable<ModuleCache> weapons = Cache.Instance.Weapons;
            _weaponNumber = 0;
            foreach (ModuleCache weapon in weapons)
            {
                // Reloading energy weapons prematurely just results in unnecessary error messages, so let's not do that
                if (weapon.IsEnergyWeapon)
                    continue;
                _weaponNumber++;

                if (weapon.IsReloadingAmmo || weapon.IsDeactivating || weapon.IsChangingAmmo || weapon.IsActive)
                {
                    if (Settings.Instance.DebugReloadAll) Logging.Log("debug ReloadAll", "Weapon [" + _weaponNumber + "] is busy, moving on to next weapon", Logging.White);
                    continue;
                }

                if (LastWeaponReload.ContainsKey(weapon.ItemId) && DateTime.UtcNow < LastWeaponReload[weapon.ItemId].AddSeconds(Time.Instance.ReloadWeaponDelayBeforeUsable_seconds))
                {
                    if (Settings.Instance.DebugReloadAll) Logging.Log("debug ReloadAll", "Weapon [" + _weaponNumber + "] has been reloaded recently, moving on to next weapon", Logging.White);
                    continue;
                }
                if (!ReloadAmmo(weapon, entity, _weaponNumber)) return false; //by returning false here we make sure we only reload one gun (or stack) per iteration (basically per second)
                return false;
            }
            if (Settings.Instance.DebugReloadAll) Logging.Log("debug ReloadAll", "completely reloaded all weapons", Logging.White);

            //_lastReloadAll = DateTime.UtcNow;
            _reloadAllIteration = 0;
            return true;
        }

        /// <summary> Returns true if it can activate the weapon on the target
        /// </summary>
        /// <remarks>
        ///   The idea behind this function is that a target that explodes is not being fired on within 5 seconds
        /// </remarks>
        /// <param name = "module"></param>
        /// <param name = "entity"></param>
        /// <param name = "isWeapon"></param>
        /// <returns></returns>
        public bool CanActivate(ModuleCache module, EntityCache entity, bool isWeapon)
        {
            if (isWeapon && !entity.IsTarget)
            {
                Logging.Log("Combat.CanActivate", "We attempted to shoot [" + entity.Name + "][" + Math.Round(entity.Distance/1000, 2) + "] which is currently not locked!", Logging.Debug);
                return false;
            }

            if (isWeapon && entity.Distance > Cache.Instance.MaxRange)
            {
                Logging.Log("Combat.CanActivate", "We attempted to shoot [" + entity.Name + "][" + Math.Round(entity.Distance / 1000, 2) + "] which is out of weapons range!", Logging.Debug);
                return false;
            }

            // We have changed target, allow activation
            if (entity.Id != module.LastTargetId)
                return true;

            // We have reloaded, allow activation
            if (isWeapon && module.CurrentCharges == MaxCharges)
                return true;

            // We haven't reloaded, insert a wait-time
            if (_lastModuleActivation.ContainsKey(module.ItemId))
            {
                if (DateTime.UtcNow.Subtract(_lastModuleActivation[module.ItemId]).TotalSeconds < 3)
                    return false;

                _lastModuleActivation.Remove(module.ItemId);
                return true;
            }

            _lastModuleActivation.Add(module.ItemId, DateTime.UtcNow);
            return false;
        }

        public List<EntityCache> TargetingMe { get; set; }
        public List<EntityCache> NotYetTargetingMe { get; set; }

        private void TargetInfo()
        {
            // Find the first active weapon's target
            EntityCache weaponTarget = null;
            foreach (ModuleCache weapon in Cache.Instance.Weapons.Where(m => m.IsActive))
            {
                // Find the target associated with the weapon
                weaponTarget = Cache.Instance.EntityById(weapon.TargetId);
                if (weaponTarget != null)
                    break;
            }
            if (weaponTarget != null)
            {
                Logging.Log("TargetInfo", "              Name: " + weaponTarget.Name, Logging.Teal);
                Logging.Log("TargetInfo", "        CategoryId: " + weaponTarget.CategoryId, Logging.Teal);
                Logging.Log("TargetInfo", "          Distance: " + weaponTarget.Distance, Logging.Teal);
                Logging.Log("TargetInfo", "           GroupID: " + weaponTarget.GroupId, Logging.Teal);
                Logging.Log("TargetInfo", "          Velocity: " + weaponTarget.Velocity, Logging.Teal);
                Logging.Log("TargetInfo", "      IsNPCFrigate: " + weaponTarget.IsNPCFrigate, Logging.Teal);
                Logging.Log("TargetInfo", "      IsNPCCruiser: " + weaponTarget.IsNPCCruiser, Logging.Teal);
                Logging.Log("TargetInfo", "IsNPCBattlecruiser: " + weaponTarget.IsNPCBattlecruiser, Logging.Teal);
                Logging.Log("TargetInfo", "   IsNPCBattleship: " + weaponTarget.IsNPCBattleship, Logging.Teal);
            }
        }

        /// <summary> Activate weapons
        /// </summary>
        private void ActivateWeapons(EntityCache target)
        {
            // When in warp there's nothing we can do, so ignore everything
            if (Cache.Instance.InSpace && Cache.Instance.InWarp)
            {
                Cache.Instance.RemovePrimaryWeaponPriorityTargets(Cache.Instance.PrimaryWeaponPriorityTargets);
                Cache.Instance.RemoveDronePriorityTargets(Cache.Instance.DronePriorityTargets);
                if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: we are in warp! doing nothing", Logging.Teal);
                return;
            }

            if (DateTime.UtcNow < Cache.Instance.NextWeaponAction) //if we just did something wait a fraction of a second
            {
                if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: waiting on NextWeaponAction", Logging.Teal);
                return;
            }

            //
            // Do we really want a non-mission action moving the ship around at all!! (other than speed tanking)?
            // If you are not in a mission by all means let combat actions move you around as needed
            /*
            if (!Cache.Instance.InMission)
            {
                if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: we are NOT in a mission: navigateintorange", Logging.Teal);
                NavigateOnGrid.NavigateIntoRange(target, "Combat");
            }
            if (Settings.Instance.SpeedTank)
            {
                if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: We are Speed Tanking: navigateintorange", Logging.Teal);
                NavigateOnGrid.NavigateIntoRange(target, "Combat");
            }
            */
            if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: after navigate into range...", Logging.Teal);

            // Get the weapons
            IEnumerable<ModuleCache> weapons = Cache.Instance.Weapons.ToList();

            // TODO: Add check to see if there is better ammo to use! :)
            // Get distance of the target and compare that with the ammo currently loaded

            //Deactivate weapons that needs to be deactivated for this list of reasons...
            _weaponNumber = 0;
            foreach (ModuleCache weapon in weapons)
            {
                _weaponNumber++;
                if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: for each weapon [" + _weaponNumber + "] in weapons", Logging.Teal);

                if (!weapon.IsActive)
                {
                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: weapon [" + _weaponNumber + "] is not active: no need to do anything", Logging.Teal);
                    continue;
                }

                if (weapon.IsReloadingAmmo || weapon.IsDeactivating || weapon.IsChangingAmmo)
                {
                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: weapon [" + _weaponNumber + "] is reloading, deactivating or changing ammo: no need to do anything", Logging.Teal);
                    continue;
                }

                //if (DateTime.UtcNow < Cache.Instance.NextReload) //if we should not yet reload we are likely in the middle of a reload and should wait!
                //{
                //    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: NextReload is still in the future: wait before doing anything with the weapon", Logging.teal);
                //    return;
                //}

                // No ammo loaded
                if (weapon.Charge == null)
                {
                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: no ammo loaded? [" + _weaponNumber + "] reload will happen elsewhere", Logging.Teal);
                    continue;
                }

                Ammo ammo = Settings.Instance.Ammo.FirstOrDefault(a => a.TypeId == weapon.Charge.TypeId);

                //use mission specific ammo
                if (Cache.Instance.MissionAmmo.Count() != 0)
                {
                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: MissionAmmocount is not 0", Logging.Teal);
                    ammo = Cache.Instance.MissionAmmo.FirstOrDefault(a => a.TypeId == weapon.Charge.TypeId);
                }

                // How can this happen? Someone manually loaded ammo
                if (ammo == null)
                {
                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: ammo == null [" + _weaponNumber + "] someone manually loaded ammo?", Logging.Teal);
                    continue;
                }

                // If we have already activated warp, deactivate the weapons
                if (!Cache.Instance.DirectEve.ActiveShip.Entity.IsWarping)
                {
                    // Target is in range
                    if (target.Distance <= ammo.Range)
                    {
                        if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: target is in range: do nothing, wait until it is dead", Logging.Teal);
                        continue;
                    }
                }

                // Target is out of range, stop firing
                if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: deactivate: target is out of range, stop firing", Logging.Teal);
                weapon.Click();
                return;
            }

            // Hack for max charges returning incorrect value
            if (!weapons.Any(w => w.IsEnergyWeapon))
            {
                MaxCharges = Math.Max(MaxCharges, weapons.Max(l => l.MaxCharges));
                MaxCharges = Math.Max(MaxCharges, weapons.Max(l => l.CurrentCharges));
            }

            int weaponsActivatedThisTick = 0;
            int weaponsToActivateThisTick = Cache.Instance.RandomNumber(1, 2);

            // Activate the weapons (it not yet activated)))
            _weaponNumber = 0;
            foreach (ModuleCache weapon in weapons)
            {
                _weaponNumber++;

                // Are we reloading, deactivating or changing ammo?
                if (weapon.IsReloadingAmmo || weapon.IsDeactivating || weapon.IsChangingAmmo || !target.IsTarget)
                {
                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: Activate: weapon [" + _weaponNumber + "] is reloading, deactivating or changing ammo", Logging.Teal);
                    continue;
                }

                // Are we on the right target?
                if (weapon.IsActive)
                {
                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: Activate: weapon [" + _weaponNumber + "] is active already", Logging.Teal);
                    if (weapon.TargetId != target.Id && target.IsTarget)
                    {
                        if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: Activate: weapon [" + _weaponNumber + "] is shooting at the wrong target: deactivating", Logging.Teal);
                        weapon.Click();
                        return;
                    }
                    continue;
                }

                // No, check ammo type and if that is correct, activate weapon
                if (ReloadAmmo(weapon, target, _weaponNumber) && CanActivate(weapon, target, true))
                {
                    if (weaponsActivatedThisTick > weaponsToActivateThisTick)

                        //if we have already activated x num of weapons return, which will wait until the next ProcessState
                        return;

                    if (Settings.Instance.DebugActivateWeapons) Logging.Log("Combat", "ActivateWeapons: Activate: weapon [" + _weaponNumber + "] has the correct ammo: activate", Logging.Teal);
                    weaponsActivatedThisTick++; //increment the num of weapons we have activated this ProcessState so that we might optionally activate more than one module per tick
                    Logging.Log("Combat", "Activating weapon  [" + _weaponNumber + "] on [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                    weapon.Activate(target.Id);
                    Cache.Instance.NextWeaponAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.WeaponDelay_milliseconds);

                    //we know we are connected if we were able to get this far - update the lastknownGoodConnectedTime
                    Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                    Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;
                    continue;
                }
            }
        }

        /// <summary> Activate target painters
        /// </summary>
        public void ActivateTargetPainters(EntityCache target)
        {
            //if (DateTime.UtcNow < Cache.Instance.NextPainterAction) //if we just did something wait a fraction of a second
            //    return;

            List<ModuleCache> targetPainters = Cache.Instance.Modules.Where(m => m.GroupId == (int)Group.TargetPainter).ToList();

            // Find the first active weapon
            // Assist this weapon
            _weaponNumber = 0;
            foreach (ModuleCache painter in targetPainters)
            {
                if (painter.ActivatedTimeStamp.AddSeconds(3) > DateTime.UtcNow)
                    continue;

                _weaponNumber++;

                // Are we on the right target?
                if (painter.IsActive)
                {
                    if (painter.TargetId != target.Id)
                    {
                        painter.Click();
                        return;
                    }

                    continue;
                }

                // Are we deactivating?
                if (painter.IsDeactivating)
                    continue;

                if (CanActivate(painter, target, false))
                {
                    Logging.Log("Combat", "Activating painter [" + _weaponNumber + "] on [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                    painter.Activate(target.Id);
                    Cache.Instance.NextPainterAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.PainterDelay_milliseconds);
                    return;
                }
            }
        }

        /// <summary> Activate Nos
        /// </summary>
        public void ActivateNos(EntityCache target)
        {
            if (DateTime.UtcNow < Cache.Instance.NextNosAction) //if we just did something wait a fraction of a second
                return;

            List<ModuleCache> noses = Cache.Instance.Modules.Where(m => m.GroupId == (int)Group.NOS).ToList();

            //Logging.Log("Combat: we have " + noses.Count.ToString() + " Nos modules");
            // Find the first active weapon
            // Assist this weapon
            _weaponNumber = 0;
            foreach (ModuleCache nos in noses)
            {
                _weaponNumber++;

                // Are we on the right target?
                if (nos.IsActive)
                {
                    if (nos.TargetId != target.Id)
                    {
                        nos.Click();
                        return;
                    }

                    continue;
                }

                // Are we deactivating?
                if (nos.IsDeactivating)
                    continue;

                //Logging.Log("Combat: Distances Target[ " + Math.Round(target.Distance,0) + " Optimal[" + nos.OptimalRange.ToString()+"]");
                // Target is out of Nos range
                if (target.Distance >= Settings.Instance.NosDistance)
                    continue;

                if (CanActivate(nos, target, false))
                {
                    Logging.Log("Combat", "Activating Nos     [" + _weaponNumber + "] on [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
                    nos.Activate(target.Id);
                    Cache.Instance.NextNosAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.NosDelay_milliseconds);
                    return;
                }

                Logging.Log("Combat", "Cannot Activate Nos [" + _weaponNumber + "] on [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "][" + Math.Round(target.Distance / 1000, 0) + "k away]", Logging.Teal);
            }
        }

        /// <summary> Activate StasisWeb
        /// </summary>
        public void ActivateStasisWeb(EntityCache target)
        {
            if (DateTime.UtcNow < Cache.Instance.NextWebAction) //if we just did something wait a fraction of a second
                return;

            List<ModuleCache> webs = Cache.Instance.Modules.Where(m => m.GroupId == (int)Group.StasisWeb).ToList();

            // Find the first active weapon
            // Assist this weapon
            _weaponNumber = 0;
            foreach (ModuleCache web in webs)
            {
                _weaponNumber++;

                // Are we on the right target?
                if (web.IsActive)
                {
                    if (web.TargetId != target.Id)
                    {
                        web.Click();
                        return;
                    }

                    continue;
                }

                // Are we deactivating?
                if (web.IsDeactivating)
                    continue;

                // Target is out of web range
                if (target.Distance >= web.OptimalRange)
                    continue;

                if (CanActivate(web, target, false))
                {
                    Logging.Log("Combat", "Activating web     [" + _weaponNumber + "] on [" + target.Name + "][ID: " + Cache.Instance.MaskedID(target.Id) + "]", Logging.Teal);
                    web.Activate(target.Id);
                    Cache.Instance.NextWebAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.WebDelay_milliseconds);
                    return;
                }
            }
        }

        public bool UnlockHighValueTarget(string module, string reason, bool OutOfRangeOnly = false)
        {
            EntityCache unlockThisHighValueTarget = null;
                    
            if (!OutOfRangeOnly)
            {
                try
                {
                    unlockThisHighValueTarget = highValueTargetsTargeted.Where(h => h.IsTarget
                                                                            && ((h.Id != Cache.Instance.PreferredPrimaryWeaponTarget.Id
                                                                            && h.Id != Cache.Instance.PreferredDroneTarget.Id)
                                                                            || (Cache.Instance.IgnoreTargets.Contains(h.Name.Trim()))
                                                                            || (!h.IsPrimaryWeaponPriorityTarget || (h.IsHigherPriorityPresent && !h.IsLowerPriorityPresent) || (highValueTargetsTargeted.Count() >= maxHighValueTarget && !Cache.Instance.PreferredPrimaryWeaponTarget.IsTarget)))
                                                                            && !h.IsWarpScramblingMe
                                                                            && (highValueTargetsTargeted.Count() >= maxHighValueTarget))
                                                                            .OrderByDescending(t => t.Distance > Cache.Instance.MaxRange)
                                                                            .ThenByDescending(t => t.Distance)
                                                                            .FirstOrDefault();
                }
                catch (NullReferenceException) { }

            }
            else
            {
                try
                {
                unlockThisHighValueTarget = highValueTargetsTargeted.Where(h => h.IsTarget
                                                                        && (h.Distance > Cache.Instance.MaxRange
                                                                        || (Cache.Instance.IgnoreTargets.Contains(h.Name.Trim())))
                                                                        && !h.IsWarpScramblingMe)
                                                                        .OrderByDescending(t => t.Distance > Cache.Instance.MaxRange)
                                                                        .ThenByDescending(t => t.Distance)
                                                                        .FirstOrDefault();
                }
                catch (NullReferenceException) { }
            }
                
            if (unlockThisHighValueTarget != null)
            {
                Logging.Log("Combat [TargetCombatants]" + module, "Unlocking " + unlockThisHighValueTarget.Name + " to make room for [" + reason + "]", Logging.Orange);
                unlockThisHighValueTarget.UnlockTarget("Combat [TargetCombatants]");
                Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                return false;
            }

            if (!OutOfRangeOnly)
            {
                //Logging.Log("Combat [TargetCombatants]" + module, "We don't have a spot open to target [" + reason + "], this could be a problem", Logging.Orange);
                //Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);    
            }

            return true;
            
        }

        public bool UnlockLowValueTarget(string module, string reason, bool OutOfRangeOnly = false)
        {
            
            EntityCache unlockThisLowValueTarget = null;
            if (!OutOfRangeOnly)
            {
                try
                {
                    unlockThisLowValueTarget = lowValueTargetsTargeted.Where(h => h.IsTarget
                                                                    && ((h.Id != Cache.Instance.PreferredPrimaryWeaponTarget.Id
                                                                    && h.Id != Cache.Instance.PreferredDroneTarget.Id)
                                                                    || (Cache.Instance.IgnoreTargets.Contains(h.Name.Trim()))
                                                                    || (!h.IsPrimaryWeaponPriorityTarget || (h.IsHigherPriorityPresent && !h.IsLowerPriorityPresent) || (lowValueTargetsTargeted.Count() >= maxLowValueTarget && !Cache.Instance.PreferredDroneTarget.IsTarget))) 
                                                                    && !h.IsWarpScramblingMe
                                                                    && (lowValueTargetsTargeted.Count() >= maxLowValueTarget))
                                                                    .OrderByDescending(t => t.Distance < Settings.Instance.DroneControlRange) //replace with .IsInDroneRange (which can be set to weapons range if usedrones is falee)
                                                                    .ThenByDescending(t => t.Nearest5kDistance)
                                                                    .FirstOrDefault();
                }
                catch (NullReferenceException) { }
            }
            else
            {
                try
                {
                    unlockThisLowValueTarget = lowValueTargetsTargeted.Where(h => h.IsTarget
                                                                    && ((h.Distance > Cache.Instance.MaxRange)
                                                                    || (Cache.Instance.IgnoreTargets.Contains(h.Name.Trim())))
                                                                    && !h.IsWarpScramblingMe)
                                                                    .OrderByDescending(t => t.Distance < Settings.Instance.DroneControlRange) //replace with .IsInDroneRange (which can be set to weapons range if usedrones is falee)
                                                                    .ThenByDescending(t => t.Nearest5kDistance)
                                                                    .FirstOrDefault();
                }
                catch (NullReferenceException) { }
            }
                    
                
            if (unlockThisLowValueTarget != null)
            {
                Logging.Log("Combat [TargetCombatants]" + module, "Unlocking " + unlockThisLowValueTarget.Name + " to make room for [" + reason + "]", Logging.Orange);
                unlockThisLowValueTarget.UnlockTarget("Combat [TargetCombatants]");
                Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                return false;
            }

            if (!OutOfRangeOnly)
            {
                //Logging.Log("Combat [TargetCombatants]" + module, "We don't have a spot open to target [" + reason + "], this could be a problem", Logging.Orange);
                //Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);    
            }

            return true;
        }

        /// <summary> Target combatants
        /// </summary>
        private void TargetCombatants()
        {
            
            if ((Cache.Instance.InSpace && Cache.Instance.InWarp) // When in warp we should not try to target anything
                    || Cache.Instance.InStation //How can we target if we are in a station?
                    || DateTime.UtcNow < Cache.Instance.NextTargetAction //if we just did something wait a fraction of a second
                    || !Cache.Instance.OpenCargoHold("Combat.TargetCombatants") //If we can't open our cargohold then something MUST be wrong
                )
                return;
            maxLowValueTarget = Settings.Instance.MaximumLowValueTargets;
            maxHighValueTarget = Settings.Instance.MaximumHighValueTargets;
            maxTotalTargets = maxHighValueTarget + maxLowValueTarget;

            #region Debugging for listing possible targets
            if (Settings.Instance.DebugTargetCombatants)
            {
                int i = 0;
                if (Cache.Instance.potentialCombatTargets.Any())
                {
                    Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: list of entities we consider PotentialCombatTargets below", Logging.Debug);

                    foreach (EntityCache t in Cache.Instance.potentialCombatTargets)
                    {
                        i++;
                        Logging.Log("Combat.TargetCombatants", "[" + i + "] Name [" + t.Name + "] Distance [" + Math.Round(t.Distance / 1000, 2) + "] TypeID [" + t.TypeId + "] groupID [" + t.GroupId + "]", Logging.Debug);
                        continue;
                    }
                    Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: list of entities we consider PotentialCombatTargets above", Logging.Debug);
                }
                else if (Cache.Instance.EntitiesNotSelf.Any(e => e.CategoryId == (int)CategoryID.Entity
                                                                 && (e.IsNpc || e.IsNpcByGroupID)
                                                                 && !e.IsContainer
                                                                 && !e.IsFactionWarfareNPC
                                                                 && !e.IsEntityIShouldLeaveAlone
                                                                 && (!e.IsBadIdea || e.IsAttacking)
                                                                 && !e.IsLargeCollidable
                                                                 && !Cache.Instance.IgnoreTargets.Contains(e.Name.Trim())))
                {
                    Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: if (Cache.Instance.potentialCombatTargets.Any()) was false - nothing to shoot?", Logging.Debug);
                    Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: list of entities below", Logging.Debug);

                    foreach (EntityCache t in Cache.Instance.EntitiesNotSelf.Where(e => e.CategoryId == (int)CategoryID.Entity
                                                                 && (e.IsNpc || e.IsNpcByGroupID)
                                                                 && !e.IsContainer
                                                                 && !e.IsFactionWarfareNPC
                                                                 && !e.IsEntityIShouldLeaveAlone
                                                                 && (!e.IsBadIdea || e.IsAttacking)
                                                                 && !e.IsLargeCollidable
                                                                 && !Cache.Instance.IgnoreTargets.Contains(e.Name.Trim())))
                    {
                        i++;
                        Logging.Log("Combat.TargetCombatants", "[" + i + "] Name [" + t.Name + "] Distance [" + Math.Round(t.Distance / 1000, 2) + "] TypeID [" + t.TypeId + "] groupID [" + t.GroupId + "]", Logging.Debug);
                        continue;
                    }

                    Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: list of entities above", Logging.Debug);
                }
            }
            #endregion

            #region ECM Jamming checks
            //
            // First, can we even target?
            // We are ECM'd / jammed, forget targeting anything...
            //
            if (Cache.Instance.DirectEve.ActiveShip.MaxLockedTargets == 0)
            {
                if (!_isJammed)
                {
                    Logging.Log("Combat", "We are jammed and can not target anything", Logging.Orange);
                }

                _isJammed = true;
                return;
            }

            if (_isJammed)
            {
                // Clear targeting list as it does not apply
                Cache.Instance.TargetingIDs.Clear();
                Logging.Log("Combat", "We are no longer jammed, retargeting", Logging.Teal);
            }

            _isJammed = false;
            #endregion

            #region Current active targets/targeting
            //
            // What do we currently have targeted?
            // Get our current targets/targetting
            //
            List<EntityCache> targets = new List<EntityCache>();
            targets.AddRange(Cache.Instance.Targets);
            targets.AddRange(Cache.Instance.Targeting);

            // Get lists of the current high and low value targets
            try
            {
                highValueTargetsTargeted = Cache.Instance.combatTargets.Where(t => (t.TargetValue.HasValue
                                                                                   && (!t.IsSentry || (t.IsSentry && Settings.Instance.KillSentries))
                                                                                   && (t.IsTarget || t.IsTargeting)
                                                                                   && (!t.IsNPCFrigate && !t.IsFrigate))
                                                                                   || t.IsPrimaryWeaponPriorityTarget
                                                                                   || t.IsWarpScramblingMe //which would make this target a warp scrambling drone priority target
                                                                                   || (Cache.Instance.PreferredPrimaryWeaponTarget != null && t.Id == Cache.Instance.PreferredPrimaryWeaponTarget.Id))
                    //|| t.Id == Cache.Instance.PreferredDroneTarget.Id)
                                                                                   .OrderByDescending(t => t.IsNPCBattleship)
                                                                                   .ThenBy(t => t.Nearest5kDistance)
                                                                                   .ToList();
            }
            catch (NullReferenceException) { }

            try
            {
                lowValueTargetsTargeted = Cache.Instance.combatTargets.Where(t => ((!t.IsSentry || (t.IsSentry && Settings.Instance.KillSentries))
                                                                                            && (t.IsTarget || t.IsTargeting)
                                                                                            && (t.IsNPCFrigate || t.IsFrigate))
                                                                                            && (highValueTargetsTargeted.Any(e => e.Id != t.Id))) //if it is a high value target by definition it is NOT a low value target
                                                                                            .OrderByDescending(t => t.IsNPCFrigate || t.IsFrigate)
                                                                                            .ThenBy(t => t.Nearest5kDistance)
                                                                                  .ToList();
            }
            catch (NullReferenceException) { }
            #endregion 

            #region Remove any target that is out of range (lower of Weapon Range or targeting range, definately matters if damped)
            //
            // If it is currently out of our weapon range unlock it for now, unless it is one of our preferred targets which should technically only happen during kill type actions
            //
            if (Cache.Instance.Targets.Any())
            {
                //
                // unlock low value targets that are out of range or ignored
                //
                if (!UnlockLowValueTarget("Combat.TargetCombatants", "OutOfRange or Ignored", true)) return;
                //
                // unlock high value targets that are out of range or ignored
                //
                if (!UnlockHighValueTarget("Combat.TargetCombatants", "OutOfRange or Ignored", true)) return;
            }
            #endregion Remove any target that is too far out of range (Weapon Range)
            
            #region Preferred Primary Weapon target handling
            //
            // Lets deal with our preferred targets next (in other words what Q is actively trying to shoot or engage drones on)
            //
            if (Cache.Instance.PreferredPrimaryWeaponTarget != null && !Cache.Instance.PreferredPrimaryWeaponTarget.IsTarget && !Cache.Instance.PreferredPrimaryWeaponTarget.HasExploded)
            {
                //
                // unlock a lower priority entity if needed
                //
                if (Cache.Instance.PreferredPrimaryWeaponTarget.Distance <= Cache.Instance.MaxRange)
                {
                    if (!UnlockHighValueTarget("Combat.TargetCombatants", "PreferredPrimaryWeaponTarget")) return;
                }

                if ((!Cache.Instance.PreferredPrimaryWeaponTarget.IsTarget && !Cache.Instance.PreferredPrimaryWeaponTarget.IsTargeting)
                    && Cache.Instance.EntitiesActivelyBeingLocked.All(i => i.Id != Cache.Instance.PreferredPrimaryWeaponTarget.Id)
                    && Cache.Instance.PreferredPrimaryWeaponTarget.Distance <= Cache.Instance.MaxRange
                    && !Cache.Instance.PreferredPrimaryWeaponTarget.HasExploded
                    && Cache.Instance.PreferredPrimaryWeaponTarget.LockTarget("TargetCombatants.PreferredPrimaryWeaponTarget"))
                {
                    Logging.Log("Combat", "Targeting preferred primary weapon target [" + Cache.Instance.PreferredPrimaryWeaponTarget.Name + "][ID: " + Cache.Instance.MaskedID(Cache.Instance.PreferredPrimaryWeaponTarget.Id) + "][" + Math.Round(Cache.Instance.PreferredPrimaryWeaponTarget.Distance / 1000, 0) + "k away]", Logging.Teal);
                    //highValueTargets.Add(primaryWeaponPriorityEntity);
                    Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                    return;
                }
            }
            #endregion

            #region Preferred Drone target handling
            //
            // Lets deal with our preferred targets next (in other words what Q is actively trying to shoot or engage drones on)
            //
            if (Cache.Instance.PreferredDroneTarget != null && !Cache.Instance.PreferredDroneTarget.IsTarget)
            {
                //
                // unlock a lower priority entity if needed
                //
                if (Cache.Instance.PreferredDroneTarget.Distance <= Cache.Instance.MaxRange)
                {
                    if (!UnlockLowValueTarget("Combat.TargetCombatants", "PreferredDroneTarget")) return;
                }

                if ((!Cache.Instance.PreferredDroneTarget.IsTarget && !Cache.Instance.PreferredDroneTarget.IsTargeting)
                    && Cache.Instance.EntitiesActivelyBeingLocked.All(i => i.Id != Cache.Instance.PreferredDroneTarget.Id)
                    && Cache.Instance.PreferredDroneTarget.Distance <= Cache.Instance.MaxRange
                    && !Cache.Instance.PreferredDroneTarget.HasExploded
                    && Cache.Instance.PreferredDroneTarget.LockTarget("TargetCombatants.PreferredDroneTarget"))
                {
                    Logging.Log("Combat", "Targeting preferred drone target [" + Cache.Instance.PreferredDroneTarget.Name + "][ID: " + Cache.Instance.MaskedID(Cache.Instance.PreferredDroneTarget.Id) + "][" + Math.Round(Cache.Instance.PreferredDroneTarget.Distance / 1000, 0) + "k away]", Logging.Teal);
                    //highValueTargets.Add(primaryWeaponPriorityEntity);
                    Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                    return;
                }
            }
            #endregion
 
            #region Priority Target Handling
            //
            // Now lets deal with the priority targets
            //
            if (Cache.Instance.PrimaryWeaponPriorityTargets.Any())
            {
                int PrimaryWeaponsPriorityTargetTargeted = targets.Count(t => Cache.Instance.PrimaryWeaponPriorityTargets.Contains(t));

                int PrimaryWeaponsPriorityTargetUnTargeted = Cache.Instance.PrimaryWeaponPriorityTargets.Count() - targets.Count(t => Cache.Instance.PrimaryWeaponPriorityTargets.Contains(t));

                if (PrimaryWeaponsPriorityTargetUnTargeted > 0)
                {
                    //
                    // unlock a lower priority entity if needed
                    //
                    if (!UnlockHighValueTarget("Combat.TargetCombatants", "PrimaryWeaponPriorityTargets")) return;

                    IEnumerable<EntityCache> _primaryWeaponPriority = Cache.Instance.PrimaryWeaponPriorityTargets.Where(t => t.IsTargetWeCanShootButHaveNotYetTargeted)
                                                                                                                     .OrderByDescending(c => c.IsInOptimalRange)
                                                                                                                     .ThenBy(c => c.Distance);

                    if (_primaryWeaponPriority.Any())
                    {
                        if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: [" + _primaryWeaponPriority.Count() + "] primaryWeaponPriority targets", Logging.Debug);

                        foreach (EntityCache primaryWeaponPriorityEntity in _primaryWeaponPriority)
                        {
                            // Have we reached the limit of high value targets?
                            if (highValueTargetsTargeted.Count() >= maxHighValueTarget)
                            {
                                break;
                            }

                            if (primaryWeaponPriorityEntity.Distance < Cache.Instance.MaxRange
                                && !primaryWeaponPriorityEntity.IsTarget
                                && !primaryWeaponPriorityEntity.IsTargeting
                                && primaryWeaponPriorityEntity.Distance < Cache.Instance.LowValueTargetsHaveToBeWithinDistance
                                && primaryWeaponPriorityEntity.LockTarget("TargetCombatants.PrimaryWeaponPriorityEntity"))
                            {
                                Logging.Log("Combat", "Targeting primary weapon priority target [" + primaryWeaponPriorityEntity.Name + "][ID: " + Cache.Instance.MaskedID(primaryWeaponPriorityEntity.Id) + "][" + Math.Round(primaryWeaponPriorityEntity.Distance / 1000, 0) + "k away]", Logging.Teal);
                                Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                                return;
                            }

                            continue;
                        }
                    }
                    else
                    {
                        if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: 0 primaryWeaponPriority targets", Logging.Debug);
                    }
                }
            }
            #endregion

            #region Drone Priority Target Handling
            //
            // Now lets deal with the priority targets
            //
            if (Cache.Instance.DronePriorityTargets.Any())
            {
                int DronesPriorityTargetTargeted = targets.Count(t => Cache.Instance.DronePriorityTargets.Contains(t) && !Cache.Instance.DronePriorityTargets.Contains(t));

                int DronesPriorityTargetUnTargeted = Cache.Instance.DronePriorityTargets.Count() - targets.Count(t => Cache.Instance.DronePriorityTargets.Contains(t) && !Cache.Instance.DronePriorityTargets.Contains(t));

                if (DronesPriorityTargetUnTargeted > 0)
                {
                    if (!UnlockLowValueTarget("Combat.TargetCombatants", "DronePriorityTargets")) return;

                    IEnumerable<EntityCache> _dronePriorityTargets = Cache.Instance.DronePriorityTargets.Where(t => t.IsTargetWeCanShootButHaveNotYetTargeted)
                                                                                                                         .OrderByDescending(c => c.IsInOptimalRange)
                                                                                                                         .ThenBy(c => c.Distance);

                    if (_dronePriorityTargets.Any())
                    {
                        if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: [" + _dronePriorityTargets.Count() + "] dronePriority targets", Logging.Debug);

                        foreach (EntityCache dronePriorityEntity in _dronePriorityTargets)
                        {
                            // Have we reached the limit of high value targets?
                            if (lowValueTargetsTargeted.Count() >= maxLowValueTarget)
                            {
                                break;
                            }

                            if (dronePriorityEntity.Distance < Cache.Instance.MaxRange
                                && !dronePriorityEntity.IsTarget
                                && !dronePriorityEntity.IsTargeting
                                && dronePriorityEntity.Distance < Cache.Instance.LowValueTargetsHaveToBeWithinDistance
                                && dronePriorityEntity.LockTarget("TargetCombatants.PrimaryWeaponPriorityEntity"))
                            {
                                Logging.Log("Combat", "Targeting primary weapon priority target [" + dronePriorityEntity.Name + "][ID: " + Cache.Instance.MaskedID(dronePriorityEntity.Id) + "][" + Math.Round(dronePriorityEntity.Distance / 1000, 0) + "k away]", Logging.Teal);
                                Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                                return;
                            }

                            continue;
                        }
                    }
                    else
                    {
                        if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: 0 primaryWeaponPriority targets", Logging.Debug);
                    }
                }
            }

            
            #endregion
           
            #region Do we have enough targets?
            //
            // OK so now that we are done dealing with preferred and priorities for now, lets see if we can target anything else
            // First lets see if we have enough targets already
            //
            if (highValueTargetsTargeted.Count() >= maxHighValueTarget && lowValueTargetsTargeted.Count() >= maxLowValueTarget)
            {
                if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: we have enough targets targeted [" + targets.Count() + "]", Logging.Debug);
                Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                return;
            }
            #endregion

            #region Aggro Handling
            //
            // OHHHH We are still here? OK Cool lets deal with things that are already targetting me
            //
            TargetingMe = Cache.Instance.TargetedBy.Where(t => t.IsTargetingMeAndNotYetTargeted
                                                            && t.Distance < Cache.Instance.MaxRange)
                                                            .ToList();


            List<EntityCache> highValueTargetingMe;
            highValueTargetingMe = TargetingMe.Where(t => (t.TargetValue.HasValue)
                                                && (!t.IsNPCFrigate && !t.IsFrigate))
                                               .OrderBy(t => t.Nearest5kDistance).ToList();

            List<EntityCache> lowValueTargetingMe;
            lowValueTargetingMe = TargetingMe.Where(t => (t.IsNPCFrigate || t.IsFrigate))
                                             .OrderBy(t => t.Nearest5kDistance).ToList();

            if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "TargetingMe [" + TargetingMe.Count() + "] lowValueTargetingMe [" + lowValueTargetingMe.Count() + "] highValueTargetingMe [" + highValueTargetingMe.Count() + "]", Logging.Debug);

            // High Value
            if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: foreach (EntityCache entity in highValueTargetingMe)", Logging.Debug);

            if (highValueTargetingMe.Any(t => !t.IsTarget && !t.IsTargeting && t.Distance < Cache.Instance.MaxRange))
            {
                if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: [" + highValueTargetingMe.Count() + "] highValueTargetingMe targets", Logging.Debug);

                int HighValueTargetsTargetedThisCycle = 1;
                foreach (EntityCache highValueTargetingMeEntity in highValueTargetingMe.Where(t => !t.IsTarget && !t.IsTargeting && t.Nearest5kDistance < Settings.Instance.DroneControlRange))
                {
                    if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: [" + HighValueTargetsTargetedThisCycle + "][" + highValueTargetingMeEntity.Name + "][" + Math.Round(highValueTargetingMeEntity.Distance / 1000, 2) + "k][groupID" + highValueTargetingMeEntity.GroupId + "]", Logging.Debug);
                    // Have we reached the limit of high value targets?
                    if (highValueTargetsTargeted.Count() >= maxHighValueTarget || HighValueTargetsTargetedThisCycle >= 4)
                    {
                        break;
                    }

                    if (highValueTargetingMeEntity != null
                        && !highValueTargetingMeEntity.IsTarget
                        && !highValueTargetingMeEntity.IsTargeting
                        && highValueTargetingMeEntity.Distance < Cache.Instance.MaxRange
                        && highValueTargetingMeEntity.LockTarget("TargetCombatants.HighValueTargetingMeEntity"))
                    {
                        HighValueTargetsTargetedThisCycle++;
                        Logging.Log("Combat", "Targeting high value target [" + highValueTargetingMeEntity.Name + "][ID: " + Cache.Instance.MaskedID(highValueTargetingMeEntity.Id) + "][" + Math.Round(highValueTargetingMeEntity.Distance / 1000, 0) + "k away] highValueTargets.Count [" + highValueTargetsTargeted.Count() + "]", Logging.Teal);
                        Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                        return;
                    }

                    continue;
                }
            }
            else
            {
                if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: 0 highValueTargetingMe targets", Logging.Debug);
            }

            // Low Value
            if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: foreach (EntityCache entity in lowValueTargetingMe)", Logging.Debug);

            if (lowValueTargetingMe.Any(t => t.Distance < Settings.Instance.DroneControlRange))
            {
                if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: [" + lowValueTargetingMe.Count() + "] lowValueTargetingMe targets", Logging.Debug);

                int LowValueTargetsTargetedThisCycle = 1;
                foreach (EntityCache lowValueTargetingMeEntity in lowValueTargetingMe.Where(t => !t.IsTarget && !t.IsTargeting && t.Nearest5kDistance < Cache.Instance.LowValueTargetsHaveToBeWithinDistance))
                {

                    if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: lowValueTargetingMe [" + LowValueTargetsTargetedThisCycle + "][" + lowValueTargetingMeEntity.Name + "][" + Math.Round(lowValueTargetingMeEntity.Distance / 1000, 2) + "k][groupID" + lowValueTargetingMeEntity.GroupId + "]", Logging.Debug);

                    // Have we reached the limit of low value targets?
                    if (lowValueTargetsTargeted.Count() >= maxLowValueTarget || LowValueTargetsTargetedThisCycle >= 3)
                    {
                        break;
                    }

                    if (lowValueTargetingMeEntity != null
                        && !lowValueTargetingMeEntity.IsTarget
                        && !lowValueTargetingMeEntity.IsTargeting
                        && lowValueTargetingMeEntity.Distance < Cache.Instance.LowValueTargetsHaveToBeWithinDistance
                        && lowValueTargetingMeEntity.LockTarget("TargetCombatants.LowValueTargetingMeEntity"))
                    {
                        LowValueTargetsTargetedThisCycle++;
                        Logging.Log("Combat", "Targeting low  value target [" + lowValueTargetingMeEntity.Name + "][ID: " + Cache.Instance.MaskedID(lowValueTargetingMeEntity.Id) + "][" + Math.Round(lowValueTargetingMeEntity.Distance / 1000, 0) + "k away] lowValueTargets.Count [" + lowValueTargetsTargeted.Count() + "]", Logging.Teal);
                        //lowValueTargets.Add(lowValueTargetingMeEntity);
                        Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                        return;
                    }

                    continue;
                }
            }
            else
            {
                if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: 0 lowValueTargetingMe targets", Logging.Debug);
            }
            #endregion

            #region All else fails grab an unlocked target that is not yet targetting me
            //
            // Ok, now that thats all handled lets grab the closest non aggressed mob and pew
            // Build a list of things not yet targeting me and not yet targetted
            //
            if (!highValueTargetsTargeted.Any() && !lowValueTargetsTargeted.Any() && !highValueTargetingMe.Any() && !lowValueTargetingMe.Any())
            {
                NotYetTargetingMe = Cache.Instance.Entities.Where(t => t.IsNotYetTargetingMeAndNotYetTargeted)
                                                                .OrderBy(t => t.Nearest5kDistance)
                                                                .ToList();

                if (NotYetTargetingMe.Any())
                {
                    if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: [" + NotYetTargetingMe.Count() + "] NotYetTargetingMe targets", Logging.Debug);

                    EntityCache TargetThisNotYetAggressiveNPC = NotYetTargetingMe.FirstOrDefault();
                    if (TargetThisNotYetAggressiveNPC != null
                        && !TargetThisNotYetAggressiveNPC.IsTarget
                        && !TargetThisNotYetAggressiveNPC.IsTargeting
                        && TargetThisNotYetAggressiveNPC.Distance < Cache.Instance.MaxRange
                        && TargetThisNotYetAggressiveNPC.LockTarget("TargetCombatants.TargetThisNotYetAggressiveNPC"))
                    {
                        Logging.Log("Combat", "Targeting non-aggressed NPC target [" + TargetThisNotYetAggressiveNPC.Name + "][ID: " + Cache.Instance.MaskedID(TargetThisNotYetAggressiveNPC.Id) + "][" + Math.Round(TargetThisNotYetAggressiveNPC.Distance / 1000, 0) + "k away]", Logging.Teal);

                        Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(4000);
                        return;
                    }
                }
                else
                {
                    if (Settings.Instance.DebugTargetCombatants) Logging.Log("Combat.TargetCombatants", "DebugTargetCombatants: 0 NotYetTargetingMe targets", Logging.Debug);
                }
            }
            #endregion

            #region I dont think we need these anymore, but keeping them around for now just in case
            /*
            //
            // Do we have too many high value (non-priority) targets targeted?
            //
            if ((PrimaryWeaponsPriorityTargetUnTargeted > 0 && highValueTargetsTargeted.Any(i => (!i.IsPrimaryWeaponPriorityTarget && !i.IsDronePriorityTarget))) 
                || highValueTargetsTargeted.Count() > maxHighValueTarget)
            {
                // Unlock any high value target
                EntityCache unlockThisHighValueTarget = highValueTargetsTargeted.Where(h => h.IsTarget && (!h.IsPrimaryWeaponPriorityTarget && !h.IsDronePriorityTarget))
                                                                        .OrderBy(t => !t.IsInOptimalRange)
                                                                        .ThenBy(t => t.Distance)
                                                                        .FirstOrDefault();
                if (unlockThisHighValueTarget == null)
                {
                    //
                    // Assume that if we have no non-scrambling high value targets that we will have low value targets we can untarget elsewhere
                    //
                    //break;
                }

                if (unlockThisHighValueTarget != null && unlockThisHighValueTarget.IsTarget && unlockThisHighValueTarget.UnlockTarget("Combat.TargetCombatants"))
                {
                    Logging.Log("Combat", "unlocking high value target [" + unlockThisHighValueTarget.Name + "][ID: " + Cache.Instance.MaskedID(unlockThisHighValueTarget.Id) + "]{" + highValueTargetsTargeted.Count + "} [" + Math.Round(unlockThisHighValueTarget.Distance / 1000, 0) + "k away]", Logging.Teal);
                    //highValueTargets.Remove(unlockThisHighValueTarget);
                    
                }
            }

            //
            // Do we have too many low value targets targeted?
            //
            if ((PrimaryWeaponsPriorityTargetUnTargeted > 0 && lowValueTargetsTargeted.Any(i => (!i.IsPrimaryWeaponPriorityTarget && !i.IsDronePriorityTarget))
                || lowValueTargetsTargeted.Count() > maxLowValueTarget))
            {
                // Unlock any target that is not warp scrambling me
                EntityCache unlockThisLowValueTarget = lowValueTargetsTargeted.Where(t => !t.IsWarpScramblingMe && t.IsTarget)
                                                                      .OrderByDescending(t => t.Distance)
                                                                      .FirstOrDefault();

                if (unlockThisLowValueTarget == null)
                {
                    //
                    // Assume that if we have no non-scrambling low value targets that we will have high value targets we can untarget elsewhere
                    //
                    //break;
                }

                if (unlockThisLowValueTarget != null && unlockThisLowValueTarget.IsTarget && unlockThisLowValueTarget.UnlockTarget("Combat.TargetCombatants"))
                {
                    Logging.Log("Combat", "unlocking low  value target [" + unlockThisLowValueTarget.Name + "][ID: " + Cache.Instance.MaskedID(unlockThisLowValueTarget.Id) + "]{" + lowValueTargetsTargeted.Count + "} [" + Math.Round(unlockThisLowValueTarget.Distance / 1000, 0) + "k away]", Logging.Teal);
                    //lowValueTargets.Remove(unlockThisLowValueTarget);
                    Cache.Instance.NextTargetAction = DateTime.UtcNow.AddMilliseconds(Time.Instance.TargetDelay_milliseconds);
                    return;
                }
            }
            */
            #endregion
            
            return;
        }

        public void ProcessState()
        {
            try
            {
                if (DateTime.UtcNow < _lastCombatProcessState.AddMilliseconds(500)) //if it has not been 500ms since the last time we ran this ProcessState return. We can't do anything that close together anyway
                {
                    return;
                }

                _lastCombatProcessState = DateTime.UtcNow;

                if ((_States.CurrentCombatState != CombatState.Idle ||
                    _States.CurrentCombatState != CombatState.OutOfAmmo) &&
                    (Cache.Instance.InStation ||// There is really no combat in stations (yet)
                    !Cache.Instance.InSpace || // if we are not in space yet, wait...
                    Cache.Instance.DirectEve.ActiveShip.Entity == null || // What? No ship entity?
                    Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked))  // There is no combat when cloaked
                {
                    _States.CurrentCombatState = CombatState.Idle;
                    return;
                }

                if (Cache.Instance.InStation)
                {
                    _States.CurrentCombatState = CombatState.Idle;
                    return;
                }

                try
                {
                    if (!Cache.Instance.Weapons.Any() && Cache.Instance.DirectEve.ActiveShip.GivenName == Settings.Instance.CombatShipName)
                    {
                        Logging.Log("Combat", "You are not in the CombatShipName [" + Settings.Instance.CombatShipName + "] and / or the combatship has no weapons!", Logging.Red);
                        _States.CurrentCombatState = CombatState.OutOfAmmo;
                    }
                }
                catch (Exception exception)
                {
                    if (Settings.Instance.DebugExceptions) Logging.Log("Combat", "if (!Cache.Instance.Weapons.Any() && Cache.Instance.DirectEve.ActiveShip.GivenName == Settings.Instance.CombatShipName ) - exception [" + exception + "]", Logging.White);
                }

                switch (_States.CurrentCombatState)
                {
                    case CombatState.CheckTargets:
                        _States.CurrentCombatState = CombatState.KillTargets; //this MUST be before TargetCombatants() or the combat state will potentially get reset (important for the outofammo state)
                        TargetCombatants();
                        break;

                    case CombatState.KillTargets:

                        if (!Cache.Instance.OpenCargoHold("Combat")) break;
                        _States.CurrentCombatState = CombatState.CheckTargets;

                        if (Cache.Instance.PreferredPrimaryWeaponTarget != null && !Cache.Instance.PreferredPrimaryWeaponTarget.HasExploded)
                        {
                            NavigateOnGrid.NavigateIntoRange(Cache.Instance.PreferredPrimaryWeaponTarget, "Combat", Cache.Instance.normalNav);

                            if (Cache.Instance.PreferredPrimaryWeaponTarget.IsReadyToShoot)
                            {
                                if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Painters", Logging.Debug);
                                ActivateTargetPainters(Cache.Instance.PreferredPrimaryWeaponTarget);
                                if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Webs", Logging.Debug);
                                ActivateStasisWeb(Cache.Instance.PreferredPrimaryWeaponTarget);
                                if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Nos", Logging.Debug);
                                ActivateNos(Cache.Instance.PreferredPrimaryWeaponTarget);
                                if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Weapons", Logging.Debug);
                                ActivateWeapons(Cache.Instance.PreferredPrimaryWeaponTarget);
                                return;    
                            }

                            return;
                        }
                        
                        if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "We do not currently have a kill target ready, how can this be?", Logging.Debug);
                        Cache.Instance.GetBestTarget(Cache.Instance.MaxRange, false, "Combat");
                        
                        #region original code dont delete yet
                        /*
                        //
                        // Cache.Instance.PreferredPrimaryWeaponTarget is set by GetBestTarget()
                        //
                        if (Cache.Instance.Targets.Any()) //weapontarget can be null, we might not yet be shooting anything. 
                        {
                            if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "if (Cache.Instance.Targets.Any()) //weapontarget can be null, we might not yet be shooting anything.", Logging.Debug);

                            //
                            // run GetBestTarget here (every x seconds), GetBestTarget also runs in CombatMissionCtrl (but only once per tick, total)
                            //
                           
                            if (!Cache.Instance.InMission) Cache.Instance.GetBestTarget(Cache.Instance.MaxRange, false, "Combat");
                            //
                            // GetBestTarget sets Cache.Instance.PreferredPrimaryWeaponTarget (or for drones in drone.cs: Cache.Instance.PreferredPrimaryWeaponTarget) 
                            //
                            
                            if (Cache.Instance.PreferredPrimaryWeaponTarget != null)
                            {
                                if (!Cache.Instance.PreferredPrimaryWeaponTarget.HasExploded)
                                {
                                    if (Cache.Instance.PreferredPrimaryWeaponTarget.Distance < Cache.Instance.MaxRange)
                                    {
                                        if (Cache.Instance.PreferredPrimaryWeaponTarget.IsTarget)
                                        {
                                            if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Painters", Logging.Debug);
                                            ActivateTargetPainters(Cache.Instance.PreferredPrimaryWeaponTarget);
                                            if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Webs", Logging.Debug);
                                            ActivateStasisWeb(Cache.Instance.PreferredPrimaryWeaponTarget);
                                            if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Nos", Logging.Debug);
                                            ActivateNos(Cache.Instance.PreferredPrimaryWeaponTarget);
                                            if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "Activating Weapons", Logging.Debug);
                                            ActivateWeapons(Cache.Instance.PreferredPrimaryWeaponTarget);
                                            return;
                                        }
                                        if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "if (Cache.Instance.PreferredPrimaryWeaponTarget.IsTarget) failed", Logging.Debug);
                                    }
                                    else
                                    {
                                        if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "if (Cache.Instance.PreferredPrimaryWeaponTarget.Distance < Cache.Instance.MaxRange) failed", Logging.Debug);
                                    }
                                }
                                else
                                {
                                    Cache.Instance.GetBestTarget(Cache.Instance.MaxRange, false, "Combat");
                                }
                            }
                            else
                            {
                                if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "if (Cache.Instance.PreferredPrimaryWeaponTarget != null) failed", Logging.Debug);
                            }
                        }
                        else
                        {
                            if (Settings.Instance.DebugKillTargets) Logging.Log("Combat.KillTargets", "if (Cache.Instance.Targets.Any()) failed", Logging.Debug);
                        }*/
                        #endregion 
                        break;

                    case CombatState.OutOfAmmo:
                        break;

                    case CombatState.Idle:

                        //
                        // below is the reasons we will start the combat state(s) - if the below is not met do nothing
                        //
                        //Logging.Log("Cache.Instance.InSpace: " + Cache.Instance.InSpace);
                        //Logging.Log("Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked: " + Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked);
                        //Logging.Log("Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower(): " + Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower());
                        //Logging.Log("Cache.Instance.InSpace: " + Cache.Instance.InSpace);
                        if (Cache.Instance.InSpace && //we are in space (as opposed to being in station or in limbo between systems when jumping)
                            (Cache.Instance.DirectEve.ActiveShip.Entity != null &&  // we are in a ship!
                            !Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked && //we are not cloaked anymore
                            Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() == Settings.Instance.CombatShipName.ToLower() && //we are in our combat ship
                            !Cache.Instance.InWarp)) // no longer in warp
                        {
                            _States.CurrentCombatState = CombatState.CheckTargets;
                            return;
                        }
                        break;

                    default:

                        // Next state
                        Logging.Log("Combat", "CurrentCombatState was not set thus ended up at default", Logging.Orange);
                        _States.CurrentCombatState = CombatState.CheckTargets;
                        break;
                }
            }
            catch (Exception exception)
            {
                Logging.Log("Combat.ProcessState", "Exception [" + exception + "]", Logging.Debug);
            }
        }
    }
}