// ------------------------------------------------------------------------------
// <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
// Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
// Please look in the accompanying license.htm file for the license that
// applies to this source code. (a copy can also be found at:
// http://www.thehackerwithin.com/license.htm)
// </copyright>
// -------------------------------------------------------------------------------

namespace BuyLPI
{
    using System;
    using System.Linq;
    using System.Threading;
    using DirectEve;
    //using InnerSpaceAPI;
    using System.Globalization;
    using Questor.Modules.Logging;
    using Questor.Modules.Lookup;
    using Questor.Modules.Caching;
    using Questor.Modules.BackgroundTasks;

    internal class BuyLPI
    {
        public static DateTime DateTimeForLogs;
        private const int WaitMillis = 3500;
        private static long _lastLoyaltyPoints;
        private static DateTime _nextAction;
        private static DateTime _loyaltyPointTimeout;
        private static string _type;
        private static int? _quantity;
        private static int? _totalQuantityOfOrders;
        private static DateTime _done = DateTime.UtcNow.AddDays(10);
        private static DirectEve _directEve;
        private static DateTime _lastPulse;
        private static Cleanup _cleanup;

        private static void Main(string[] args)
        {
            DateTimeForLogs = DateTime.Now;
            //Logging.Log("BuyLPI", "BuyLPI: Test", Logging.White);
            //InnerSpace.Echo(string.Format("{0:HH:mm:ss} {1}", DateTimeForLogs, "BuyLPI: Test2"));
            
            if (args.Length == 0)
            {
                //InnerSpace.Echo(string.Format("{0:HH:mm:ss} {1}", DateTimeForLogs, "BuyLPI: 0 arguments"));
                Logging.Log("BuyLPI", "Syntax:", Logging.White);
                Logging.Log("BuyLPI", "DotNet BuyLPI BuyLPI <TypeName or TypeId> [Quantity]", Logging.White);
                Logging.Log("BuyLPI", "(Quantity is optional)", Logging.White);
                Logging.Log("BuyLPI", "", Logging.White);
                Logging.Log("BuyLPI", "Example:", Logging.White);
                Logging.Log("BuyLPI", "DotNet BuyLPI BuyLPI \"Caldari Navy Mjolnir Torpedo\" 10", Logging.White);
                Logging.Log("BuyLPI", "*OR*", Logging.White);
                Logging.Log("BuyLPI", "DotNet BuyLPI BuyLPI 27339 10", Logging.White);
                return;
            }

            if (args.Length >= 1)
            {
                _type = args[0];
            }

            if (args.Length >= 2)
            {
                int dummy;
                if (!int.TryParse(args[1], out dummy))
                {
                    Logging.Log("BuyLPI", "Quantity must be an integer, 0 - " + int.MaxValue, Logging.White);
                    return;
                }

                if (dummy < 0)
                {
                    Logging.Log("BuyLPI", "Quantity must be a positive number", Logging.White);
                    return;
                }

                _quantity = dummy;
                _totalQuantityOfOrders = dummy;
            }

            //InnerSpace.Echo(string.Format("{0:HH:mm:ss} {1}", DateTimeForLogs, "Starting BuyLPI... - innerspace Echo"));
            Logging.Log("BuyLPI", "Starting BuyLPI...", Logging.White);
            _cleanup = new Cleanup();
            _directEve = new DirectEve();
            Cache.Instance.DirectEve = _directEve;
            _directEve.OnFrame += OnFrame;

            // Sleep until we're done
            while (_done.AddSeconds(5) > DateTime.UtcNow)
            {
                Thread.Sleep(50);
            }

            _directEve.Dispose();
            Logging.Log("BuyLPI", "BuyLPI finished.", Logging.White);
            //InnerSpace.Echo(string.Format("{0:HH:mm:ss} {1}", DateTimeForLogs, "BuyLPI: Finished 2"));
        }

        private static void OnFrame(object sender, EventArgs eventArgs)
        {
            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            Cache.Instance.LastFrame = DateTime.UtcNow;

            // Only pulse state changes every 1.5s
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < 300)
                return;

            _lastPulse = DateTime.UtcNow;

            // Session is not ready yet, do not continue
            if (!Cache.Instance.DirectEve.Session.IsReady)
                return;

            if (Cache.Instance.DirectEve.Session.IsReady)
                Cache.Instance.LastSessionIsReady = DateTime.UtcNow;

            // We are not in space or station, don't do shit yet!
            if (!Cache.Instance.InSpace && !Cache.Instance.InStation)
            {
                Cache.Instance.NextInSpaceorInStation = DateTime.UtcNow.AddSeconds(12);
                Cache.Instance.LastSessionChange = DateTime.UtcNow;
                return;
            }

