using System;
using System.Collections.Generic;
using System.Linq;
using DirectEve;
using Questor.Modules.Caching;
using Questor.Modules.Logging;
using Questor.Modules.States;

namespace QuestorManager.Actions
{
    public class BuyLPI
    {
        public int Item { get; set; }

        public int Unit { get; set; }

        private QuestorManagerUI _form;

        private static DateTime _lastAction;
        private static DateTime _loyaltyPointTimeout;
        private static long _lastLoyaltyPoints;
        private int _requiredUnit;
        private int _requiredItemId;
        private static DirectLoyaltyPointOffer _offer;

        public BuyLPI(QuestorManagerUI form1)
        {
            _form = form1;
        }

        public void ProcessState()
        {
            if (DateTime.UtcNow.Subtract(_lastAction).TotalSeconds < 1)
                return;
            _lastAction = DateTime.UtcNow;

            if (!Cache.Instance.OpenItemsHangar("BuyLPI")) return;
            DirectMarketWindow marketWindow = Cache.Instance.DirectEve.Windows.OfType<DirectMarketWindow>().FirstOrDefault();

            switch (_States.CurrentBuyLPIState)
            {
                case BuyLPIState.Idle:
                case BuyLPIState.Done:
                    break;

                case BuyLPIState.Begin:

                    /*
                    if(marketWindow != null)
                        marketWindow.Close();

                    if(lpstore != null)
                        lpstore.Close();*/

                    _States.CurrentBuyLPIState = BuyLPIState.ReadyItemhangar;
                    break;

                case BuyLPIState.ReadyItemhangar:

                    if (!Cache.Instance.OpenItemsHangar("BuyLPI")) return;
                    if (!Cache.Instance.OpenShipsHangar("BuyLPI")) return;

                    _States.CurrentBuyLPIState = BuyLPIState.OpenLpStore;
                    break;

                case BuyLPIState.OpenLpStore:

                    if (!Cache.Instance.OpenLPStore("BuyLPI")) return;
                    _States.CurrentBuyLPIState = BuyLPIState.FindOffer;
                    break;

                case BuyLPIState.FindOffer:

                    if (Cache.Instance.LPStore != null)
                    {
                        BuyLPI._offer = Cache.Instance.LPStore.Offers.FirstOrDefault(o => o.TypeId == Item);

                        // Wait for the amount of LP to change
                        if (_lastLoyaltyPoints == Cache.Instance.LPStore.LoyaltyPoints)
                            break;

                        // Do not expect it to be 0 (probably means its reloading)
                        if (Cache.Instance.LPStore.LoyaltyPoints == 0)
                        {
                            if (_loyaltyPointTimeout < DateTime.UtcNow)
                            {
                                Logging.Log("BuyLPI", "It seems we have no loyalty points left", Logging.White);
                                _States.CurrentBuyLPIState = BuyLPIState.Done;
                                break;
                            }
                            break;
                        }

                        _lastLoyaltyPoints = Cache.Instance.LPStore.LoyaltyPoints;

                        // Find the offer
                        if (_offer == null)
                        {
                            Logging.Log("BuyLPI", "Can't find offer with type name/id: " + Item + "!", Logging.White);
                            _States.CurrentBuyLPIState = BuyLPIState.Done;
                            break;
                        }
                        _States.CurrentBuyLPIState = BuyLPIState.CheckPetition;
                    }
                    _States.CurrentBuyLPIState = BuyLPIState.OpenLpStore;
                    break;

                case BuyLPIState.CheckPetition:

                    if (Cache.Instance.LPStore != null)
                    {
                        // Check LP
                        if (_lastLoyaltyPoints < _offer.LoyaltyPointCost)
                        {
                            Logging.Log("BuyLPI", "Not enough loyalty points left", Logging.White);

                            _States.CurrentBuyLPIState = BuyLPIState.Done;
                            break;
                        }

                        // Check ISK
                        if (Cache.Instance.DirectEve.Me.Wealth < _offer.IskCost)
                        {
                            Logging.Log("BuyLPI", "Not enough ISK left", Logging.White);

                            _States.CurrentBuyLPIState = BuyLPIState.Done;
                            break;
                        }

                        // Check items
                        foreach (DirectLoyaltyPointOfferRequiredItem requiredItem in _offer.RequiredItems)
                        {
                            DirectItem ship = Cache.Instance.ShipHangar.Items.FirstOrDefault(i => i.TypeId == requiredItem.TypeId);
                            DirectItem item = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.TypeId == requiredItem.TypeId);
                            if (item == null || item.Quantity < requiredItem.Quantity)
                            {
                                if (ship == null || ship.Quantity < requiredItem.Quantity)
                                {
                                    Logging.Log("BuyLPI", "Missing [" + requiredItem.Quantity + "] x [" +
                                                requiredItem.TypeName + "]", Logging.White);

                                    //if(!_form.chkBuyItems.Checked)
                                    //{
                                    //    Logging.Log("BuyLPI","Done, do not buy item");
                                    //    States.CurrentBuyLPIState = BuyLPIState.Done;
                                    //    break;
                                    //}

                                    Logging.Log("BuyLPI", "Are buying the item [" + requiredItem.TypeName + "]",
                                            Logging.White);
                                    _requiredUnit = Convert.ToInt32(requiredItem.Quantity);
                                    _requiredItemId = requiredItem.TypeId;
                                    _States.CurrentBuyLPIState = BuyLPIState.OpenMarket;
                                    return;
                                }
                            }
                            _States.CurrentBuyLPIState = BuyLPIState.AcceptOffer;
                        }
                        _States.CurrentBuyLPIState = BuyLPIState.OpenLpStore;
                    }
                    break;

                case BuyLPIState.OpenMarket:

                    if (marketWindow == null)
                    {
                        Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.OpenMarket);
                        break;
                    }

