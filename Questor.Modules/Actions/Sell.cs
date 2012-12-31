namespace Questor.Modules.Actions
{
    using System;
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.States;

    public class Sell
    {
        public int Item { get; set; }

        public int Unit { get; set; }

        private DateTime _lastAction;

        public void ProcessState()
        {
            if (!Cache.Instance.InStation)
                return;

            if (Cache.Instance.InSpace)
                return;

            if (DateTime.UtcNow < Cache.Instance.LastInSpace.AddSeconds(20)) // we wait 20 seconds after we last thought we were in space before trying to do anything in station
                return;

            //DirectMarketWindow marketWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketWindow>().FirstOrDefault();
            DirectMarketActionWindow sellWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketActionWindow>().FirstOrDefault(w => w.IsSellAction);

            switch (_States.CurrentSellState)
            {
                case SellState.Idle:
                case SellState.Done:
                    break;

                case SellState.Begin:
                    _States.CurrentSellState = SellState.StartQuickSell;
                    break;

                case SellState.StartQuickSell:

                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 1)
                        break;
                    _lastAction = DateTime.UtcNow;

                    if (!Cache.Instance.OpenItemsHangar("Sell")) break;

                    DirectItem directItem = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => (i.TypeId == Item));
                    if (directItem == null)
                    {
                        Logging.Log("Sell", "Item " + Item + " no longer exists in the hanger", Logging.White);
                        break;
                    }

                    // Update Quantity
                    if (Unit == 00)
                        Unit = directItem.Quantity;

                    Logging.Log("Sell", "Starting QuickSell for " + Item, Logging.White);
                    if (!directItem.QuickSell())
                    {
                        _lastAction = DateTime.UtcNow.AddSeconds(-5);

                        Logging.Log("Sell", "QuickSell failed for " + Item + ", retrying in 5 seconds", Logging.White);
                        break;
                    }

                    _States.CurrentSellState = SellState.WaitForSellWindow;
                    break;

                case SellState.WaitForSellWindow:

                    //if (sellWindow == null || !sellWindow.IsReady || sellWindow.Item.ItemId != Item)
                    //    break;

                    // Mark as new execution
                    _lastAction = DateTime.UtcNow;

                    Logging.Log("Sell", "Inspecting sell order for " + Item, Logging.White);
                    _States.CurrentSellState = SellState.InspectOrder;
                    break;

                case SellState.InspectOrder:
                    // Let the order window stay open for 2 seconds
                    if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 2)
                        break;
                    if (sellWindow != null)
                    {
                        if ((!sellWindow.OrderId.HasValue || !sellWindow.Price.HasValue || !sellWindow.RemainingVolume.HasValue))
                        {
                            Logging.Log("Sell", "No order available for " + Item, Logging.White);

                            sellWindow.Cancel();
                            _States.CurrentSellState = SellState.WaitingToFinishQuickSell;
                            break;
                        }

                        double price = sellWindow.Price.Value;

                        Logging.Log("Sell", "Selling " + Unit + " of " + Item + " [Sell price: " + (price * Unit).ToString("#,##0.00") + "]", Logging.White);
                        sellWindow.Accept();
                        _States.CurrentSellState = SellState.WaitingToFinishQuickSell;
                    }
                    _lastAction = DateTime.UtcNow;
                    break;

                case SellState.WaitingToFinishQuickSell:
                    if (sellWindow == null || !sellWindow.IsReady || sellWindow.Item.ItemId != Item)
                    {
                        DirectWindow modal = Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.IsModal);
                        if (modal != null)
                            modal.Close();

                        _States.CurrentSellState = SellState.Done;
                        break;
                    }
                    break;
            }
        }
    }
}