// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor.Modules.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using System.Xml.Linq;
    using System.Globalization;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    using global::Questor.Modules.Logging;
    using Questor.Modules.BackgroundTasks;

    public class Arm
    {
        private bool _bringItemMoved;
        private bool _bringoptionalItemMoved;
        private bool ItemsAreBeingMoved;
        private bool CheckCargoForBringItem;
        private bool CheckCargoForOptionalBringItem;
        //private bool CheckCargoForAmmo;

        private DateTime _lastPulse;
        private DateTime _lastArmAction;

        private int bringItemQuantity;
        private int bringOptionalItemQuantity;
        public Arm()
        {
            AmmoToLoad = new List<Ammo>();
        }

        // Bleh, we don't want this here, can we move it to cache?
        public long AgentId { get; set; }

        public List<Ammo> AmmoToLoad { get; private set; }

        private bool DefaultFittingChecked; //false; //flag to check for the correct default fitting before using the fitting manager
        private bool DefaultFittingFound; //Did we find the default fitting?
        private bool TryMissionShip = true;  // Used in the event we can't find the ship specified in the missionfittings
        private bool UseMissionShip; //false; // Were we successful in activating the mission specific ship?
        private bool CustomFittingFound;
        private bool WaitForFittingToLoad = true;
        private bool capsMoved = false;
        private bool ammoMoved = false;
        private int retryCount = 0;

        public void LoadSpecificAmmo(IEnumerable<DamageType> damageTypes)
        {
            AmmoToLoad.Clear();
            AmmoToLoad.AddRange(Settings.Instance.Ammo.Where(a => damageTypes.Contains(a.DamageType)).Select(a => a.Clone()));
        }

        private bool FindDefaultFitting(string module)
        {
            DefaultFittingFound = false;
            if (!DefaultFittingChecked)
            {
                if (!Cache.Instance.OpenFittingManagerWindow(module)) return false;

                if (Cache.Instance.DefaultFitting == null)
                {
                    Cache.Instance.DefaultFitting = Settings.Instance.DefaultFitting.Fitting;
                    Cache.Instance.Fitting = Cache.Instance.DefaultFitting;
                }
                if (Settings.Instance.DebugFittingMgr) Logging.Log(module, "Character Settings XML says Default Fitting is [" + Cache.Instance.DefaultFitting + "]", Logging.White);

                if (Cache.Instance.FittingManagerWindow.Fittings.Any())
                {
                    if (Settings.Instance.DebugFittingMgr) Logging.Log(module, "if (Cache.Instance.FittingManagerWindow.Fittings.Any())", Logging.Teal);
                    int i = 1;
                    foreach (DirectFitting fitting in Cache.Instance.FittingManagerWindow.Fittings)
                    {
                        //ok found it
                        if (Settings.Instance.DebugFittingMgr)
                        {
                            Logging.Log(module, "[" + i + "] Found a Fitting Named [" + fitting.Name + "]", Logging.Teal);
                        }

                        if (fitting.Name.ToLower().Equals(Cache.Instance.DefaultFitting.ToLower()))
                        {
                            DefaultFittingChecked = true;
                            DefaultFittingFound = true;
                            Logging.Log(module, "[" + i + "] Found Default Fitting [" + fitting.Name + "]", Logging.White);
                            return true;
                        }
                        i++;
                    }
                }
                else
                {
                    Logging.Log("Arm.LoadFitting", "No Fittings found in the Fitting Mangar at all!  Disabling fitting manager.", Logging.Orange);
                    DefaultFittingChecked = true;
                    DefaultFittingFound = false;
                    return true;
                }

                if (!DefaultFittingFound)
                {
                    Logging.Log("Arm.LoadFitting", "Error! Could not find Default Fitting [" + Cache.Instance.DefaultFitting.ToLower() + "].  Disabling fitting manager.", Logging.Orange);
                    DefaultFittingChecked = true;
                    DefaultFittingFound = false;
                    Settings.Instance.UseFittingManager = false;
                    Logging.Log("Arm.LoadFitting", "Closing Fitting Manager", Logging.White);
                    Cache.Instance.FittingManagerWindow.Close();

                    _States.CurrentArmState = ArmState.MoveItems;
                    return true;
                }
            }
            return false;
        }

        public void ProcessState()
        {
            // Only pulse state changes every 1.5s
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < 1500) //default: 1500ms
                return;

            _lastPulse = DateTime.UtcNow;

            if (!Cache.Instance.InStation)
                return;

            if (Cache.Instance.InSpace)
                return;

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20)) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return;

            switch (_States.CurrentArmState)
            {
                case ArmState.Idle:
                    break;

                case ArmState.Cleanup:
                    if (!Cleanup.CloseInventoryWindows()) break;
                    _States.CurrentArmState = ArmState.Done;
                    break;

                case ArmState.Done:
                    break;

                case ArmState.NotEnoughDrones:

                    //This is logged in questor.cs - do not double log
                    //Logging.Log("Arm","Armstate.NotEnoughDrones");
                    //State = ArmState.Idle;
                    break;

                case ArmState.NotEnoughAmmo:

                    //This is logged in questor.cs - do not double log
                    //Logging.Log("Arm","Armstate.NotEnoughAmmo");
                    //State = ArmState.Idle;
                    break;

                case ArmState.Begin:
                    if (!Cleanup.CloseInventoryWindows()) break;
                    Cache.Instance.ArmLoadedCache = false;
                    TryMissionShip = true;           // Used in the event we can't find the ship specified in the missionfittings
                    UseMissionShip = false;          // Were we successful in activating the mission specific ship?
                    DefaultFittingChecked = false;   //flag to check for the correct default fitting before using the fitting manager
                    DefaultFittingFound = false;      //Did we find the default fitting?
                    CustomFittingFound = false;
                    WaitForFittingToLoad = false;
                    _bringItemMoved = false;
                    bringItemQuantity = Math.Max(Cache.Instance.BringMissionItemQuantity, 1);
                    CheckCargoForBringItem = true;
                    bringOptionalItemQuantity = Math.Max(Cache.Instance.BringOptionalMissionItemQuantity, 1);
                    _bringoptionalItemMoved = false;
                    CheckCargoForOptionalBringItem = true;
                    capsMoved = false;
                    ammoMoved = false;
                    retryCount = 0;

                    if (Cache.Instance.MissionAmmo.Any())
                    {
                        AmmoToLoad = new List<Ammo>(Cache.Instance.MissionAmmo);
                    }

                    //CheckCargoForAmmo = true;

                    _States.CurrentArmState = ArmState.OpenShipHangar;
                    _States.CurrentCombatState = CombatState.Idle;
                    Cache.Instance.NextArmAction = DateTime.UtcNow;
                    break;

                case ArmState.OpenShipHangar:
                case ArmState.SwitchToTransportShip:
                case ArmState.SwitchToSalvageShip:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;
                    
                    if (!Cache.Instance.OpenShipsHangar("Arm")) return;

                    if (string.IsNullOrEmpty(Settings.Instance.CombatShipName) || string.IsNullOrEmpty(Settings.Instance.SalvageShipName))
                    {
                        Logging.Log("Arm","CombatShipName and SalvageShipName both have to be populated! Fix your characters config.",Logging.Red);
                        _States.CurrentArmState = ArmState.NotEnoughAmmo;
                        Cache.Instance.Paused = true;
                        return;
                    }

                    if (Settings.Instance.CombatShipName == Settings.Instance.SalvageShipName)
                    {
                        Logging.Log("Arm", "CombatShipName and SalvageShipName cannot be the same ship/shipname ffs! Fix your characters config.", Logging.Red);
                        _States.CurrentArmState = ArmState.NotEnoughAmmo;
                        Cache.Instance.Paused = true;
                        return;
                    }

                    if (_States.CurrentArmState == ArmState.OpenShipHangar)
                    {
                        Logging.Log("Arm", "Activating combat ship", Logging.White);
                        _States.CurrentArmState = ArmState.ActivateCombatShip;
                    }
                    else if (_States.CurrentArmState == ArmState.SwitchToTransportShip)
                    {
                        Logging.Log("Arm", "Activating transport ship", Logging.White);
                        _States.CurrentArmState = ArmState.ActivateTransportShip;
                    }
                    else
                    {
                        Logging.Log("Arm", "Activating salvage ship", Logging.White);
                        _States.CurrentArmState = ArmState.ActivateSalvageShip;
                    }
                    return;
                    

                case ArmState.ActivateNoobShip:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;

                    if (!Cache.Instance.CloseCargoHold("Arm.ActivateNoobShip")) return;

                    if (Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.RookieShip && 
                        Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Shuttle)
                    {
                        if (!Cache.Instance.OpenShipsHangar("Arm")) return;

                        List<DirectItem> ships = Cache.Instance.ShipHangar.Items;
                        foreach (DirectItem ship in ships.Where(ship => ship.GivenName != null && ship.GroupId == (int)Group.RookieShip || ship.GroupId == (int)Group.Shuttle))
                        {
                            Logging.Log("Arm", "Making [" + ship.GivenName + "] active", Logging.White);
                            ship.ActivateShip();
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.SwitchShipsDelay_seconds);
                            return;
                        }

                        return;
                    }

                    if (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.RookieShip || 
                        Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Shuttle)
                    {
                        Logging.Log("Arm.ActivateNoobShip", "Done", Logging.White);
                        _States.CurrentArmState = ArmState.Cleanup;
                        return;
                    }

                    break;

                case ArmState.ActivateTransportShip:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;
                    
                    if (!Cache.Instance.CloseCargoHold("Arm.ActivateTransportShip")) return;

                    if (string.IsNullOrEmpty(Settings.Instance.TransportShipName))
                    {
                        _States.CurrentArmState = ArmState.NotEnoughAmmo;
                        Logging.Log("Arm.ActivateTransportShip", "Could not find transportshipName in settings!", Logging.Orange);
                        return;
                    }

                    if (Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != Settings.Instance.TransportShipName.ToLower())
                    {
                        if (!Cache.Instance.OpenShipsHangar("Arm")) break;

                        List<DirectItem> ships = Cache.Instance.ShipHangar.Items;
                        foreach (DirectItem ship in ships.Where(ship => ship.GivenName != null && ship.GivenName.ToLower() == Settings.Instance.TransportShipName.ToLower()))
                        {
                            Logging.Log("Arm", "Making [" + ship.GivenName + "] active", Logging.White);
                            ship.ActivateShip();
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.SwitchShipsDelay_seconds);
                            break;
                        }
                        return;
                    }

                    Logging.Log("Arm.ActivateTransportShip", "Done", Logging.White);
                    _States.CurrentArmState = ArmState.Cleanup;
                    return;
                    
                case ArmState.ActivateSalvageShip:
                    string salvageshipName = Settings.Instance.SalvageShipName.ToLower();

                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;
                    
                    if (!Cache.Instance.CloseCargoHold("Arm.ActivateSalvageShip")) return;

                    if (string.IsNullOrEmpty(salvageshipName))
                    {
                        _States.CurrentArmState = ArmState.NotEnoughAmmo;
                        Logging.Log("Arm.ActivateSalvageShip", "Could not find salvageshipName: " + salvageshipName + " in settings!", Logging.Orange);
                        return;
                    }

                    if ((!string.IsNullOrEmpty(salvageshipName) && Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != salvageshipName.ToLower()))
                    {
                        if (!Cache.Instance.OpenShipsHangar("Arm")) break;

                        List<DirectItem> ships = Cache.Instance.ShipHangar.Items;
                        foreach (DirectItem ship in ships.Where(ship => ship.GivenName != null && ship.GivenName.ToLower() == salvageshipName.ToLower()))
                        {
                            Logging.Log("Arm.ActivateSalvageShip", "Making [" + ship.GivenName + "] active", Logging.White);
                            ship.ActivateShip();
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.SwitchShipsDelay_seconds);
                            break;
                        }

                        return;
                    }

                    if (!string.IsNullOrEmpty(salvageshipName) && Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != salvageshipName)
                    {
                        _States.CurrentArmState = ArmState.OpenShipHangar;
                        break;
                    }
                    
                    Logging.Log("Arm.ActivateSalvageShip", "Done", Logging.White);
                    _States.CurrentArmState = ArmState.Cleanup;
                    return;
                    
                case ArmState.ActivateCombatShip:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;

                    string shipNameToUseNow = Settings.Instance.CombatShipName;
                    if (string.IsNullOrEmpty(shipNameToUseNow))
                    {
                        _States.CurrentArmState = ArmState.NotEnoughAmmo;
                        Logging.Log("Arm.ActivateCombatShip", "Could not find CombatShipName: " + shipNameToUseNow + " in settings!", Logging.Orange);
                        return;
                    }

                    if (!Cache.Instance.ArmLoadedCache)
                    {
                        _bringItemMoved = false;
                        if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior)
                        {
                            Cache.Instance.RefreshMissionItems(AgentId);
                        }
                        Cache.Instance.ArmLoadedCache = true;
                    }

                    //
                    // If we have a mission-specific ship defined, switch to it
                    //
                    if (!string.IsNullOrEmpty(Cache.Instance.MissionShip) && TryMissionShip)
                    {
                        shipNameToUseNow = Cache.Instance.MissionShip;
                        TryMissionShip = true;
                    }
                    else
                    {
                        TryMissionShip = false;
                    }

                    //
                    // if we have a ship to use defined and we are not currently in that defined ship. change to that ship
                    //
                    if (Settings.Instance.DebugArm) Logging.Log("Arm.ActivateCombatShip", "shipNameToUseNow = [" + shipNameToUseNow + "]", Logging.Teal);
                    if (Settings.Instance.DebugArm) Logging.Log("Arm.ActivateCombatShip", "Cache.Instance.DirectEve.ActiveShip.GivenName   = [" + Cache.Instance.DirectEve.ActiveShip.GivenName + "]", Logging.Teal);

                    if ((!string.IsNullOrEmpty(shipNameToUseNow) && Cache.Instance.DirectEve.ActiveShip.GivenName != shipNameToUseNow))
                    {
                        if (!Cache.Instance.OpenShipsHangar("Arm.ActivateCombatShip")) break;

                        List<DirectItem> shipsInShipHangar = Cache.Instance.ShipHangar.Items;
                        DirectItem shipToUseNow = shipsInShipHangar.FirstOrDefault(s => s.GivenName != null && s.GivenName.ToLower() == shipNameToUseNow.ToLower());
                        if (shipToUseNow != null)
                        {
                            Logging.Log("Arm.ActivateCombatShip", "Making [" + shipToUseNow.GivenName + "] active", Logging.White);
                            shipToUseNow.ActivateShip();
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.SwitchShipsDelay_seconds);
                            if (TryMissionShip)
                            {
                                UseMissionShip = true;
                            }

                            if (TryMissionShip && !UseMissionShip)
                            {
                                Logging.Log("Arm.ActivateCombatShip", "Unable to find the ship specified in the missionfitting.  Using default combat ship and default fitting.", Logging.Orange);
                                TryMissionShip = false;
                                Cache.Instance.Fitting = Cache.Instance.DefaultFitting;
                            }
                        }
                        else
                        {
                            _States.CurrentArmState = ArmState.NotEnoughAmmo;
                            Logging.Log("Arm.ActivateCombatShip", "Found the following ships:", Logging.White);
                            foreach (DirectItem shipInShipHangar in shipsInShipHangar)
                            {
                                Logging.Log("Arm.ActivateCombatShip", "[" + shipInShipHangar.GivenName + "]", Logging.White);
                            }
                            Logging.Log("Arm.ActivateCombatShip", "Could not find [" + shipNameToUseNow + "] ship!", Logging.Red);
                            return;
                        }
                    }

                    if (TryMissionShip)
                    {
                        UseMissionShip = true;
                    }

                    if (AmmoToLoad.Count == 0 && string.IsNullOrEmpty(Cache.Instance.BringMissionItem))
                    {
                        Logging.Log("Arm", "Done", Logging.White);
                        _States.CurrentArmState = ArmState.Cleanup;
                    }
                    else
                    {
                        _States.CurrentArmState = ArmState.RepairShop;
                    }

                    break;

                case ArmState.RepairShop:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;

                    if (Settings.Instance.UseStationRepair && Cache.Instance.RepairAll)
                    {
                        if (!Cache.Instance.RepairItems("Arm.RepairShop [ALL]")) return; //attempt to use repair facilities if avail in station
                    }
                    else if (Settings.Instance.UseStationRepair && Settings.Instance.UseDrones)
                    {
                        if (!Cache.Instance.RepairDrones("Arm.RepairShop [Drones]")) return; //attempt to use repair facilities if avail in station
                    }

                    _States.CurrentArmState = ArmState.LoadSavedFitting;
                    break;

                case ArmState.LoadSavedFitting:

                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;

                    if (Settings.Instance.UseFittingManager)
                    {
                        //If we are already loading a fitting...
                        if (WaitForFittingToLoad)
                        {
                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadSavedFitting", "if (WaitForFittingToLoad) ", Logging.Teal);

                            if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                            {
                                //we should be done fitting, proceed to the next state
                                if (!Cache.Instance.CloseFittingManager("Arm")) return;

                                WaitForFittingToLoad = false;
                                _States.CurrentArmState = ArmState.MoveDrones;
                                Logging.Log("Arm.LoadFitting", "Done Loading Saved Fitting", Logging.White);
                                return;
                            }

                            if (DateTime.UtcNow.Subtract(_lastArmAction).TotalSeconds > 120)
                            {
                                Logging.Log("Arm.LoadFitting", "Loading Fitting timed out, clearing item locks", Logging.Orange);
                                Cache.Instance.DirectEve.UnlockItems();
                                _lastArmAction = DateTime.UtcNow.AddSeconds(-10);
                                _States.CurrentArmState = ArmState.Begin;
                                break;
                            }

                            //let's wait 10 seconds if we still have locked items
                            Logging.Log("Arm.LoadFitting", "Waiting for fitting. locked items = " + Cache.Instance.DirectEve.GetLockedItems().Count, Logging.White);
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.FittingWindowLoadFittingDelay_seconds);
                            return;
                        }

                        if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior) //|| _States.CurrentQuestorState == QuestorState.BackgroundBehavior)
                        {
                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior)", Logging.Teal);

                            if (!FindDefaultFitting("Arm.LoadSavedFitting")) return;

                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "These are the reasons we would use or not use the fitting manager.(below)", Logging.Teal);
                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "DefaultFittingFound [" + DefaultFittingFound + "]", Logging.Teal);
                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "UseMissionShip [" + UseMissionShip + "]", Logging.Teal);
                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "Cache.Instance.ChangeMissionShipFittings [" + Cache.Instance.ChangeMissionShipFittings + "]", Logging.Teal);
                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "if ((!Settings.Instance.UseFittingManager || !DefaultFittingFound) || (UseMissionShip && !Cache.Instance.ChangeMissionShipFittings)) then do not use fitting manager", Logging.Teal);
                            if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "These are the reasons we would use or not use the fitting manager.(above)", Logging.Teal);

                            if ((!DefaultFittingFound) || (UseMissionShip && !Cache.Instance.ChangeMissionShipFittings))
                            {
                                if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadFitting", "if ((!Settings.Instance.UseFittingManager || !DefaultFittingFound) || (UseMissionShip && !Cache.Instance.ChangeMissionShipFittings))", Logging.Teal);
                                _States.CurrentArmState = ArmState.MoveDrones;
                                return;
                            }

                            //let's check first if we need to change fitting at all
                            Logging.Log("Arm.LoadFitting", "Fitting: " + Cache.Instance.Fitting + " - currentFit: " + Cache.Instance.CurrentFit, Logging.White);
                            if (Cache.Instance.Fitting.Equals(Cache.Instance.CurrentFit))
                            {
                                Logging.Log("Arm.LoadFitting", "Current fit is now correct", Logging.White);
                                _States.CurrentArmState = ArmState.MoveDrones;
                                return;
                            }

                            if (!Cache.Instance.OpenFittingManagerWindow("Arm.LoadFitting")) return;

                            Logging.Log("Arm.LoadFitting", "Looking for fitting " + Cache.Instance.Fitting, Logging.White);

                            foreach (DirectFitting fitting in Cache.Instance.FittingManagerWindow.Fittings)
                            {
                                //ok found it
                                DirectActiveShip CurrentShip = Cache.Instance.DirectEve.ActiveShip;
                                if (Cache.Instance.Fitting.ToLower().Equals(fitting.Name.ToLower()) && fitting.ShipTypeId == CurrentShip.TypeId)
                                {
                                    Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.SwitchShipsDelay_seconds);
                                    Logging.Log("Arm.LoadFitting", "Found fitting [ " + fitting.Name + " ][" + Math.Round(Cache.Instance.NextArmAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + "sec]", Logging.White);

                                    //switch to the requested fitting for the current mission
                                    fitting.Fit();
                                    _lastArmAction = DateTime.UtcNow;
                                    WaitForFittingToLoad = true;
                                    Cache.Instance.CurrentFit = fitting.Name;
                                    CustomFittingFound = true;
                                    return;
                                }

                                continue;
                            }

                            //if we did not find it, we'll set currentfit to default
                            //this should provide backwards compatibility without trying to fit always
                            if (!CustomFittingFound)
                            {
                                if (UseMissionShip)
                                {
                                    Logging.Log("Arm.LoadFitting", "Could not find fitting for this ship typeid.  Using current fitting.", Logging.Orange);
                                    _States.CurrentArmState = ArmState.MoveItems;
                                    return;
                                }

                                Logging.Log("Arm.LoadFitting", "Could not find fitting - switching to default", Logging.Orange);
                                Cache.Instance.Fitting = Cache.Instance.DefaultFitting;
                                _States.CurrentArmState = ArmState.MoveItems;
                                return;
                            }
                        }
                    }

                    if (!Cache.Instance.CloseFittingManager("Arm.LoadFitting")) return;
                    _States.CurrentArmState = ArmState.MoveDrones;
                    break;

                case ArmState.MoveDrones:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;

                    if (!Settings.Instance.UseDrones || 
                        (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Shuttle || 
                         Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Industrial || 
                         Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.TransportShip))
                    {
                        _States.CurrentArmState = ArmState.MoveItems;
                        return;
                    }

                    if (_States.CurrentQuestorState == QuestorState.DedicatedBookmarkSalvagerBehavior
                        //_States.CurrentQuestorState == QuestorState.BackgroundBehavior 
                        //_States.CurrentQuestorState == QuestorState.Mining 
                       )
                    {
                        _States.CurrentArmState = ArmState.Cleanup;
                        return;
                    }

                    if (Cache.Instance.DirectEve.GetLockedItems().Count != 0)
                    {
                        if (DateTime.UtcNow.Subtract(_lastArmAction).TotalSeconds > 120)
                        {
                            Logging.Log("Arm.MoveDrones", "Moving Drones timed out, clearing item locks", Logging.Orange);
                            Cache.Instance.DirectEve.UnlockItems();
                            _lastArmAction = DateTime.UtcNow.AddSeconds(-10);
                            _States.CurrentArmState = ArmState.Begin;
                            return;
                        }
                        return;
                    }

                    if (!Cache.Instance.ReadyAmmoHangar("Arm.MoveDrones")) break;
                    if (!Cache.Instance.OpenItemsHangar("Arm.MoveDrones")) break;
                    if (!Cache.Instance.OpenDroneBay("Arm.MoveDrones")) return;

                    retryCount++;
                    List<DirectItem> ItemHangarDrones = null;
                    List<DirectItem> AmmoHangarDrones = null;
                    List<DirectItem> LootHangarDrones = null;
                    int ItemHangarDronesQuantity = 0;
                    int AmmoHangarDronesQuantity = 0;
                    int LootHangarDronesQuantity = 0;

                    if (Cache.Instance.DirectEve.GetShipsDroneBay().Capacity == Cache.Instance.DirectEve.GetShipsDroneBay().UsedCapacity)
                    {
                        retryCount = 0;
                        Logging.Log("Arm.MoveDrones", "MoveItems", Logging.White);
                        _States.CurrentArmState = ArmState.MoveItems;
                        return;
                    }

                    try
                    {
                        ItemHangarDrones = Cache.Instance.ItemHangar.Items.Where(i => i.TypeId == Settings.Instance.DroneTypeId).ToList();
                        ItemHangarDronesQuantity = ItemHangarDrones.Sum(item => item.Stacksize);
                        if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveDrones", "[" + ItemHangarDronesQuantity + "] Drones available in the ItemHangar", Logging.Debug);

                        if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
                        {
                            AmmoHangarDrones = Cache.Instance.AmmoHangar.Items.Where(i => i.TypeId == Settings.Instance.DroneTypeId).ToList();
                            AmmoHangarDronesQuantity = AmmoHangarDrones.Sum(item => item.Stacksize);
                            if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveDrones", "[" + AmmoHangarDronesQuantity + "] Drones available in the AmmoHangar [" + Settings.Instance.AmmoHangar.ToString(CultureInfo.InvariantCulture) + "]", Logging.Debug);        
                        }

                        if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                        {
                            LootHangarDrones = Cache.Instance.LootHangar.Items.Where(i => i.TypeId == Settings.Instance.DroneTypeId).ToList();
                            LootHangarDronesQuantity = LootHangarDrones.Sum(item => item.Stacksize);
                            if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveDrones", "[" + LootHangarDronesQuantity + "] Drones available in the LootHangar [" + Settings.Instance.LootHangar.ToString(CultureInfo.InvariantCulture) + "]", Logging.Debug);
                        }
                    }
                    catch (Exception exception)
                    {
                        if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveDrones","NON FATAL Exception (this happens normally for loothangar and sometimes itemhangar) [" + exception + "]",Logging.Debug);    
                    }
                    
                    DirectItem drone = null;
                    if (ItemHangarDrones != null && ItemHangarDrones.Any())
                    {
                        //
                        // ItemHangar Drones, this prefers stacks, not singletons
                        //
                        drone = ItemHangarDrones.Where(i => i.Stacksize >= 1).OrderBy(i => i.Quantity).FirstOrDefault();
                        if (drone != null)
                        {
                            Logging.Log("Arm.MoveDrones", "Found [" + ItemHangarDronesQuantity + "] drones in ItemHangar: using a stack of [" + drone.Quantity + "]", Logging.White);        
                        }
                    }

                    if (!string.IsNullOrEmpty(Settings.Instance.AmmoHangar))
                    {
                        if (drone == null && AmmoHangarDrones != null && AmmoHangarDrones.Any())
                        {
                            //
                            // AmmoHangar Drones, this prefers stacks, not singletons
                            //
                            drone = AmmoHangarDrones.Where(i => i.Stacksize >= 1).OrderBy(i => i.Quantity).FirstOrDefault();
                            if (drone != null)
                            {
                                Logging.Log("Arm.MoveDrones", "Found [" + AmmoHangarDronesQuantity + "] drones in AmmoHangar [" + Settings.Instance.AmmoHangar.ToString() + "] using a stack of [" + drone.Quantity + "]", Logging.White);    
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(Settings.Instance.LootHangar))
                    {
                        if (drone == null && LootHangarDrones != null && LootHangarDrones.Any())
                        {
                            //
                            // LootHangar Drones, this prefers stacks, not singletons
                            //
                            drone = LootHangarDrones.Where(i => i.Stacksize >= 1).OrderBy(i => i.Quantity).FirstOrDefault(); 
                            if (drone != null)
                            {
                                Logging.Log("Arm.MoveDrones", "Found [" + LootHangarDronesQuantity + "] drones in LootHangar [" + Settings.Instance.LootHangar.ToString() + "] using a stack of [" + drone.Quantity + "]", Logging.White);
                            }
                        }
                    }

                    if (drone == null || drone.Quantity < -1 || retryCount > 30)
                    {
                        string droneHangarName = string.IsNullOrEmpty(Settings.Instance.AmmoHangar) ? "ItemHangar" : Settings.Instance.AmmoHangar.ToString(CultureInfo.InvariantCulture);
                        Logging.Log("Arm.MoveDrones", "Out of drones with typeID [" + Settings.Instance.DroneTypeId + "] in [" + droneHangarName + "] retryCount [" + retryCount + "]", Logging.Orange);
                        if (drone != null && Settings.Instance.DebugArm)
                        {
                            Logging.Log("Arm.MoveDrones", "drone.IsSingleton [" + drone.IsSingleton + "]", Logging.Orange);
                            Logging.Log("Arm.MoveDrones", "drone.Quantity [" + drone.Quantity + "]", Logging.Orange);
                            Logging.Log("Arm.MoveDrones", "drone.TypeId [" + drone.TypeId + "]", Logging.Orange);
                            Logging.Log("Arm.MoveDrones", "drone.Volume [" + drone.Volume + "]", Logging.Orange);
                            Logging.Log("Arm.MoveDrones", "drone.ItemId [" + drone.ItemId + "]", Logging.Orange);
                        }
                        retryCount = 0;
                        _States.CurrentArmState = ArmState.NotEnoughDrones;
                        return;
                    }

                    double neededDrones = Math.Floor((Cache.Instance.DroneBay.Capacity - Cache.Instance.DroneBay.UsedCapacity) / drone.Volume);
                    Logging.Log("Arm.MoveDrones", "neededDrones: " + neededDrones, Logging.White);

                    if ((int)neededDrones == 0)
                    {
                        retryCount= 0;
                        Logging.Log("Arm.MoveDrones", "MoveItems", Logging.White);
                        _States.CurrentArmState = ArmState.MoveItems;
                        return;
                    }

                    // Move needed drones
                    Logging.Log("Arm.MoveDrones", "Move [ " + (int)Math.Min(neededDrones, drone.Stacksize) + " ] Drones into drone bay", Logging.White);
                    _lastArmAction = DateTime.UtcNow;
                    Cache.Instance.DroneBay.Add(drone, (int)Math.Min(neededDrones, drone.Stacksize));
                    break;

                case ArmState.MoveItems:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction)
                    {
                        if (Settings.Instance.DebugArm) Logging.Log("ArmState.MoveItems", "if (DateTime.UtcNow < Cache.Instance.NextArmAction)) return;", Logging.Teal);
                        return;
                    }

                    if (Settings.Instance.DebugArm) Logging.Log("ArmState.MoveItems", " start if (!Cache.Instance.CloseFittingManager(Arm)) return;", Logging.Teal);
                    if (!Cache.Instance.CloseFittingManager("Arm")) return;
                    if (Settings.Instance.DebugArm) Logging.Log("ArmState.MoveItems", " finish if (!Cache.Instance.CloseFittingManager(Arm)) return;", Logging.Teal);

                    //
                    // Check for locked items if we are already moving items
                    //
                    #region check for item locks

                    if (ItemsAreBeingMoved)
                    {
                        if (Settings.Instance.DebugArm) Logging.Log("ArmState.MoveItems", "if (ItemsAreBeingMoved)", Logging.Teal);

                        if (Cache.Instance.DirectEve.GetLockedItems().Count != 0)
                        {
                            if (DateTime.UtcNow.Subtract(Cache.Instance.NextArmAction).TotalSeconds > 120)
                            {
                                Logging.Log("Unloadloot.MoveItems", "Moving Items timed out, clearing item locks", Logging.Orange);
                                Cache.Instance.DirectEve.UnlockItems();
                                Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(-10);
                                _States.CurrentArmState = ArmState.Begin;
                                return;
                            }

                            if (Settings.Instance.DebugArm) Logging.Log("ArmState.MoveItems", "Waiting for Locks to clear. GetLockedItems().Count [" + Cache.Instance.DirectEve.GetLockedItems().Count + "]", Logging.Teal);
                            return;
                        }
                        ItemsAreBeingMoved = false;
                        return;
                    }
                    #endregion check for item locks

                    //
                    // Bring item
                    //
                    #region Bring Item
                    retryCount++;
                    string bringItem = Cache.Instance.BringMissionItem;
                    if (string.IsNullOrEmpty(bringItem))
                    {
                        _bringItemMoved = true;
                    }

                    if (!_bringItemMoved)
                    {
                        if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveItems", "if (!_missionItemMoved)", Logging.Teal);
                        if (!Cache.Instance.OpenCargoHold("Arm.MoveItems")) break;
                        if (!Cache.Instance.ReadyAmmoHangar("Arm.MoveItems")) break;
                        if (!Cache.Instance.OpenItemsHangar("Arm.MoveItems")) break;

                        IEnumerable<DirectItem> cargoItems = Cache.Instance.CargoHold.Items.Where(i => (i.TypeName ?? string.Empty).ToLower() == bringItem);

                        DirectItem hangarItem = null;
                        if (Cache.Instance.ItemHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem) != null)
                        {
                           hangarItem = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem); 
                        }

                        if (hangarItem == null && !string.IsNullOrEmpty(Settings.Instance.AmmoHangar) && Cache.Instance.AmmoHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem) != null)
                        {
                            hangarItem = Cache.Instance.AmmoHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem);
                        }

                        if (hangarItem == null && !string.IsNullOrEmpty(Settings.Instance.LootHangar) && Cache.Instance.LootHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem) != null)
                        {
                            hangarItem = Cache.Instance.LootHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem);
                        }

                        if (CheckCargoForBringItem)
                        {
                            //
                            // check the local cargo for items and subtract the items in the cargo from the quantity we still need to move to our cargohold
                            //
                            foreach (DirectItem bringItemInCargo in cargoItems)
                            {
                                bringItemQuantity -= bringItemInCargo.Stacksize;
                                if (bringItemQuantity <= 0)
                                {
                                    //
                                    // if we already have enough bringItems in our cargoHold then we are done
                                    //
                                    if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveItems", "BringItem: if (bringItemQuantity <= 0)", Logging.Debug);
                                    _bringItemMoved = true;
                                    retryCount = 0;
                                    CheckCargoForBringItem = false;
                                    return;
                                }

                                continue;
                            }
                            CheckCargoForBringItem = false;
                        }

                        if (hangarItem != null && !string.IsNullOrEmpty(hangarItem.TypeName.ToString(CultureInfo.InvariantCulture)))
                        {
                            if (hangarItem.ItemId <= 0 || hangarItem.Volume == 0.00 || hangarItem.Quantity == 0)
                            {
                                _bringoptionalItemMoved = true;
                                retryCount = 0;
                                return;
                            }

                            int moveBringItemQuantity = Math.Min(hangarItem.Stacksize, bringItemQuantity);
                            moveBringItemQuantity = Math.Max(moveBringItemQuantity, 1);
                            Logging.Log("Arm.MoveItems", "Moving Bring Item [" + hangarItem.TypeName + "] to CargoHold", Logging.White);
                            Cache.Instance.CargoHold.Add(hangarItem, moveBringItemQuantity);

                            bringItemQuantity = bringItemQuantity - moveBringItemQuantity;
                            if (bringItemQuantity <= 0)
                            {
                                if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveItems", "BringItem: if (bringItemQuantity <= 0)", Logging.Debug);
                                _bringItemMoved = true;
                                retryCount = 0;
                                return;
                            }

                            if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveItems", "BringItem: We have [" + bringItemQuantity + "] more bringitem(s) to move", Logging.Debug);
                            ItemsAreBeingMoved = true;
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(4);
                            return;
                        }

                        if (retryCount > 10)
                        {
                            Logging.Log("Arm.MoveItems","We do not have enough of bringitem [" + bringItem + "] in any hangar (we tried itemhangar, ammohangar and loothangar and our cargohold)",Logging.Red);
                            _bringItemMoved = false;
                            _States.CurrentArmState = ArmState.NotEnoughAmmo;
                            Cache.Instance.Paused = true; 
                        }

                        return;
                        
                    }

                    #endregion Bring Item
                    
                    //
                    // Try To Optional Bring item
                    //
                    #region Optional Bring Item

                    retryCount++;
                    string bringOptionalItem = Cache.Instance.BringOptionalMissionItem;
                    if (string.IsNullOrEmpty(bringOptionalItem))
                    {
                        _bringoptionalItemMoved = true;
                    }

                    if (!_bringoptionalItemMoved)
                    {
                        if (Settings.Instance.DebugArm) Logging.Log("Arm.MoveItems", "if (!_optionalMissionItemMoved)", Logging.Teal);
                        if (!Cache.Instance.OpenCargoHold("Arm.MoveItems")) break;
                        if (!Cache.Instance.ReadyAmmoHangar("Arm.MoveItems")) break;
                        if (!Cache.Instance.OpenItemsHangar("Arm.MoveItems")) break;

                        IEnumerable<DirectItem> cargoItems = Cache.Instance.CargoHold.Items.Where(i => (i.TypeName ?? string.Empty).ToLower() == bringOptionalItem);

                        DirectItem hangarItem = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringOptionalItem) ??
                                                Cache.Instance.AmmoHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringOptionalItem) ??
                                                Cache.Instance.LootHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringOptionalItem);

                        if (CheckCargoForOptionalBringItem)
                        {
                            //
                            // check the local cargo for items and subtract the items in the cargo from the quantity we still need to move to our cargohold
                            //
                            foreach (DirectItem bringOptionalItemInCargo in cargoItems)
                            {
                                bringOptionalItemQuantity -= bringOptionalItemInCargo.Quantity;
                                Logging.Log("Arm.MoveItems", "Bring Optional Item: we found [" + bringOptionalItemInCargo + "][" + bringOptionalItemInCargo.Quantity + "] already in the cargo, we need [" + bringOptionalItemQuantity + "] more.", Logging.Teal);
                                if (bringOptionalItemQuantity <= 0)
                                {
                                    //
                                    // if we already have enough bringOptionalItems in our cargoHold then we are done
                                    //
                                    Logging.Log("Arm.MoveItems", "Bring Optional Item: we have all the bring optional items we need.", Logging.Teal);
                                    _bringoptionalItemMoved = true;
                                    retryCount = 0;
                                    CheckCargoForOptionalBringItem = false;
                                    return;
                                }

                                continue;
                            }

                            CheckCargoForOptionalBringItem = false;
                        }

                        if (hangarItem != null && !string.IsNullOrEmpty(hangarItem.TypeName.ToString(CultureInfo.InvariantCulture)))
                        {
                            if (hangarItem.ItemId <= 0 || hangarItem.Volume == 0.00 || hangarItem.Quantity == 0)
                            {
                                Logging.Log("Arm.MoveItems", "Bring Optional Item: Error: retrying", Logging.Teal);
                                _bringoptionalItemMoved = false;
                                return;
                            }

                            int moveOptionalMissionItemQuantity = Math.Min(hangarItem.Stacksize, bringOptionalItemQuantity);
                            moveOptionalMissionItemQuantity = Math.Max(moveOptionalMissionItemQuantity, 1);
                            Logging.Log("Arm.MoveItems", "Moving Bring Optional Item [" + hangarItem.TypeName + "] to CargoHold", Logging.White);
                            Cache.Instance.CargoHold.Add(hangarItem, moveOptionalMissionItemQuantity);

                            bringOptionalItemQuantity -= moveOptionalMissionItemQuantity;
                            if (bringOptionalItemQuantity < 1)
                            {
                                Logging.Log("Arm.MoveItems", "Bring Optional Item: we have all the bring optional items we need. [bringOptionalItemQuantity is now 0]", Logging.Teal);
                                _bringoptionalItemMoved = true;
                                retryCount = 0;
                                return;
                            }

                            ItemsAreBeingMoved = true;
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(4);
                            return;
                        }

                        if (retryCount > 10)
                        {
                            Logging.Log("Arm.MoveItems", "We do not have enough of bringOptionalItem [" + bringOptionalItem + "] in any hangar (we tried itemhangar, ammohangar and loothangar and our cargohold)", Logging.Red);
                            _bringoptionalItemMoved = true;    
                        }

                        return;
                    }
                    #endregion optional bring item

                    //
                    // load ammo
                    //
                    #region load ammo

                    //Civilian Gatling Pulse Laser	3634
                    //Civilian Gatling Autocannon	3636
                    //Civilian Gatling Railgun	3638
                    //Civilian Light Electron Blaster	3640

                    if (Cache.Instance.Modules.Count(i => i.IsTurret && i.MaxCharges == 0) > 0) //civilian guns of all types
                    {
                        Logging.Log("Arm.MoveItems","No ammo needed for civilian guns: done",Logging.White);
                        _States.CurrentArmState = ArmState.Cleanup;
                        return;    
                    }
                    
                    // We must create our own Cache, somehow after changing the fitting the cached data is wrong
                    if (!capsMoved)
                    {
                        DirectContainer modules = Cache.Instance.DirectEve.GetShipsModules();
                        if (modules.Items.Any(m => m.GroupId == (int)Group.CapacitorInjector))
                        {
                            if (!Cache.Instance.OpenCargoHold("Arm.MoveItems")) break;

                            int capsIncargo = 0;
                            foreach (DirectItem cargoItem in Cache.Instance.CargoHold.Items)
                            {
                                if (cargoItem.TypeId != Settings.Instance.CapacitorInjectorScript)
                                    continue;

                                capsIncargo += cargoItem.Quantity;
                                continue;
                            }
                            int capsToLoad = Settings.Instance.CapBoosterToLoad - capsIncargo;
                            if (capsToLoad <= 0)
                            {
                                capsMoved = true;
                                return;
                            }

                            if (capsToLoad > 0)
                            {
                                if (!Cache.Instance.ReadyAmmoHangar("Arm.MoveItems")) break;

                                foreach (DirectItem item in Cache.Instance.AmmoHangar.Items)
                                {
                                    if (item.ItemId <= 0 || item.Volume == 0.00 || item.Quantity == 0)
                                        continue;

                                    if (item.TypeId != Settings.Instance.CapacitorInjectorScript)
                                        continue;

                                    int moveCapQuantity = Math.Min(item.Stacksize, capsToLoad);
                                    Cache.Instance.CargoHold.Add(item, moveCapQuantity);
                                    Logging.Log("Arm.MoveItems", "Moving [" + moveCapQuantity + "] units of Cap  [" + item.TypeName + "] from [ AmmoHangar ] to CargoHold", Logging.White);
                                    return; // you can only move one set of items per frame
                                }

                                Logging.Log("Arm", "Missing [" + capsToLoad + "] units of Cap Booster with TypeId [" + Settings.Instance.CapacitorInjectorScript + "]", Logging.Orange);
                                _States.CurrentArmState = ArmState.NotEnoughAmmo;
                                return;
                            }

                            return;
                        }
                    }

                    if (!Cache.Instance.ReadyAmmoHangar("Arm.MoveItems")) break;
                    if (!Cache.Instance.OpenCargoHold("Arm.MoveItems")) break;

                    //
                    // make sure we actually have something in the list of AmmoToLoad before trying to load ammo.
                    //
                    Ammo CurrentAmmoToLoad = AmmoToLoad.FirstOrDefault();
                    if (CurrentAmmoToLoad == null)
                    {
                        Logging.Log("Arm", "if (CurrentAmmoToLoad == null)", Logging.Debug);
                        _States.CurrentArmState = ArmState.Cleanup;
                        return;
                    }

                    if (CurrentAmmoToLoad.Quantity == 0)
                    {
                        Cache.Instance.MissionAmmo.RemoveAll(a => a.TypeId == CurrentAmmoToLoad.TypeId);
                        AmmoToLoad.RemoveAll(a => a.TypeId == CurrentAmmoToLoad.TypeId);
                        return;
                    }

                    int icount = 0;
                    foreach (DirectItem itemfound in Cache.Instance.AmmoHangar.Items)
                    {
                        if (itemfound.GroupName == "Advanced Autocannon Ammo")
                        {
                            Logging.Log("Arm.MoveItems","Found: Name [" + itemfound.TypeName + "] Quantity [" + itemfound.Quantity + "] in the AmmoHangar",Logging.Red);

                        }

                        continue;
                    }

                    IEnumerable<DirectItem> AmmoHangarItems = Cache.Instance.AmmoHangar.Items.Where(i => i.TypeId == CurrentAmmoToLoad.TypeId).OrderBy(i => i.IsSingleton).ThenBy(i => i.Quantity);
                    IEnumerable<DirectItem> AmmoItems = AmmoHangarItems;
                    
                    if (Settings.Instance.DebugArm) Logging.Log("Arm", "Ammohangar has [" + AmmoHangarItems.Count() + "] items with the right typeID [" + CurrentAmmoToLoad.TypeId + "] for this ammoType. MoveAmmo will use AmmoHangar", Logging.Debug);
                    if (!AmmoHangarItems.Any())
                    {
                        IEnumerable<DirectItem> ItemHangarItems = Cache.Instance.ItemHangar.Items.Where(i => i.TypeId == CurrentAmmoToLoad.TypeId).OrderBy(i => i.IsSingleton).ThenBy(i => i.Quantity);
                        AmmoItems = ItemHangarItems;
                        if (Settings.Instance.DebugArm) Logging.Log("Arm", "Itemhangar has [" + ItemHangarItems.Count() + "] items with the right typeID [" + CurrentAmmoToLoad.TypeId + "] for this ammoType. MoveAmmo will use ItemHangar", Logging.Debug);   
                    }

                    foreach (DirectItem item in AmmoItems)
                    {
                        int moveAmmoQuantity = Math.Min(item.Stacksize, CurrentAmmoToLoad.Quantity);
                        moveAmmoQuantity = Math.Max(moveAmmoQuantity, 1);
                        Logging.Log("Arm.MoveItems", "Moving [" + moveAmmoQuantity + "] units of Ammo  [" + item.TypeName + "] from [ AmmoHangar ] to CargoHold", Logging.White);
                        Cache.Instance.CargoHold.Add(item, moveAmmoQuantity);
                        CurrentAmmoToLoad.Quantity -= moveAmmoQuantity;
                        
                        return; //you can only move one set of items per frame.
                    }

                    if (AmmoToLoad.Any())
                    {
                        foreach (Ammo ammo in AmmoToLoad)
                        {
                            Logging.Log("Arm", "Missing [" + ammo.Quantity + "] units of ammo: [ " + ammo.Description + " ] with TypeId [" + ammo.TypeId + "]", Logging.Orange);
                        }

                        _States.CurrentArmState = ArmState.NotEnoughAmmo;
                        return;
                    }

                    Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.WaitforItemstoMove_seconds);
                    Logging.Log("Arm.MoveItems", "Waiting for items", Logging.White);
                    _States.CurrentArmState = ArmState.WaitForItems;
                    return;
                    
                    #endregion move ammo

                #region WaitForItems
                case ArmState.WaitForItems:

                    // Wait 5 seconds after moving
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction)
                        break;

                    if (!Cache.Instance.OpenCargoHold("Arm.WaitForItems")) return;

                    if (Cache.Instance.CargoHold.Items.Count == 0) return;

                    if (Settings.Instance.UseDrones && (Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Shuttle && Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Industrial && Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.TransportShip))
                    {
                        // Close the drone bay, its not required in space.
                        //if (Cache.Instance.DroneBay.IsReady) //why is not .isready and .isvalid working at the moment? 4/2012
                        Cache.Instance.CloseDroneBay("Arm.WaitForItems");
                    }

                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        Logging.Log("Arm", "Done", Logging.White);

                        if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior)
                        {
                            //reload the ammo setting for combat
                            try
                            {
                                DirectAgentMission mission = Cache.Instance.DirectEve.AgentMissions.FirstOrDefault(m => m.AgentId == AgentId);
                                if (mission == null) return;

                                Cache.Instance.SetmissionXmlPath(Cache.Instance.FilterPath(mission.Name));

                                XDocument missionXml = XDocument.Load(Cache.Instance.MissionXmlPath);
                                Cache.Instance.MissionAmmo = new List<Ammo>();
                                if (missionXml.Root != null)
                                {
                                    XElement ammoTypes = missionXml.Root.Element("missionammo");
                                    if (ammoTypes != null)
                                    {
                                        foreach (XElement ammo in ammoTypes.Elements("ammo"))
                                        {
                                            Cache.Instance.MissionAmmo.Add(new Ammo(ammo));
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logging.Log("Arms.WaitForItems", "Unable to load missionammo from mission XML for: [" + Cache.Instance.MissionName + "], " + ex.Message, Logging.Orange);
                                Cache.Instance.MissionAmmo = new List<Ammo>();
                            }
                        }

                        _States.CurrentArmState = ArmState.Cleanup;
                        return;
                    }

                    // Note, there's no unlock here as we *always* want our ammo!
                    break;

                #endregion WaitForItems
            }
        }
    }
}