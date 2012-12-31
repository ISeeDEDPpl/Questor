// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor.Modules.Activities
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using DirectEve;
    using global::Questor.Modules.Actions;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Combat;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.States;

    public class Traveler
    {
        private static TravelerDestination _destination;
        private static DateTime _nextTravelerAction;
        private static DateTime _lastPulse;
        private static DateTime _nextGetLocation;
        private static DateTime _nextSetEVENavDestination = DateTime.MinValue;
        private static DateTime _nextGetDestinationPath = DateTime.MinValue;

        private static List<long> _destinationRoute;
        private static DirectLocation _location;
        private static IEnumerable<DirectBookmark> myHomeBookmarks;
        private static string _locationName;
        private static int _locationErrors;
        private static int TravelHomeCounter;
        private static Combat _combat;
        private static Drones _drones;

        private static List<long> EVENavdestination { get; set; }

        public DirectBookmark UndockBookmark { get; set; }

        public Traveler()
        {
            _lastPulse = DateTime.MinValue;
            _combat = new Combat();
            _drones = new Drones();
        }

        public static TravelerDestination Destination
        {
            get { return _destination; }
            set
            {
                _destination = value;
                _States.CurrentTravelerState = _destination == null ? TravelerState.AtDestination : TravelerState.Idle;
            }
        }

        /// <summary>
        ///   Set destination to a solar system
        /// </summary>
        public static bool SetStationDestination(long stationId)
        {
            _location = Cache.Instance.DirectEve.Navigation.GetLocation(stationId);
            if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "Location = [" + Logging.Yellow + Cache.Instance.DirectEve.Navigation.GetLocationName(stationId) + Logging.Green + "]", Logging.Green);
            if (_location.IsValid)
            {
                _locationErrors = 0;
                if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "Setting destination to [" + Logging.Yellow + _location.Name + Logging.Green + "]", Logging.Teal);
                _location.SetDestination();
                return true;
            }

            Logging.Log("Traveler", "Error setting solar system destination [" + Logging.Yellow + stationId + Logging.Green + "]", Logging.Green);
            _locationErrors++;
            if (_locationErrors > 100)
            {
                return false;
            }
            return false;
        }

        /// <summary>
        ///   Navigate to a solar system
        /// </summary>
        /// <param name = "solarSystemId"></param>
        private static void NavigateToBookmarkSystem(long solarSystemId)
        {
            if (_nextTravelerAction > DateTime.UtcNow)
            {
                //Logging.Log("Traveler: will continue in [ " + Math.Round(_nextTravelerAction.Subtract(DateTime.UtcNow).TotalSeconds, 0) + " ]sec");
                return;
            }

            Cache.Instance.NextTravelerAction = DateTime.UtcNow.AddSeconds(1);
            if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "NavigateToBookmarkSystem - Iterating- next iteration should be in no less than [1] second ", Logging.Teal);

            _destinationRoute = Cache.Instance.DirectEve.Navigation.GetDestinationPath();

            if (_destinationRoute.Count == 0 || _destinationRoute.All(d => d != solarSystemId))
            {
                if (Settings.Instance.DebugTraveler) if (_destinationRoute.Count == 0) Logging.Log("Traveler", "We have no destination", Logging.Teal);
                if (Settings.Instance.DebugTraveler) if (_destinationRoute.All(d => d != solarSystemId)) Logging.Log("Traveler", "the destination is not currently set to solarsystemId [" + solarSystemId + "]", Logging.Teal);

                // We do not have the destination set
                if (DateTime.UtcNow > _nextGetLocation || _location == null)
                {
                    if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "NavigateToBookmarkSystem: getting Location of solarSystemId [" + solarSystemId + "]", Logging.Teal);
                    _nextGetLocation = DateTime.UtcNow.AddSeconds(10);
                    _location = Cache.Instance.DirectEve.Navigation.GetLocation(solarSystemId);
                    Cache.Instance.NextTravelerAction = DateTime.UtcNow.AddSeconds(2);
                    return;
                }

                if (_location.IsValid)
                {
                    _locationErrors = 0;
                    Logging.Log("Traveler", "Setting destination to [" + Logging.Yellow + _location.Name + Logging.Green + "]", Logging.Green);
                    if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "Setting destination to [" + Logging.Yellow + _location.Name + Logging.Green + "]", Logging.Teal);
                    _location.SetDestination();
                    Cache.Instance.NextTravelerAction = DateTime.UtcNow.AddSeconds(3);
                    return;
                }

                Logging.Log("Traveler", "Error setting solar system destination [" + Logging.Yellow + solarSystemId + Logging.Green + "]", Logging.Green);
                _locationErrors++;
                if (_locationErrors > 100)
                {
                    _States.CurrentTravelerState = TravelerState.Error;
                    return;
                }
                return;
            }

            _locationErrors = 0;
            if (!Cache.Instance.InSpace)
            {
                if (Cache.Instance.InStation)
                {
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdExitStation);
                    _nextTravelerAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerExitStationAmIInSpaceYet_seconds);
                }
                Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddSeconds(Cache.Instance.RandomNumber(1, 2));

                // We are not yet in space, wait for it
                return;
            }

            // We are apparently not really in space yet...
            if (Cache.Instance.DirectEve.ActiveShip.Entity == null)
                return;

            //if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "Destination is set: processing...", Logging.Teal);

            // Find the first waypoint
            long waypoint = _destinationRoute.First();

            //if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "NavigateToBookmarkSystem: getting next waypoints locationname", Logging.Teal);
            _locationName = Cache.Instance.DirectEve.Navigation.GetLocationName(waypoint);
            if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "NavigateToBookmarkSystem: Next Waypoint is: [" + _locationName + "]", Logging.Teal);

            // Find the stargate associated with it

            if (!Cache.Instance.Stargates.Any())
            {
                // not found, that cant be true?!?!?!?!
                Logging.Log("Traveler", "Error [" + Logging.Yellow + _locationName + Logging.Green + "] not found, most likely lag waiting [" + Time.Instance.TravelerNoStargatesFoundRetryDelay_seconds + "] seconds.", Logging.Red);
                _nextTravelerAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerNoStargatesFoundRetryDelay_seconds);
                return;
            }

            // Warp to, approach or jump the stargate
            EntityCache MyNextStargate = Cache.Instance.Stargates.FirstOrDefault(e => e.Name == _locationName);
            if (MyNextStargate != null && (MyNextStargate.Distance < (int)Distance.DecloakRange && !Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked))
            {
                Logging.Log("Traveler", "Jumping to [" + Logging.Yellow + _locationName + Logging.Green + "]", Logging.Green);
                MyNextStargate.Jump();
                Cache.Instance.NextInSpaceorInStation = DateTime.UtcNow;
                _nextTravelerAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerJumpedGateNextCommandDelay_seconds);
                Cache.Instance.NextActivateSupportModules = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerJumpedGateNextCommandDelay_seconds);
                return;
            }

            if (MyNextStargate != null && MyNextStargate.Distance < (int)Distance.WarptoDistance)
            {
                if (DateTime.UtcNow > Cache.Instance.NextApproachAction && !Cache.Instance.IsApproaching)
                {
                    if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "NavigateToBookmarkSystem: approaching the stargate named [" + MyNextStargate.Name + "]", Logging.Teal);
                    MyNextStargate.Approach(); //you could use a negative approach distance here but ultimately that is a bad idea.. Id like to go toward the entity without approaching it so we would end up inside the docking ring (eventually)
                    return;
                }
                if (Settings.Instance.DebugTraveler) Logging.Log("Traveler", "NavigateToBookmarkSystem: we are already approaching the stargate named [" + MyNextStargate.Name + "]", Logging.Teal);
                return;
            }

            if (DateTime.UtcNow > Cache.Instance.NextWarpTo)
            {
                if (Cache.Instance.InSpace && !Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                {
                    if (MyNextStargate != null)
                    {
                        Logging.Log("Traveler",
                                    "Warping to [" + Logging.Yellow + _locationName + Logging.Green + "][" + Logging.Yellow +
                                    Math.Round((MyNextStargate.Distance / 1000) / 149598000, 2) + Logging.Green + " AU away]", Logging.Green);
                        MyNextStargate.WarpTo();
                    }
                    return;
                }
                return;
            }
            if (!Combat.ReloadAll(Cache.Instance.MyShip)) return;
            return;
        }

        public static void TravelToMiningHomeBookmark(DirectBookmark myHomeBookmark, string module)
        {

            //
            // defending yourself is more important that the traveling part... so it comes first.
            //
            if (Cache.Instance.InSpace && Settings.Instance.DefendWhileTraveling)
            {
                if (!Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || (Cache.Instance.LastSessionChange.AddSeconds(60) > DateTime.UtcNow))
                {
                    if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToMiningHomeBookmark: _combat.ProcessState()", Logging.White);
                    _combat.ProcessState();
                    if (!Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToMiningHomeBookmark: we are not scrambled - pulling drones.", Logging.White);
                        Cache.Instance.IsMissionPocketDone = true; //tells drones.cs that we can pull drones

                        //Logging.Log("CombatmissionBehavior","TravelToAgentStation: not pointed",Logging.White);
                    }
                    else if (Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                    {
                        Cache.Instance.IsMissionPocketDone = false;
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToMiningHomeBookmark: we are scrambled", Logging.Teal);
                        _drones.ProcessState();
                        return;
                    }
                }
            }

            Cache.Instance.OpenWrecks = false;

            if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToMiningHomeBookmark:      Cache.Instance.AgentStationId [" + Cache.Instance.AgentStationID + "]", Logging.White);
            if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToMiningHomeBookmark:  Cache.Instance.AgentSolarSystemId [" + Cache.Instance.AgentSolarSystemID + "]", Logging.White);

            if (_destination == null)
            {
                Logging.Log(module, "Destination: [" + myHomeBookmark.Description + "]", Logging.White);

                //Cache.Instance.DirectEve.Navigation.GetLocation((long)myHomeBookmark.LocationId).SetDestination();

                _destination = new BookmarkDestination(myHomeBookmark);

                //_destination = new StationDestination(Cache.Instance.AgentSolarSystemID, Cache.Instance.AgentStationID, Cache.Instance.AgentStationName);
                _States.CurrentTravelerState = TravelerState.Idle;
                return;
            }
            else
            {
                if (Settings.Instance.DebugGotobase) if (Traveler.Destination != null) Logging.Log("MiningMissionsBehavior", "TravelToMiningHomeBookmark: Traveler.Destination.SolarSystemId [" + Traveler.Destination.SolarSystemId + "]", Logging.White);
                Traveler.ProcessState();

                //we also assume you are connected during a manual set of questor into travel mode (safe assumption considering someone is at the kb)
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;

                if (_States.CurrentTravelerState == TravelerState.AtDestination)
                {
                    if (_States.CurrentCombatMissionCtrlState == CombatMissionCtrlState.Error)
                    {
                        Logging.Log(module, "an error has occurred", Logging.White);
                        if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Traveler)
                        {
                            _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Error;
                        }
                        return;
                    }

                    if (Cache.Instance.InSpace)
                    {
                        Logging.Log(module, "Arrived at destination (in space, Questor stopped)", Logging.White);
                        Cache.Instance.Paused = true;
                        return;
                    }

                    Logging.Log(module, "Arrived at destination", Logging.White);
                    if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Traveler)
                    {
                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Idle;
                    }

                    if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.Traveler)
                    {
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                    }

                    if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.Traveler)
                    {
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                    }
                    return;
                }
            }
            return;
        }

        public static void TravelHome(string module)
        {
            if (_States.CurrentQuestorState == QuestorState.CombatMissionsBehavior || _States.CurrentQuestorState == QuestorState.CloseQuestor)
            {
                //
                // if we got this far it is because we havent setup Settings.Instance.HomeBookmarkName yet or we do not have a
                // bookmark in game with the configured prefix at the start of the name of the bookmark
                // we will instead use the AgentID to find the station
                //
                if (Settings.Instance.DebugGotobase) Logging.Log("TravelHome", "TravelToAgentsStation(module);", Logging.Teal);
                TravelToAgentsStation(module);
                return;
            }

            //only call bookmark stuff if UseHomebookmark is true
            if (Settings.Instance.UseHomebookmark)
            {
                TravelHomeCounter++;
                if (myHomeBookmarks == null || TravelHomeCounter > 30)
                {
                    TravelHomeCounter = 0;
                    myHomeBookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.HomeBookmarkName).ToList();
                }

                if (myHomeBookmarks.Any())
                {
                    DirectBookmark oldestHomeBookmark = myHomeBookmarks.OrderBy(b => b.CreatedOn).FirstOrDefault();
                    if (oldestHomeBookmark != null && oldestHomeBookmark.LocationId != null)
                    {
                        TravelToHomeBookmark(oldestHomeBookmark, module);
                        return;
                    }
                    return;
                }

                Logging.Log("Traveler.TravelHome", "HomeBookmarkName bookmark not found! using AgentsStation info instead: We were Looking for bookmark starting with [" + Settings.Instance.HomeBookmarkName + "] found none.", Logging.Orange);
            } 
            TravelToAgentsStation(module);
            return;
        }

        public static void TravelToAgentsStation(string module)
        {
            //
            // defending yourself is more important that the traveling part... so it comes first.
            //
            if (Cache.Instance.InSpace && Settings.Instance.DefendWhileTraveling)
            {
                if (!Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || (Cache.Instance.LastSessionChange.AddSeconds(60) > DateTime.UtcNow))
                {
                    if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: _combat.ProcessState()", Logging.White);
                    _combat.ProcessState();
                    if (!Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: we are not scrambled - pulling drones.", Logging.White);
                        Cache.Instance.IsMissionPocketDone = true; //tells drones.cs that we can pull drones

                        //Logging.Log("CombatmissionBehavior","TravelToAgentStation: not pointed",Logging.White);
                    }
                    else if (Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                    {
                        Cache.Instance.IsMissionPocketDone = false;
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: we are scrambled", Logging.Teal);
                        _drones.ProcessState();
                        return;
                    }
                }
            }

            Cache.Instance.OpenWrecks = false;

            /*
            if (Settings.Instance.setEveClientDestinationWhenTraveling) //sets destination to Questors destination, so they match... (defaults to false, needs testing again and probably needs to be exposed as a setting)
            {
                if (DateTime.UtcNow > _nextGetDestinationPath || EVENavdestination == null)
                {
                    if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: EVENavdestination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();", Logging.White);
                    _nextGetDestinationPath = DateTime.UtcNow.AddSeconds(20);
                    _nextSetEVENavDestination = DateTime.UtcNow.AddSeconds(4);
                    EVENavdestination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();
                    if (Settings.Instance.DebugGotobase) if (EVENavdestination != null) Logging.Log(module, "TravelToAgentsStation: Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId [" + Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId + "]", Logging.White);
                    return;
                }

                if (Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId != Cache.Instance.AgentSolarSystemID)
                {
                    //Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId [" + Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId + "]", Logging.White);
                    //Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: EVENavdestination.LastOrDefault() [" + EVENavdestination.LastOrDefault() + "]", Logging.White);
                    //Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: Cache.Instance.AgentSolarSystemID [" + Cache.Instance.AgentSolarSystemID + "]", Logging.White);
                    if (DateTime.UtcNow > _nextSetEVENavDestination)
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: Cache.Instance.DirectEve.Navigation.SetDestination(Cache.Instance.AgentStationId);", Logging.White);
                        _nextSetEVENavDestination = DateTime.UtcNow.AddSeconds(7);
                        Cache.Instance.DirectEve.Navigation.SetDestination(Cache.Instance.AgentStationID);
                        Logging.Log(module, "Setting Destination to [" + Cache.Instance.AgentStationName + "'s] Station", Logging.White);
                        return;
                    }
                }
                else if (EVENavdestination != null || EVENavdestination.Count != 0)
                {
                    if (EVENavdestination.Count == 1 && EVENavdestination.First() == 0)
                        EVENavdestination[0] = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;
                }
            }
            */

            if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation:      Cache.Instance.AgentStationId [" + Cache.Instance.AgentStationID + "]", Logging.White);
            if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation:  Cache.Instance.AgentSolarSystemId [" + Cache.Instance.AgentSolarSystemID + "]", Logging.White);

            if (_destination == null || _destination.SolarSystemId != Cache.Instance.AgentSolarSystemID)
            {
                Logging.Log(module, "Destination: [" + Cache.Instance.AgentStationName + "]", Logging.White);
                _destination = new StationDestination(Cache.Instance.AgentSolarSystemID, Cache.Instance.AgentStationID, Cache.Instance.AgentStationName);
                _States.CurrentTravelerState = TravelerState.Idle;
                return;
            }
            else
            {
                if (Settings.Instance.DebugGotobase) if (Traveler.Destination != null) Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: Traveler.Destination.SolarSystemId [" + Traveler.Destination.SolarSystemId + "]", Logging.White);
                Traveler.ProcessState();

                //we also assume you are connected during a manual set of questor into travel mode (safe assumption considering someone is at the kb)
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;

                if (_States.CurrentTravelerState == TravelerState.AtDestination)
                {
                    if (_States.CurrentCombatMissionCtrlState == CombatMissionCtrlState.Error)
                    {
                        Logging.Log(module, "an error has occurred", Logging.White);
                        if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Traveler)
                        {
                            _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Error;
                        }
                        return;
                    }

                    if (Cache.Instance.InSpace)
                    {
                        Logging.Log(module, "Arrived at destination (in space, Questor stopped)", Logging.White);
                        Cache.Instance.Paused = true;
                        return;
                    }

                    Logging.Log(module, "Arrived at destination", Logging.White);
                    if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Traveler)
                    {
                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Idle;
                    }

                    if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.Traveler)
                    {
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                    }

                    if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.Traveler)
                    {
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                    }
                    return;
                }
            }
            return;
        }

        public static void TravelToHomeBookmark(DirectBookmark myHomeBookmark, string module)
        {
            //
            // defending yourself is more important that the traveling part... so it comes first.
            //
            if (Cache.Instance.InSpace && Settings.Instance.DefendWhileTraveling)
            {
                if (!Cache.Instance.DirectEve.ActiveShip.Entity.IsCloaked || (Cache.Instance.LastSessionChange.AddSeconds(60) > DateTime.UtcNow))
                {
                    if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: _combat.ProcessState()", Logging.White);
                    _combat.ProcessState();
                    if (!Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: we are not scrambled - pulling drones.", Logging.White);
                        Cache.Instance.IsMissionPocketDone = true; //tells drones.cs that we can pull drones

                        //Logging.Log("CombatmissionBehavior","TravelToAgentStation: not pointed",Logging.White);
                    }
                    else if (Cache.Instance.TargetedBy.Any(t => t.IsWarpScramblingMe))
                    {
                        Cache.Instance.IsMissionPocketDone = false;
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: we are scrambled", Logging.Teal);
                        _drones.ProcessState();
                        return;
                    }
                }
            }

            Cache.Instance.OpenWrecks = false;

            /*
            if (Settings.Instance.setEveClientDestinationWhenTraveling) //sets destination to Questors destination, so they match... (defaults to false, needs testing again and probably needs to be exposed as a setting)
            {
                if (DateTime.UtcNow > _nextGetDestinationPath || EVENavdestination == null)
                {
                    if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: EVENavdestination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();", Logging.White);
                    _nextGetDestinationPath = DateTime.UtcNow.AddSeconds(20);
                    _nextSetEVENavDestination = DateTime.UtcNow.AddSeconds(4);
                    EVENavdestination = Cache.Instance.DirectEve.Navigation.GetDestinationPath();
                    if (Settings.Instance.DebugGotobase) if (EVENavdestination != null) Logging.Log(module, "TravelToAgentsStation: Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId [" + Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId + "]", Logging.White);
                    return;
                }

                if (Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId != Cache.Instance.AgentSolarSystemID)
                {
                    //Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId [" + Cache.Instance.DirectEve.Navigation.GetLocation(EVENavdestination.Last()).LocationId + "]", Logging.White);
                    //Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: EVENavdestination.LastOrDefault() [" + EVENavdestination.LastOrDefault() + "]", Logging.White);
                    //Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: Cache.Instance.AgentSolarSystemID [" + Cache.Instance.AgentSolarSystemID + "]", Logging.White);
                    if (DateTime.UtcNow > _nextSetEVENavDestination)
                    {
                        if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation: Cache.Instance.DirectEve.Navigation.SetDestination(Cache.Instance.AgentStationId);", Logging.White);
                        _nextSetEVENavDestination = DateTime.UtcNow.AddSeconds(7);
                        Cache.Instance.DirectEve.Navigation.SetDestination(Cache.Instance.AgentStationID);
                        Logging.Log(module, "Setting Destination to [" + Cache.Instance.AgentStationName + "'s] Station", Logging.White);
                        return;
                    }
                }
                else if (EVENavdestination != null || EVENavdestination.Count != 0)
                {
                    if (EVENavdestination.Count == 1 && EVENavdestination.First() == 0)
                        EVENavdestination[0] = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;
                }
            }
            */

            if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation:      Cache.Instance.AgentStationId [" + Cache.Instance.AgentStationID + "]", Logging.White);
            if (Settings.Instance.DebugGotobase) Logging.Log(module, "TravelToAgentsStation:  Cache.Instance.AgentSolarSystemId [" + Cache.Instance.AgentSolarSystemID + "]", Logging.White);

            if (_destination == null || _destination.SolarSystemId != Cache.Instance.AgentSolarSystemID)
            {
                Logging.Log(module, "Destination: [" + Cache.Instance.AgentStationName + "]", Logging.White);
                _destination = new StationDestination(Cache.Instance.AgentSolarSystemID, Cache.Instance.AgentStationID, Cache.Instance.AgentStationName);
                _States.CurrentTravelerState = TravelerState.Idle;
                return;
            }
            else
            {
                if (Settings.Instance.DebugGotobase) if (Traveler.Destination != null) Logging.Log("CombatMissionsBehavior", "TravelToAgentsStation: Traveler.Destination.SolarSystemId [" + Traveler.Destination.SolarSystemId + "]", Logging.White);
                Traveler.ProcessState();

                //we also assume you are connected during a manual set of questor into travel mode (safe assumption considering someone is at the kb)
                Cache.Instance.LastKnownGoodConnectedTime = DateTime.UtcNow;
                Cache.Instance.MyWalletBalance = Cache.Instance.DirectEve.Me.Wealth;

                if (_States.CurrentTravelerState == TravelerState.AtDestination)
                {
                    if (_States.CurrentCombatMissionCtrlState == CombatMissionCtrlState.Error)
                    {
                        Logging.Log(module, "an error has occurred", Logging.White);
                        if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Traveler)
                        {
                            _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Error;
                        }
                        return;
                    }

                    if (Cache.Instance.InSpace)
                    {
                        Logging.Log(module, "Arrived at destination (in space, Questor stopped)", Logging.White);
                        Cache.Instance.Paused = true;
                        return;
                    }

                    Logging.Log(module, "Arrived at destination", Logging.White);
                    if (_States.CurrentCombatMissionBehaviorState == CombatMissionsBehaviorState.Traveler)
                    {
                        _States.CurrentCombatMissionBehaviorState = CombatMissionsBehaviorState.Idle;
                    }

                    if (_States.CurrentDedicatedBookmarkSalvagerBehaviorState == DedicatedBookmarkSalvagerBehaviorState.Traveler)
                    {
                        _States.CurrentDedicatedBookmarkSalvagerBehaviorState = DedicatedBookmarkSalvagerBehaviorState.Idle;
                    }

                    if (_States.CurrentCombatHelperBehaviorState == CombatHelperBehaviorState.Traveler)
                    {
                        _States.CurrentCombatHelperBehaviorState = CombatHelperBehaviorState.Idle;
                    }
                    return;
                }
            }
            return;
        }

        public static void ProcessState()
        {
            // Only pulse state changes every 1.5s
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < Time.Instance.QuestorPulse_milliseconds) //default: 1500ms
                return;

            _lastPulse = DateTime.UtcNow;

            switch (_States.CurrentTravelerState)
            {
                case TravelerState.Idle:
                    _States.CurrentTravelerState = TravelerState.Traveling;
                    break;

                case TravelerState.Traveling:
                    if (Cache.Instance.InWarp || (!Cache.Instance.InSpace && !Cache.Instance.InStation)) //if we are in warp, do nothing, as nothing can actually be done until we are out of warp anyway.
                        return;

                    if (Destination == null)
                    {
                        _States.CurrentTravelerState = TravelerState.Error;
                        break;
                    }

                    if (Destination.SolarSystemId != Cache.Instance.DirectEve.Session.SolarSystemId)
                    {
                        //Logging.Log("traveler: NavigateToBookmarkSystem(Destination.SolarSystemId);");
                        NavigateToBookmarkSystem(Destination.SolarSystemId);
                    }
                    else if (Destination.PerformFinalDestinationTask())
                    {
                        _destinationRoute = null;
                        _location = null;
                        _locationName = string.Empty;
                        _locationErrors = 0;

                        //Logging.Log("traveler: _States.CurrentTravelerState = TravelerState.AtDestination;");
                        _States.CurrentTravelerState = TravelerState.AtDestination;
                    }
                    break;

                case TravelerState.AtDestination:

                    //do nothing when at destination
                    //Traveler sits in AtDestination when it has nothing to do, NOT in idle.
                    break;

                default:
                    break;
            }
        }
    }
}