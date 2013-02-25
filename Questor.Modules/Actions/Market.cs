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
    using System.Xml.Linq;
    using DirectEve;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;
    
    public class Market
    {
        //private DateTime _lastPulse;

        private static Dictionary<int, InvTypeMarket> InvTypesById { get; set; }

        public static List<ItemCacheMarket> Items { get; set; }

        private static List<ItemCacheMarket> ItemsToSell { get; set; }

        private static List<ItemCacheMarket> ItemsToRefine { get; set; }

        public static string InvTypesXMLData
        {
            get
            {
                return Settings.Instance.Path + "\\InvTypes.xml";
            }
        }

        public Market()
        {
            Items = new List<ItemCacheMarket>();
            ItemsToSell = new List<ItemCacheMarket>();
            ItemsToRefine = new List<ItemCacheMarket>();

            InvTypesById = new Dictionary<int, InvTypeMarket>();

            Logging.Log("Market", "Load InvTypes.xml from [" + InvTypesXMLData + "]", Logging.White);
            try
            {
                XDocument invTypes = XDocument.Load(InvTypesXMLData);
                if (invTypes.Root != null)
                {
                    foreach (XElement element in invTypes.Root.Elements("invtype"))
                    {
                        InvTypesById.Add((int)element.Attribute("id"), new InvTypeMarket(element));
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Log("Market", "Unable to load [" + InvTypesXMLData + "] exception was [" + ex.Message + "]", Logging.Orange);
            }
        }
        
        private static InvTypeMarket _currentMineral;
        private static ItemCacheMarket _currentItem;
        private static DateTime _lastExecute = DateTime.MinValue;

        public static bool CheckMineralPrices(string module, bool refine)
        {
            try
            {
                if (refine)
                {
                    _currentMineral = InvTypesById.Values.FirstOrDefault(i => i.ReprocessValue.HasValue && i.LastUpdate < DateTime.UtcNow.AddDays(-7));
                }
                else
                {
                    _currentMineral = InvTypesById.Values.FirstOrDefault(i => i.Id != (int)TypeID.Chalcopyrite && i.GroupId == (int)Group.Minerals && i.LastUpdate < DateTime.UtcNow.AddHours(-4));
                }

            }
            catch (Exception exception)
            {
                Logging.Log(module,"CheckMineralPrices: exception [" + exception + "]",Logging.Debug);
            }
            
            if (_currentMineral == null)
            {
                //
                // we must have already gone through all the minerals or we would not have hit null (right?)
                //
                if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > Time.Instance.Marketlookupdelay_seconds)
                {
                    if (!Cache.Instance.CloseMarket(module)) return false;
                    return true;
                }

                return false;
            }

            if (!Cache.Instance.OpenMarket(module)) return false;

            //
            // If the market window is not yet displaying the info for the currentMineral, load the right data
            //
            if (Cache.Instance.MarketWindow.DetailTypeId != _currentMineral.Id)
            {
                if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds < Time.Instance.Marketlookupdelay_seconds)
                {
                    return false;
                }

                Logging.Log(module, "Loading orders for " + _currentMineral.Name, Logging.White);

                Cache.Instance.MarketWindow.LoadTypeId(_currentMineral.Id);
                _lastExecute = DateTime.UtcNow;
                return false;
            }

            List<DirectOrder> orders;
            double totalAmount;
            double amount = 0;
            Double value = 0;
            Double count = 0;
            //
            // buy orders...
            //
            if (Cache.Instance.MarketWindow.BuyOrders.All(o => o.StationId != Cache.Instance.DirectEve.Session.StationId))
            {
                //
                // no buy orders found
                //
                _currentMineral.LastUpdate = DateTime.UtcNow;

                Logging.Log(module, "No buy orders found for " + _currentMineral.Name, Logging.White);
                //return false; // we still need to check sell orders.
            }
            else //we found some buy orders...
            {
                // Take top 5 orders, average the buy price and consider that median-buy (it's not really median buy but its what we want)
                //_currentMineral.MedianBuy = marketWindow.BuyOrders.Where(o => o.StationId == DirectEve.Session.StationId).OrderByDescending(o => o.Price).Take(5).Average(o => o.Price);

                // Take top 1% orders and count median-buy price (no set of bots covers more than 1% Jita orders anyway)
                orders = Cache.Instance.MarketWindow.BuyOrders.Where(o => o.StationId == Cache.Instance.DirectEve.Session.StationId && o.MinimumVolume == 1).OrderByDescending(o => o.Price).ToList();
                totalAmount = orders.Sum(o => (double)o.VolumeRemaining);
                amount = 0;
                value = 0;
                count = 0;
                for (int i = 0; i < orders.Count(); i++)
                {
                    amount += orders[i].VolumeRemaining;
                    value += orders[i].VolumeRemaining * orders[i].Price;
                    count++;

                    if (Settings.Instance.DebugValuedump) Logging.Log(module, _currentMineral.Name + " " + count + ": " + orders[i].VolumeRemaining.ToString("#,##0") + " items @ " + orders[i].Price, Logging.Debug);
                    if (amount / totalAmount > 0.01)
                    {
                        break;
                    }
                }

                _currentMineral.MedianBuy = value / amount;
                Logging.Log(module, "Average buy price for " + _currentMineral.Name + " is " + _currentMineral.MedianBuy.Value.ToString("#,##0.00") + " (" + count + " / " + orders.Count() + " orders, " + amount.ToString("#,##0") + " / " + totalAmount.ToString("#,##0") + " items)", Logging.White);
            }

            //
            // Sell orders
            //
            if (Cache.Instance.MarketWindow.SellOrders.All(o => o.StationId != Cache.Instance.DirectEve.Session.StationId))
            {
                _currentMineral.LastUpdate = DateTime.UtcNow;

                Logging.Log(module, "No sell orders found for " + _currentMineral.Name, Logging.White);
                return false;
            }

            // Take top 1% orders and count median-sell price
            orders = Cache.Instance.MarketWindow.SellOrders.Where(o => o.StationId == Cache.Instance.DirectEve.Session.StationId).OrderBy(o => o.Price).ToList();
            totalAmount = orders.Sum(o => (double)o.VolumeRemaining);
            amount = 0;
            value = 0;
            count = 0;
            for (int i = 0; i < orders.Count(); i++)
            {
                amount += orders[i].VolumeRemaining;
                value += orders[i].VolumeRemaining * orders[i].Price;
                count++;

                //Logging.Log(_currentMineral.Name + " " + count + ": " + orders[i].VolumeRemaining.ToString("#,##0") + " items @ " + orders[i].Price);
                if (amount / totalAmount > 0.01)
                {
                    break;
                }
            }

            _currentMineral.MedianSell = value / amount - 0.01;
            Logging.Log(module, "Average sell price for " + _currentMineral.Name + " is " + _currentMineral.MedianSell.Value.ToString("#,##0.00") + " (" + count + " / " + orders.Count() + " orders, " + amount.ToString("#,##0") + " / " + totalAmount.ToString("#,##0") + " items)", Logging.White);

            if (_currentMineral.MedianSell.HasValue && !double.IsNaN(_currentMineral.MedianSell.Value))
            {
                _currentMineral.MedianAll = _currentMineral.MedianSell;
            }
            else if (_currentMineral.MedianBuy.HasValue && !double.IsNaN(_currentMineral.MedianBuy.Value))
            {
                _currentMineral.MedianAll = _currentMineral.MedianBuy;
            }

            //we have not processed every mineral yet, return so we can process the next available mineral.
            _currentMineral.LastUpdate = DateTime.UtcNow;
            return false;
        }

        public static bool SaveMineralprices(string module)
        {
            Logging.Log(module, "Updating reprocess prices", Logging.White);

            // a quick price check table
            Dictionary<string, double> mineralPrices = new Dictionary<string, double>();
            foreach (InvTypeMarket i in InvTypesById.Values)
            {
                if (InvTypeMarket.Minerals.Contains(i.Name))
                {
                    mineralPrices.Add(i.Name, i.MedianBuy ?? 0);
                }
            }

            foreach (InvTypeMarket i in InvTypesById.Values)
            {
                double temp = 0;
                foreach (string m in InvTypeMarket.Minerals)
                {
                    if (i.Reprocess[m].HasValue && i.Reprocess[m] > 0)
                    {
                        var d = i.Reprocess[m];
                        if (d != null)
                        {
                            temp += d.Value * mineralPrices[m];
                        }
                    }
                }

                if (temp > 0)
                {
                    i.ReprocessValue = temp;
                }
                else
                {
                    i.ReprocessValue = null;
                }
            }

            Logging.Log(module, "Saving [" + InvTypesXMLData + "]", Logging.White);

            XDocument xdoc = new XDocument(new XElement("invtypes"));
            foreach (InvTypeMarket type in InvTypesById.Values.OrderBy(i => i.Id))
            {
                if (xdoc.Root != null)
                {
                    xdoc.Root.Add(type.Save());
                }
            }

            try
            {
                xdoc.Save(InvTypesXMLData);
            }
            catch (Exception ex)
            {
                Logging.Log(module, "Unable to save [" + InvTypesXMLData + "], is it a file permissions issue? Is the file open and locked? exception was [ " + ex.Message + "]", Logging.Orange);
                return false;
            }

            return true;
        }

        public static bool UpdatePrices(string module, bool sell, bool refine, bool undersell)
        {
            bool updated = false;

            foreach (ItemCacheMarket item in Items)
            {
                InvTypeMarket invType;
                if (!InvTypesById.TryGetValue(item.TypeId, out invType))
                {
                    Logging.Log(module, "Unknown TypeId " + item.TypeId + " for " + item.Name + ", adding to the list", Logging.Orange);
                    invType = new InvTypeMarket(item);
                    InvTypesById.Add(item.TypeId, invType);
                    updated = true;
                    continue;
                }
                item.InvType = invType;

                bool updItem = false;
                foreach (ItemCacheMarket material in item.RefineOutput)
                {
                    try
                    {
                        if (!InvTypesById.TryGetValue(material.TypeId, out invType))
                        {
                            Logging.Log(module, "Unknown TypeId [" + material.TypeId + "] for [" + material.Name + "]", Logging.White);
                            continue;
                        }
                        material.InvType = invType;

                        double matsPerItem = (double)material.Quantity / item.PortionSize;
                        bool exists = InvTypesById[item.TypeId].Reprocess[material.Name].HasValue;
                        if ((!exists && matsPerItem > 0) || (exists && InvTypesById[item.TypeId].Reprocess[material.Name] != matsPerItem))
                        {
                            if (exists)
                                Logging.Log(module,
                                    Logging.Orange + " [" + Logging.White +
                                    item.Name +
                                    Logging.Orange + "][" + Logging.White +
                                    material.Name +
                                    Logging.Orange + "] old value: [" + Logging.White +
                                    InvTypesById[item.TypeId].Reprocess[material.Name] + ", new value: " +
                                    Logging.Orange + "[" + Logging.White + matsPerItem +
                                    Logging.Orange + "]", Logging.White);
                            InvTypesById[item.TypeId].Reprocess[material.Name] = matsPerItem;
                            updItem = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log(module, "Unknown TypeId [" + material.TypeId + "] for [" + material.Name + "] Exception was: " + ex, Logging.Orange);
                        continue;
                    }
                }

                if (updItem)
                {
                    Logging.Log(module, "Updated [" + item.Name + "] refine materials", Logging.White);
                }
                updated |= updItem;
            }

            if (updated)
            {
                _States.CurrentValueDumpState = ValueDumpState.SaveMineralPrices;
                return false;
            }

            if (sell || refine)
            {
                // Copy the items to sell list
                ItemsToSell.Clear();
                ItemsToRefine.Clear();
                if (undersell)
                {
                    //
                    // Add all items to the list of things to sell
                    //
                    ItemsToSell.AddRange(Items.Where(i => i.InvType != null && i.MarketGroupId > 0));
                }
                else
                {
                    //
                    // Add only items with decent buy orders to the list of tings to sell
                    //
                    ItemsToSell.AddRange(Items.Where(i => i.InvType != null && i.MarketGroupId > 0 && i.InvType.MedianBuy.HasValue));
                    
                    if (Settings.Instance.DebugValuedump)
                    {
                        Logging.Log(module, "UpdatePrices: We will be selling [" + Items.Count(i => i.InvType != null && i.MarketGroupId > 0 && i.InvType.MedianBuy.HasValue) + "] items out of [" + Items.Count() + "] total", Logging.Debug);
                        
                        int itemcounter = 1;
                        foreach (ItemCacheMarket Item in Items.Where(i => i.InvType != null && i.MarketGroupId > 0 && !i.InvType.MedianBuy.HasValue))
                        {
                            itemcounter++;
                            Logging.Log(module,"UpdatePrices: Item [" + itemcounter + "of" + Items.Count() + "][" + Item.Name + "] was found to have no valuable buy orders and will not be sold.",Logging.Debug);
                        }
                    }
                }

                _States.CurrentValueDumpState = ValueDumpState.NextItem;
                return false;
            }

            return true;
        }

        public static bool NextItem(string module)
        {
            if (ItemsToSell.Count == 0)
            {
                if (ItemsToRefine.Count != 0)
                {
                    Logging.Log(module, ItemsToSell.Count + " items left to sell. Refining Items Next.", Logging.White);
                    _States.CurrentValueDumpState = ValueDumpState.RefineItems;
                    return false;
                }

                Logging.Log(module, ItemsToSell.Count + " items left to sell. Done.", Logging.White);
                _States.CurrentValueDumpState = ValueDumpState.Done;
                return false;
            }

            //if (!_form.RefineCheckBox.Checked)
            Logging.Log(module, ItemsToSell.Count + " items left to sell", Logging.White);

            _currentItem = ItemsToSell[0];
            ItemsToSell.RemoveAt(0);

            // Do not sell containers
            if (_currentItem.GroupId == (int)Group.AuditLogSecureContainer ||
                _currentItem.GroupId == (int)Group.FreightContainer ||
                _currentItem.GroupId == (int)Group.SecureContainer
               )
            {
                Logging.Log(module, "Skipping " + _currentItem.Name, Logging.White);
                return false;
            }

            try
            {
                // Do not sell items in InvIgnore.xml
                if (Cache.Instance.InvIgnore.Root != null)
                {
                    foreach (XElement element in Cache.Instance.InvIgnore.Root.Elements("invtype"))
                    {
                        if (_currentItem.TypeId == (int)element.Attribute("id"))
                        {
                            Logging.Log(module, "Skipping [" + _currentItem.Name + "] because it is listed in InvIgnore.xml", Logging.White);
                            return false;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                if (Settings.Instance.DebugValuedump) Logging.Log(module, "InvIgnore processing caused an exception [" + exception + "]", Logging.Debug);
            }

            return true;
        }

        public static bool StartQuickSell(string module, bool sell)
        {
            if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds < Time.Instance.Marketsellorderdelay_seconds)
                return false;
            _lastExecute = DateTime.UtcNow;

            DirectItem directItem = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.ItemId == _currentItem.Id);
            if (directItem == null)
            {
                Logging.Log(module, "Item " + _currentItem.Name + " no longer exists in the hanger", Logging.White);
                return false;
            }

            // Update Quantity
            _currentItem.QuantitySold = _currentItem.Quantity - directItem.Quantity;

            if (sell)
            {
                DirectMarketActionWindow sellWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketActionWindow>().FirstOrDefault(w => w.IsSellAction);

                //
                // if we do not yet have a sell window then start the QuickSell for this item
                //
                if (sellWindow == null || !sellWindow.IsReady || sellWindow.Item.ItemId != _currentItem.Id)
                {
                    Logging.Log(module, "Starting QuickSell for " + _currentItem.Name, Logging.White);
                    if (!directItem.QuickSell())
                    {
                        _lastExecute = DateTime.UtcNow.AddSeconds(-5);

                        Logging.Log(module, "QuickSell failed for " + _currentItem.Name + ", retrying in 5 seconds", Logging.White);
                        return false;
                    }
                    return false;
                }

                //
                // what happens here if we have a sell window that is not a quicksell window? wont this hang?
                //

                //
                // proceed to the next state
                //

                // Mark as new execution
                _lastExecute = DateTime.UtcNow;
                return true;
            }

            //
            // if we are not selling check to see if we should refine.
            //
            _States.CurrentValueDumpState = ValueDumpState.InspectRefinery;
            return false;
        }

        public static bool Inspectorder(string module, bool sell, bool refine, bool undersell, double RefiningEff)
        {
            // Let the order window stay open for a few seconds
            if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds < Time.Instance.Marketbuyorderdelay_seconds)
                return false;

            DirectMarketActionWindow sellWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketActionWindow>().FirstOrDefault(w => w.IsSellAction);

            if (sellWindow != null && (!sellWindow.OrderId.HasValue || !sellWindow.Price.HasValue || !sellWindow.RemainingVolume.HasValue))
            {
                Logging.Log(module, "No order available for " + _currentItem.Name, Logging.White);

                sellWindow.Cancel();
                //
                // next state.
                //
                return true;
            }

            if (sellWindow != null)
            {
                double price = sellWindow.Price.Value;
                int quantity = (int)Math.Min(_currentItem.Quantity - _currentItem.QuantitySold, sellWindow.RemainingVolume.Value);
                double totalPrice = quantity * price;

                string otherPrices = " ";
                if (_currentItem.InvType.MedianBuy.HasValue)
                {
                    otherPrices += "[Median buy price: " + (_currentItem.InvType.MedianBuy.Value * quantity).ToString("#,##0.00") + "]";
                }
                else
                {
                    otherPrices += "[No median buy price]";
                }

                if (refine)
                {
                    int portions = quantity / _currentItem.PortionSize;
                    double refinePrice = _currentItem.RefineOutput.Any()
                                             ? _currentItem.RefineOutput.Sum(
                                                 m => m.Quantity * m.InvType.MedianBuy ?? 0) * portions
                                             : 0;
                    refinePrice *= RefiningEff / 100;

                    otherPrices += "[Refine price: " + refinePrice.ToString("#,##0.00") + "]";

                    if (refinePrice > totalPrice)
                    {
                        Logging.Log(module, "InspectRefinery [" + _currentItem.Name + "[" + quantity + "units] is worth more as mins [Refine each: " + (refinePrice / portions).ToString("#,##0.00") + "][Sell each: " + price.ToString("#,##0.00") + "][Refine total: " + refinePrice.ToString("#,##0.00") + "][Sell total: " + totalPrice.ToString("#,##0.00") + "]", Logging.White);

                        // Add it to the refine list
                        ItemsToRefine.Add(_currentItem);

                        sellWindow.Cancel();
                        //
                        // next state.
                        //
                        return true;
                    }
                }

                if (!undersell)
                {
                    if (!_currentItem.InvType.MedianBuy.HasValue)
                    {
                        Logging.Log(module, "No historical price available for " + _currentItem.Name,
                                    Logging.White);

                        sellWindow.Cancel();
                        //
                        // next state.
                        //
                        return true;
                    }

                    double perc = price / _currentItem.InvType.MedianBuy.Value;
                    double total = _currentItem.InvType.MedianBuy.Value * _currentItem.Quantity;

                    // If percentage < 85% and total price > 1m isk then skip this item (we don't undersell)
                    if (perc < 0.85 && total > 1000000)
                    {
                        Logging.Log(module, "Not underselling item " + _currentItem.Name +
                                                   Logging.Orange + " [" + Logging.White +
                                                   "Median buy price: " +
                                                   _currentItem.InvType.MedianBuy.Value.ToString("#,##0.00") +
                                                   Logging.Orange + "][" + Logging.White +
                                                   "Sell price: " + price.ToString("#,##0.00") +
                                                   Logging.Orange + "][" + Logging.White +
                                                   perc.ToString("0%") +
                                                   Logging.Orange + "]", Logging.White);

                        sellWindow.Cancel();
                        //
                        // next state.
                        //
                        return true;
                    }
                }

                // Update quantity sold
                _currentItem.QuantitySold += quantity;

                // Update station price
                if (!_currentItem.StationBuy.HasValue)
                {
                    _currentItem.StationBuy = price;
                }

                _currentItem.StationBuy = (_currentItem.StationBuy + price) / 2;

                if (sell)
                {
                    Logging.Log(module, "Selling " + quantity + " of " + _currentItem.Name +
                                               Logging.Orange + " [" + Logging.White +
                                               "Sell price: " + (price * quantity).ToString("#,##0.00") +
                                               Logging.Orange + "]" + Logging.White +
                                               otherPrices, Logging.White);
                    sellWindow.Accept();

                    // Update quantity sold
                    _currentItem.QuantitySold += quantity;

                    // Re-queue to check again
                    if (_currentItem.QuantitySold < _currentItem.Quantity)
                    {
                        ItemsToSell.Add(_currentItem);
                    }

                    _lastExecute = DateTime.UtcNow;
                    //
                    // next state
                    //
                    return true;
                }
            }
            return true; //how would we get here with no sell window?
        }

        public static bool InspectRefinery(string module, double RefiningEff)
        {
            if (_currentItem.InvType.MedianBuy != null)
            {
                double priceR = _currentItem.InvType.MedianBuy.Value;
                int quantityR = _currentItem.Quantity;
                double totalPriceR = quantityR * priceR;
                int portions = quantityR / _currentItem.PortionSize;
                double refinePrice = _currentItem.RefineOutput.Any() ? _currentItem.RefineOutput.Sum(m => m.Quantity * m.InvType.MedianBuy ?? 0) * portions : 0;
                refinePrice *= RefiningEff / 100;

                if (refinePrice > totalPriceR || totalPriceR <= 1500000 || _currentItem.TypeId == 30497)
                {
                    Logging.Log(module, "InspectRefinery [" + _currentItem.Name + "[" + quantityR + "units] is worth more as mins [Refine each: " + (refinePrice / portions).ToString("#,##0.00") + "][Sell each: " + priceR.ToString("#,##0.00") + "][Refine total: " + refinePrice.ToString("#,##0.00") + "][Sell total: " + totalPriceR.ToString("#,##0.00") + "]", Logging.White);

                    // Add it to the refine list
                    ItemsToRefine.Add(_currentItem);
                }
                else
                {
                    if (Settings.Instance.DebugValuedump)
                    {
                        Logging.Log(module, "InspectRefinery [" + _currentItem.Name + "[" + quantityR + "units] is worth more to sell [Refine each: " + (refinePrice / portions).ToString("#,##0.00") + "][Sell each: " + priceR.ToString("#,##0.00") + "][Refine total: " + refinePrice.ToString("#,##0.00") + "][Sell total: " + totalPriceR.ToString("#,##0.00") + "]", Logging.White);
                    }
                }
            }
            /*else
            {
                Logging.Log("Selling gives a better price for item " + _currentItem.Name + " [Refine price: " + refinePrice.ToString("#,##0.00") + "][Sell price: " + totalPrice_r.ToString("#,##0.00") + "]");
            }*/

            _lastExecute = DateTime.UtcNow;
            return true;
        }

        public static bool WaitingToFinishQuickSell(string module)
        {
            DirectMarketActionWindow sellWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketActionWindow>().FirstOrDefault(w => w.IsSellAction);
            if (sellWindow == null || !sellWindow.IsReady || sellWindow.Item.ItemId != _currentItem.Id)
            {
                //
                // this closes ANY modal window and moves on, do we want to be more discriminating?
                //
                DirectWindow modal = Cache.Instance.DirectEve.Windows.FirstOrDefault(w => w.IsModal);
                if (modal != null)
                {
                    modal.Close();
                }

                return true;
            }
            return false;
        }

        public static bool RefineItems(string module, bool refine)
        {
            if (refine)
            {
                if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (refine)", Logging.Debug);
                        
                if (!Cache.Instance.OpenItemsHangar(module)) return false;
                DirectReprocessingWindow reprocessingWindow = Cache.Instance.DirectEve.Windows.OfType<DirectReprocessingWindow>().FirstOrDefault();

                if (reprocessingWindow == null)
                {
                    if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (reprocessingWindow == null)", Logging.Debug);
                
                    if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > Time.Instance.Marketlookupdelay_seconds)
                    {
                        IEnumerable<DirectItem> refineItems = Cache.Instance.ItemHangar.Items.Where(i => ItemsToRefine.Any(r => r.Id == i.ItemId)).ToList();
                        if (refineItems.Any())
                        {
                            if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (refineItems.Any())", Logging.Debug);

                            Cache.Instance.DirectEve.ReprocessStationItems(refineItems);
                            if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: Cache.Instance.DirectEve.ReprocessStationItems(refineItems);", Logging.Debug);
                            _lastExecute = DateTime.UtcNow;
                            return false;
                        }

                        if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (refineItems.Any()) was false", Logging.Debug);
                        return false;
                    }

                    return false;
                }

                if (reprocessingWindow.NeedsQuote)
                {
                    if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (reprocessingWindow.NeedsQuote)", Logging.Debug);

                    if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > Time.Instance.Marketlookupdelay_seconds)
                    {
                        if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > Time.Instance.Marketlookupdelay_seconds)", Logging.Debug);

                        reprocessingWindow.GetQuotes();
                        _lastExecute = DateTime.UtcNow;
                        return false;
                    }

                    if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: waiting for: if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > Time.Instance.Marketlookupdelay_seconds)", Logging.Debug);
                    return false;
                }

                if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: // Wait till we have a quote", Logging.Debug);

                // Wait till we have a quote
                if (reprocessingWindow.Quotes.Count == 0)
                {
                    if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (reprocessingWindow.Quotes.Count == 0)", Logging.Debug);
                    _lastExecute = DateTime.UtcNow;
                    return false;
                }

                if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: // Wait another 5 seconds to view the quote and then reprocess the stuff", Logging.Debug);

                // Wait another 5 seconds to view the quote and then reprocess the stuff
                if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > Time.Instance.Marketlookupdelay_seconds)
                {
                    if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > Time.Instance.Marketlookupdelay_seconds)", Logging.Debug);
                    // TODO: We should wait for the items to appear in our hangar and then sell them...
                    reprocessingWindow.Reprocess();
                    return true;
                }
            }
            else
            {
                if (Settings.Instance.DebugValuedump) Logging.Log(module, "RefineItems: if (!refine)", Logging.Debug);
                
                if (!Cache.Instance.OpenCargoHold(module)) return false;
                if (!Cache.Instance.ReadyAmmoHangar(module)) return false;

                IEnumerable<DirectItem> refineItems = Cache.Instance.ItemHangar.Items.Where(i => ItemsToRefine.Any(r => r.Id == i.ItemId)).ToList();
                if (refineItems.Any())
                {
                    Logging.Log("Arm", "Moving loot to refine to CargoHold", Logging.White);

                    Cache.Instance.CargoHold.Add(refineItems);
                    _lastExecute = DateTime.UtcNow;
                    return false;
                }

                _States.CurrentValueDumpState = ValueDumpState.Idle;
                return true;
            }
            return false;
        }


    }
}