                    if (!marketWindow.IsReady)
                        break;

                    _States.CurrentBuyLPIState = BuyLPIState.BuyItems;
                    break;

                case BuyLPIState.BuyItems:

                    Logging.Log("BuyLPI", "Opening Market", Logging.White);

                    if (marketWindow != null && marketWindow.DetailTypeId != _requiredItemId)
                    {
                        marketWindow.LoadTypeId(_requiredItemId);
                        break;
                    }

                    if (marketWindow != null)
                    {
                        IEnumerable<DirectOrder> orders =
                            marketWindow.SellOrders.Where(o => o.StationId == Cache.Instance.DirectEve.Session.StationId);

                        DirectOrder order = orders.OrderBy(o => o.Price).FirstOrDefault();

                        if (order == null)
                        {
                            Logging.Log("BuyLPI", "No orders", Logging.White);
                            _States.CurrentBuyLPIState = BuyLPIState.Done;
                            break;
                        }

                        order.Buy(_requiredUnit, DirectOrderRange.Station);
                    }

                    Logging.Log("BuyLPI", "Buy Item", Logging.White);

                    _States.CurrentBuyLPIState = BuyLPIState.CheckPetition;

                    break;

                case BuyLPIState.AcceptOffer:

                    if (Cache.Instance.LPStore != null)
                    {
                        DirectLoyaltyPointOffer offer2 = Cache.Instance.LPStore.Offers.FirstOrDefault(o => o.TypeId == Item);

                        if (offer2 != null)
                        {
                            Logging.Log("BuyLPI", "Accepting [" + offer2.TypeName + "]", Logging.White);
                            offer2.AcceptOfferFromWindow();
                        }
                    }
                    _States.CurrentBuyLPIState = BuyLPIState.Quantity;
                    break;

                case BuyLPIState.Quantity:

                    _loyaltyPointTimeout = DateTime.UtcNow.AddSeconds(1);

                    Unit = Unit - 1;
                    if (Unit <= 0)
                    {
                        Logging.Log("BuyLPI", "Quantity limit reached", Logging.White);

                        _States.CurrentBuyLPIState = BuyLPIState.Done;
                        break;
                    }

                    _States.CurrentBuyLPIState = BuyLPIState.Begin;

                    break;
            }
        }
    }
}