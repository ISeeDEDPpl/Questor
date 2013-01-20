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
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < Time.Instance.QuestorPulse_milliseconds) //default: 1500ms
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
                    retryCount = 0;

                    //CheckCargoForAmmo = true;

                    _States.CurrentArmState = ArmState.OpenShipHangar;
                    _States.CurrentCombatState = CombatState.Idle;
                    Cache.Instance.NextArmAction = DateTime.UtcNow;
                    break;

                case ArmState.OpenShipHangar:
                case ArmState.SwitchToTransportShip:
                case ArmState.SwitchToSalvageShip:
                    if (DateTime.UtcNow > Cache.Instance.NextArmAction) //default 10 seconds
                    {
                        if (!Cache.Instance.OpenShipsHangar("Arm")) break;

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
                        break;
                    }
                    break;

                case ArmState.ActivateNoobShip:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction) return;

                    if (!Cache.Instance.CloseCargoHold("Arm.ActivateNoobShip")) return;

                    if (Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.RookieShip && 
                        Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Shuttle)
                    {
                        if (!Cache.Instance.OpenShipsHangar("Arm")) break;

                        List<DirectItem> ships = Cache.Instance.ShipHangar.Items;
                        foreach (DirectItem ship in ships.Where(ship => ship.GivenName != null && ship.GroupId == (int)Group.RookieShip || ship.GroupId == (int)Group.Shuttle))
                        {
                            Logging.Log("Arm", "Making [" + ship.GivenName + "] active", Logging.White);
                            ship.ActivateShip();
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.SwitchShipsDelay_seconds);
                            break;
                        }
                        return;
                    }

                    if (DateTime.UtcNow > Cache.Instance.NextArmAction) //default 7 seconds
                    {
                        if (Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.RookieShip || 
                            Cache.Instance.DirectEve.ActiveShip.GroupId == (int)Group.Shuttle)
                        {
                            Logging.Log("Arm.ActivateTransportShip", "Done", Logging.White);
                            _States.CurrentArmState = ArmState.Cleanup;
                            return;
                        }
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

                    if (DateTime.UtcNow > Cache.Instance.NextArmAction) //default 7 seconds
                    {
                        if (Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() == Settings.Instance.TransportShipName.ToLower())
                        {
                            Logging.Log("Arm.ActivateTransportShip", "Done", Logging.White);
                            _States.CurrentArmState = ArmState.Cleanup;
                            return;
                        }
                    }

                    break;

                case ArmState.ActivateSalvageShip:
                    string salvageshipName = Settings.Instance.SalvageShipName.ToLower();

                    if (DateTime.UtcNow > Cache.Instance.NextArmAction) //default 10 seconds
                    {
                        if (!Cache.Instance.CloseCargoHold("Arm.ActivateSalvageShip")) return;

                        if (string.IsNullOrEmpty(salvageshipName))
                        {
                            _States.CurrentArmState = ArmState.NotEnoughAmmo;
                            Logging.Log("Arm.ActivateSalvageShip", "Could not find salvageshipName: " + salvageshipName + " in settings!", Logging.Orange);
                            return;
                        }

                        if ((!string.IsNullOrEmpty(salvageshipName) && Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != salvageshipName.ToLower()))
                        {
                            if (DateTime.UtcNow > Cache.Instance.NextArmAction)
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

                            return;
                        }

                        if (DateTime.UtcNow > Cache.Instance.NextArmAction && (!string.IsNullOrEmpty(salvageshipName) && Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != salvageshipName))
                        {
                            _States.CurrentArmState = ArmState.OpenShipHangar;
                            break;
                        }

                        if (DateTime.UtcNow > Cache.Instance.NextArmAction)
                        {
                            Logging.Log("Arm.ActivateSalvageShip", "Done", Logging.White);
                            _States.CurrentArmState = ArmState.Cleanup;
                            return;
                        }
                    }
                    break;

                case ArmState.ActivateCombatShip:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction)
                        return;

                    //if (!Cache.Instance.CloseCargoHold("Arm.ActivateCombatShip")) return;

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

                case ArmState.LoadSavedFitting:

                    if (DateTime.UtcNow < Cache.Instance.NextArmAction)
                        return;

                    if (Settings.Instance.UseFittingManager)
                    {
                        //if (Settings.Instance.DebugFittingMgr) Logging.Log("Arm.LoadSavedFitting", "You Are Here . ", Logging.Teal);

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

                        if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior)
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
                            /*  Deactivate this for now, so Fitting specific items will get refiled (Cap Booster for example)
                            Logging.Log("Arm.LoadFitting", "Fitting: " + Cache.Instance.Fitting + " - currentFit: " + Cache.Instance.CurrentFit, Logging.White);
                            if (Cache.Instance.Fitting.Equals(Cache.Instance.CurrentFit))
                            {
                                Logging.Log("Arm.LoadFitting", "Current fit is now correct", Logging.White);
                                _States.CurrentArmState = ArmState.MoveDrones;
                                return;
                            }
                             */ 

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
                                    break;
                                }
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
                                return;
                            }
                        }
                    }

                    if (!Cache.Instance.CloseFittingManager("Arm.LoadFitting")) return;
                    _States.CurrentArmState = ArmState.MoveDrones;
                    break;

                case ArmState.RepairShop:
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction)
                        return;

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

                case ArmState.MoveDrones:

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

                    DirectItem drone = Cache.Instance.ItemHangar.Items.OrderBy(i => i.IsSingleton).ThenBy(i => i.Quantity).FirstOrDefault(i => i.TypeId == Settings.Instance.DroneTypeId) ??
                                       Cache.Instance.AmmoHangar.Items.OrderBy(i => i.IsSingleton).ThenBy(i => i.Quantity).FirstOrDefault(i => i.TypeId == Settings.Instance.DroneTypeId) ??
                                       Cache.Instance.LootHangar.Items.OrderBy(i => i.IsSingleton).ThenBy(i => i.Quantity).FirstOrDefault(i => i.TypeId == Settings.Instance.DroneTypeId);

                    if (drone == null || drone.Stacksize < 1)
                    {
                        string ammoHangarName = string.IsNullOrEmpty(Settings.Instance.AmmoHangar) ? "ItemHangar" : Settings.Instance.AmmoHangar.ToString(CultureInfo.InvariantCulture);
                        Logging.Log("Arm.MoveDrones", "Out of drones with typeID [" + Settings.Instance.DroneTypeId + "] in [" + ammoHangarName + "]", Logging.Orange);
                        _States.CurrentArmState = ArmState.NotEnoughDrones;
                        return;
                    }

                    if (!Cache.Instance.OpenDroneBay("Arm.MoveDrones")) return;

                    double neededDrones = Math.Floor((Cache.Instance.DroneBay.Capacity - Cache.Instance.DroneBay.UsedCapacity) / drone.Volume);
                    Logging.Log("Arm.MoveDrones", "neededDrones: " + neededDrones, Logging.White);

                    if ((int)neededDrones == 0)
                    {
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

                        DirectItem hangarItem = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem) ??
                                                Cache.Instance.AmmoHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem) ??
                                                Cache.Instance.LootHangar.Items.FirstOrDefault(i => (i.TypeName ?? string.Empty).ToLower() == bringItem);

                        if (CheckCargoForBringItem)
                        {
                            //
                            // check the local cargo for items and subtract the items in the cargo from the quantity we still need to move to our cargohold
                            //
                            foreach (DirectItem bringItemInCargo in cargoItems)
                            {
                                bringItemQuantity -= bringItemInCargo.Quantity;
                                if (bringItemQuantity <= 0)
                                {
                                    //
                                    // if we already have enough bringItems in our cargoHold then we are done
                                    //
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

                            int moveBringItemQuantity = Math.Min(hangarItem.Quantity, bringItemQuantity);
                            moveBringItemQuantity = Math.Max(moveBringItemQuantity, 1);
                            Logging.Log("Arm.MoveItems", "Moving Bring Item [" + hangarItem.TypeName + "] to CargoHold", Logging.White);
                            Cache.Instance.CargoHold.Add(hangarItem, moveBringItemQuantity);

                            bringItemQuantity = bringItemQuantity - moveBringItemQuantity;
                            if (bringItemQuantity <= 0)
                            {
                                _bringItemMoved = true;
                                retryCount = 0;
                                return;
                            }
                            ItemsAreBeingMoved = true;
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(1);
                            return;
                        }

                        if (retryCount > 30)
                        {
                            _bringItemMoved = false;
                            _States.CurrentArmState = ArmState.NotEnoughAmmo;
                            Cache.Instance.Paused = true; 
                        }
                        retryCount++;
                        return;
                        
                    }

                    #endregion Bring Item
                    
                    //
                    // Try To Optional Bring item
                    //
                    #region Optional Bring Item

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

                            int moveOptionalMissionItemQuantity = Math.Min(hangarItem.Quantity, bringOptionalItemQuantity);
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
                            Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(1);
                            return;
                        }

                        if (retryCount > 30)
                        {
                            _bringoptionalItemMoved = true;    
                        }
                        retryCount++;
                        return;
                    }
                    #endregion optional bring item

                    //
                    // load ammo
                    //
                    #region load ammo

                    bool ammoMoved = false;
                    if (Cache.Instance.MissionAmmo.Count() != 0)
                    {
                        AmmoToLoad = new List<Ammo>(Cache.Instance.MissionAmmo);
                    }

                    if (!Cache.Instance.OpenCargoHold("Arm.MoveItems")) break;
                    if (!Cache.Instance.ReadyAmmoHangar("Arm.MoveItems")) break;

                    //IEnumerable<DirectItem> AmmoInCargo = Cache.Instance.CargoHold.Items.Where(i => (i.TypeName ?? string.Empty).ToLower() == bringItem);

                    //
                    // check the local cargo for items and subtract the items in the cargo from the quantity we still need to move to our cargohold
                    //
                    //foreach (DirectItem bringItemInCargo in AmmoInCargo)
                    //{
                    //    AmmoFlavorFound -= bringItemInCargo.Quantity;
                    //    if (AmmoFlavorFound <= 0)
                    //    {
                    //        //
                    //        // if we already have enough bringItems in our cargoHold then we are done
                    //        //
                    //        Cache.Instance.MissionAmmo.RemoveAll(a => a.TypeId == item.TypeId);
                    //        AmmoToLoad.RemoveAll(a => a.TypeId == item.TypeId);
                    //        break;
                    //    }
                    //}

                    foreach (DirectItem item in Cache.Instance.AmmoHangar.Items.OrderBy(i => i.IsSingleton).ThenBy(i => i.Quantity))
                    {
                        if (item.ItemId <= 0 || item.Volume == 0.00 || item.Quantity == 0)
                            continue;

                        Ammo ammo = AmmoToLoad.FirstOrDefault(a => a.TypeId == item.TypeId);
                        if (ammo == null || ammo.Quantity == 0)
                            continue;

                        int moveAmmoQuantity = Math.Min(item.Quantity, ammo.Quantity);
                        moveAmmoQuantity = Math.Max(moveAmmoQuantity, 1);
                        Cache.Instance.CargoHold.Add(item, moveAmmoQuantity);

                        Logging.Log("Arm.MoveItems", "Moving [" + moveAmmoQuantity + "] units of Ammo  [" + item.TypeName + "] from [ AmmoHangar ] to CargoHold", Logging.White);

                        ammo.Quantity -= moveAmmoQuantity;
                        if (ammo.Quantity <= 0)
                        {
                            Cache.Instance.MissionAmmo.RemoveAll(a => a.TypeId == item.TypeId);
                            AmmoToLoad.RemoveAll(a => a.TypeId == item.TypeId);
                        }
                        ammoMoved = true;
                        break;
                    }

                    if (AmmoToLoad.Count == 0)
                    {
                        Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Time.Instance.WaitforItemstoMove_seconds);

                        Logging.Log("Arm.MoveItems", "Waiting for items", Logging.White);
                        _States.CurrentArmState = ArmState.WaitForItems;
                    }
                    else if (!ammoMoved)
                    {
                        if (Settings.Instance.DebugArm) Logging.Log("ArmState.MoveItems", "else if (!ammoMoved)", Logging.Teal);

                        if (AmmoToLoad.Count > 0)
                        {
                            foreach (Ammo ammo in AmmoToLoad)
                            {
                                Logging.Log("Arm", "Missing [" + ammo.Quantity + "] units of ammo: [ " + ammo.Description + " ] with TypeId [" + ammo.TypeId + "]", Logging.Orange);
                            }
                        }

                        _States.CurrentArmState = ArmState.NotEnoughAmmo;
                    }
                    #endregion move ammo

                    break;

                case ArmState.WaitForItems:

                    // Wait 5 seconds after moving
                    if (DateTime.UtcNow < Cache.Instance.NextArmAction)
                        break;

                    if (!Cache.Instance.OpenCargoHold("Arm.WaitForItems")) return;

                    if (Cache.Instance.CargoHold.Items.Count == 0) return;

                    if (Settings.Instance.UseDrones && (Cache.Instance.DirectEve.ActiveShip.GroupId != 31 && Cache.Instance.DirectEve.ActiveShip.GroupId != 28 && Cache.Instance.DirectEve.ActiveShip.GroupId != 380))
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
            }
        }
    }
}