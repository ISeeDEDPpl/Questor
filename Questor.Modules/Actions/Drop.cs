namespace Questor.Modules.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;

    public class Drop
    {
        public int Item { get; set; }

        public int Unit { get; set; }

        public string DestinationHangarName { get; set; }
        public DirectContainer dropHangar = null;

        private DateTime _lastAction;

        public void ProcessState()
        {
            if (!Cache.Instance.InStation)
                return;

            if (Cache.Instance.InSpace)
                return;

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(10)) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return;

            switch (_States.CurrentDropState)
            {
                case DropState.Idle:
                case DropState.Done:
                    break;

                case DropState.Begin:
                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: Begin", Logging.Debug);
                    
                    _States.CurrentDropState = DropState.ReadyItemhangar;
                    break;

                case DropState.ReadyItemhangar:
                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: ReadyItemhangar", Logging.Debug);
                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 2) return;
                    dropHangar = Cache.Instance.ItemHangar;
                    
                    if (DestinationHangarName == "Local Hangar")
                    {
                        if (!Cache.Instance.OpenItemsHangar("Drop")) return;
                        dropHangar = Cache.Instance.ItemHangar;
                    }
                    else if (DestinationHangarName == "Ship Hangar")
                    {
                        if (!Cache.Instance.OpenShipsHangar("Drop")) return;
                        dropHangar = Cache.Instance.ShipHangar;
                    }
                    else
                    {
                        if (dropHangar != null && dropHangar.Window == null)
                        {
                            // No, command it to open
                            //Cache.Instance.DirectEve.OpenCorporationHangar();
                            break;
                        }

                        if (dropHangar != null && !dropHangar.Window.IsReady) return;
                    }

                    Logging.Log("Drop", "Opening Hangar", Logging.White);
                    _States.CurrentDropState = DropState.OpenCargo;
                    break;

                case DropState.OpenCargo:
                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: OpenCargo", Logging.Debug);
                    
                    if (!Cache.Instance.OpenCargoHold("Drop")) return;

                    Logging.Log("Drop", "Opening Cargo Hold", Logging.White);
                    _States.CurrentDropState = Item == 00 ? DropState.AllItems : DropState.MoveItems;

                    break;

                case DropState.MoveItems:

                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: MoveItems", Logging.Debug);
                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 2) return;
                    if (!Cache.Instance.OpenCargoHold("Drop")) return;

                    DirectItem dropItem;

                    if (Unit == 00)
                    {
                        try
                        {
                            if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Item TypeID is [" + Item + "]",Logging.Debug);
                            
                            int x = 1;
                            foreach (DirectItem ItemTest in Cache.Instance.CargoHold.Items)
                            {
                                if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "[" + x + "] ItemName: [" + ItemTest.TypeName + "]", Logging.Debug);
                            }

                            if (Cache.Instance.CargoHold.Items.Any(i => i.TypeId == Item))
                            {
                                dropItem = Cache.Instance.CargoHold.Items.FirstOrDefault(i => i.TypeId == Item);
                                if (dropItem != null)
                                {
                                    if (Settings.Instance.DebugQuestorManager) Logging.Log("DropItem", "dropItem = [" + dropItem.TypeName + "]", Logging.Debug);
                                    if (dropHangar != null) dropHangar.Add(dropItem, dropItem.Quantity);
                                    Logging.Log("Drop", "Moving all the items", Logging.White);
                                    _lastAction = DateTime.UtcNow;
                                    _States.CurrentDropState = DropState.WaitForMove;
                                    return;
                                }
                            }
                            else
                            {
                                if (Settings.Instance.DebugQuestorManager) Logging.Log("DropItem", "missing item with typeID of [" + Item + "]", Logging.Debug);
                            }
                        }
                        catch (Exception exception)
                        {
                            Logging.Log("Drop","MoveItems (all): Exception [" + exception + "]",Logging.Debug);
                        }
                        return;
                    }

                    try
                    {
                        if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Item = [" + Item + "]", Logging.Debug);
                        dropItem = Cache.Instance.CargoHold.Items.FirstOrDefault(i => (i.TypeId == Item));
                        if (dropItem != null)
                        {
                            if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Unit = [" + Unit + "]", Logging.Debug);
                            
                            if (dropHangar != null) dropHangar.Add(dropItem, Unit);
                            Logging.Log("Drop", "Moving item", Logging.White);
                            _lastAction = DateTime.UtcNow;
                            _States.CurrentDropState = DropState.WaitForMove;
                            return;
                        }
                    }
                    catch (Exception exception)
                    {
                        Logging.Log("Drop", "MoveItems: Exception [" + exception + "]", Logging.Debug);
                    }
                    
                    break;

                case DropState.AllItems:
                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: AllItems", Logging.Debug);
                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 2) return;

                    List<DirectItem> allItem = Cache.Instance.CargoHold.Items;
                    if (allItem != null)
                    {
                        if (dropHangar != null) dropHangar.Add(allItem);
                        Logging.Log("Drop", "Moving item", Logging.White);
                        _lastAction = DateTime.UtcNow;
                        _States.CurrentDropState = DropState.WaitForMove;
                        return;
                    }

                    break;

                case DropState.WaitForMove:
                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: WaitForMove", Logging.Debug);
                    
                    // Wait 2 seconds after moving
                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 2) return;

                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        _States.CurrentDropState = DropState.StackItemsHangar;
                        return;
                    }

                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds > 60)
                    {
                        Logging.Log("Drop", "Moving items timed out, clearing item locks", Logging.White);
                        Cache.Instance.DirectEve.UnlockItems();

                        _States.CurrentDropState = DropState.StackItemsHangar;
                        return;
                    }
                    break;

                case DropState.StackItemsHangar:
                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: StackItemsHangar", Logging.Debug);
                    // Do not stack until 5 seconds after the cargo has cleared
                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 5) return;
                    
                    // Stack everything
                    if (dropHangar != null)
                    {
                        Logging.Log("Drop", "Stacking items", Logging.White);
                        dropHangar.StackAll();
                        _lastAction = DateTime.UtcNow;
                        _States.CurrentDropState = DropState.WaitForStacking;
                        return;
                    }
                    break;

                case DropState.WaitForStacking:
                    if (Settings.Instance.DebugQuestorManager) Logging.Log("Drop", "Entered: WaitForStacking", Logging.Debug);
                    // Wait 5 seconds after stacking
                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 5) return;

                    if (Cache.Instance.DirectEve.GetLockedItems().Count == 0)
                    {
                        Logging.Log("Drop", "Done", Logging.White);
                        _States.CurrentDropState = DropState.Done;
                        return;
                    }

                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds > 120)
                    {
                        Logging.Log("Drop", "Stacking items timed out, clearing item locks", Logging.White);
                        Cache.Instance.DirectEve.UnlockItems();

                        Logging.Log("Drop", "Done", Logging.White);
                        _States.CurrentDropState = DropState.Done;
                        return;
                    }
                    break;
            }
        }
    }
}