            if (DateTime.UtcNow < Cache.Instance.NextInSpaceorInStation)
                return;

            if (Cache.Instance.SessionState != "Quitting")
            {
                // Update settings (settings only load if character name changed)
                if (!Settings.Instance.DefaultSettingsLoaded)
                {
                    Settings.Instance.LoadSettings();
                }
            }

            // Start _cleanup.ProcessState
            // Description: Closes Windows, and eventually other things considered 'cleanup' useful to more than just Questor(Missions) but also Anomalies, Mining, etc
            //
            _cleanup.ProcessState();

            // Done
            // Cleanup State: ProcessState

            if (DateTime.UtcNow > _done)
                return;

            // Wait for the next action
            if (_nextAction >= DateTime.UtcNow)
            {
                return;
            }

            if (!Cache.Instance.OpenItemsHangar("BuyLPI")) return;

            if (!Cache.Instance.OpenLPStore("BuyLPI")) return;

            if (Cache.Instance.LPStore == null)
            {
                _nextAction = DateTime.UtcNow.AddMilliseconds(WaitMillis);
                _directEve.ExecuteCommand(DirectCmd.OpenLpstore);

                Logging.Log("BuyLPI", "Opening loyalty point store", Logging.White);
                return;
            }

            // Wait for the amount of LP to change
            if (_lastLoyaltyPoints == Cache.Instance.LPStore.LoyaltyPoints)
                return;

            // Do not expect it to be 0 (probably means its reloading)
            if (Cache.Instance.LPStore.LoyaltyPoints == 0)
            {
                if (_loyaltyPointTimeout < DateTime.UtcNow)
                {
                    Logging.Log("BuyLPI", "It seems we have no loyalty points left", Logging.White);
                    _done = DateTime.UtcNow;
                    return;
                }
                return;
            }

            _lastLoyaltyPoints = Cache.Instance.LPStore.LoyaltyPoints;

            // Find the offer
            DirectLoyaltyPointOffer offer = Cache.Instance.LPStore.Offers.FirstOrDefault(o => o.TypeId.ToString(CultureInfo.InvariantCulture) == _type || String.Compare(o.TypeName, _type, StringComparison.OrdinalIgnoreCase) == 0);
            if (offer == null)
            {
                Logging.Log("BuyLPI", " Can't find offer with type name/id: [" + _type + "]", Logging.White);
                _done = DateTime.UtcNow;
                return;
            }

            // Check LP
            if (_lastLoyaltyPoints < offer.LoyaltyPointCost)
            {
                Logging.Log("BuyLPI", "Not enough loyalty points left: you have [" + _lastLoyaltyPoints + "] and you need [" + offer.LoyaltyPointCost + "]", Logging.White);
                _done = DateTime.UtcNow;
                return;
            }

            // Check ISK
            if (_directEve.Me.Wealth < offer.IskCost)
            {
                Logging.Log("BuyLPI", "Not enough ISK left: you have [" + Math.Round(_directEve.Me.Wealth, 0) + "] and you need [" + offer.IskCost + "]", Logging.White);
                _done = DateTime.UtcNow;
                return;
            }

            // Check items
            foreach (DirectLoyaltyPointOfferRequiredItem requiredItem in offer.RequiredItems)
            {
                DirectItem item = Cache.Instance.ItemHangar.Items.FirstOrDefault(i => i.TypeId == requiredItem.TypeId);
                if (item == null || item.Quantity < requiredItem.Quantity)
                {
                    Logging.Log("BuyLPI", "Missing [" + requiredItem.Quantity + "] x [" +
                                                    requiredItem.TypeName + "]", Logging.White);
                    _done = DateTime.UtcNow;
                    return;
                }
            }

            // All passed, accept offer
            if (_quantity != null)
                if (_totalQuantityOfOrders != null)
                    Logging.Log("BuyLPI", "Accepting " + offer.TypeName + " [ " + _quantity.Value + " ] of [ " + _totalQuantityOfOrders.Value + " ] orders and will cost another [" + Math.Round(((offer.IskCost * _quantity.Value) / (double)1000000), 2) + "mil isk]", Logging.White);
            offer.AcceptOfferFromWindow();

            // Set next action + loyalty point timeout
            _nextAction = DateTime.UtcNow.AddMilliseconds(WaitMillis);
            _loyaltyPointTimeout = DateTime.UtcNow.AddSeconds(25);

            if (_quantity.HasValue)
            {
                _quantity = _quantity.Value - 1;
                if (_quantity.Value <= 0)
                {
                    Logging.Log("BuyLPI", "Quantity limit reached", Logging.White);
                    _done = DateTime.UtcNow;
                    return;
                }
            }
        }
    }
}