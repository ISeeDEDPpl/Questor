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
    using System.Linq;
    using DirectEve;
    using global::Questor.Modules.Caching;
    using global::Questor.Modules.Logging;
    using global::Questor.Modules.Lookup;

    public abstract class TravelerDestination
    {
        public long SolarSystemId { get; set; }

        public DirectBookmark UndockBookmark { get; set; }

        /// <summary>
        ///   This function returns true if we are at the final destination and false if the task is not yet complete
        /// </summary>
        /// <returns></returns>
        public abstract bool PerformFinalDestinationTask();
    }

    public class SolarSystemDestination : TravelerDestination
    {
        private DateTime _nextSolarSystemAction;

        public SolarSystemDestination(long solarSystemId)
        {
            Logging.Log("TravelerDestination.SolarSystemDestination", "Destination set to solar system id [" + solarSystemId + "]", Logging.White);
            SolarSystemId = solarSystemId;
        }

        public override bool PerformFinalDestinationTask()
        {
            // The destination is the solar system, not the station in the solar system.
            if (Cache.Instance.InStation && !Cache.Instance.InSpace)
            {
                if (_nextSolarSystemAction < DateTime.UtcNow)
                {
                    Logging.Log("TravelerDestination.SolarSystemDestination", "Exiting station", Logging.White);

                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdExitStation);
                    _nextSolarSystemAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerExitStationAmIInSpaceYet_seconds);
                }

                // We are not there yet
                return false;
            }

            // The task was to get to the solar system, we're there :)
            Logging.Log("TravelerDestination.SolarSystemDestination", "Arrived in system", Logging.White);
            return true;
        }
    }

    public class StationDestination : TravelerDestination
    {
        private DateTime _nextStationAction;
        private static int _undockAttempts = 0;

        public StationDestination(long stationId)
        {
            DirectLocation station = Cache.Instance.DirectEve.Navigation.GetLocation(stationId);
            if (station == null || !station.ItemId.HasValue || !station.SolarSystemId.HasValue)
            {
                Logging.Log("TravelerDestination.StationDestination", "Invalid station id [" + Logging.Yellow + StationId + Logging.Green + "]", Logging.Red);
                SolarSystemId = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;
                StationId = -1;
                StationName = "";
                return;
            }

            Logging.Log("TravelerDestination.StationDestination", "Destination set to [" + Logging.Yellow + station.Name + Logging.Green + "]", Logging.Green);
            StationId = stationId;
            StationName = station.Name;
            SolarSystemId = station.SolarSystemId.Value;
        }

        public StationDestination(long solarSystemId, long stationId, string stationName)
        {
            Logging.Log("TravelerDestination.StationDestination", "Destination set to [" + Logging.Yellow + stationName + Logging.Green + "]", Logging.Green);
            SolarSystemId = solarSystemId;
            StationId = stationId;
            StationName = stationName;
        }

        public long StationId { get; set; }

        public string StationName { get; set; }

        public override bool PerformFinalDestinationTask()
        {
            DirectBookmark localundockBookmark = UndockBookmark;
            bool arrived = PerformFinalDestinationTask(StationId, StationName, ref _nextStationAction, ref localundockBookmark);
            UndockBookmark = localundockBookmark;
            return arrived;
        }

        internal static bool PerformFinalDestinationTask(long stationId, string stationName, ref DateTime nextAction, ref DirectBookmark localundockBookmark)
        {
            if (Cache.Instance.InStation && Cache.Instance.DirectEve.Session.StationId == stationId)
            {
                Logging.Log("TravelerDestination.StationDestination", "Arrived in station", Logging.Green);
                return true;
            }

            if (Cache.Instance.InStation && DateTime.UtcNow > Cache.Instance.LastInSpace.AddSeconds(10))
            {
                // We are in a station, but not the correct station!
                if (DateTime.UtcNow > Cache.Instance.NextUndockAction)
                {
                    if (_undockAttempts > 10)
                    {
                        Logging.Log("TravelerDestination.StationDestination", "This is not the destination station, we have tried to undock [" + _undockAttempts + "] times - and it is evidentally not working (lag?) - restarting Questor (and EVE)", Logging.Green);
                        Cache.Instance.SessionState = "Quitting"; //this will perform a graceful restart
                    }

                    Logging.Log("TravelerDestination.StationDestination", "This is not the destination station, undocking from [" + Cache.Instance.DirectEve.GetLocationName(Cache.Instance.DirectEve.Session.StationId ?? 0) + "]", Logging.Green);

                    //if (!string.IsNullOrEmpty(Settings.Instance.UndockPrefix))
                    //{
                    //    var bookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.UndockPrefix).OrderByDescending(b => b.CreatedOn).Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId);
                    //    //var bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId).Where(b => b.Title.Contains(Settings.Instance.UndockPrefix)); //this does not handle more than one station undock bookmark per system and WILL likely warp to the wrong bm in that case
                    //    //var bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId).Where(b => b.Title.Contains(Cache.Instance.DirectEve.GetLocationName(Cache.Instance.DirectEve.Session.StationId ?? 0)) && b.Title.Contains(Settings.Instance.UndockPrefix));
                    //    //var bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.Title.Contains(Cache.Instance.DirectEve.GetLocationName(Cache.Instance.DirectEve.Session.StationId ?? 0)) && b.Title.Contains(Settings.Instance.UndockPrefix));
                    //    if (bookmarks != null && bookmarks.Count() > 0)
                    //    {
                    //        localundockBookmark = bookmarks.FirstOrDefault();
                    //        if (localundockBookmark.X == null || localundockBookmark.Y == null || localundockBookmark.Z == null)
                    //        {
                    //            Logging.Log("TravelerDestination.StationDestination: undock bookmark [" + localundockBookmark.Title + "] is unusable: it has no coords");
                    //            localundockBookmark = null;
                    //        }
                    //        else Logging.Log("TravelerDestination.StationDestination: undock bookmark [" + localundockBookmark.Title + "] is usable: it has coords");
                    //   }
                    //    else Logging.Log("TravelerDestination.StationDestination: you do not have an undock bookmark that has the prefix: " + Settings.Instance.UndockPrefix + " in local"); //+ Cache.Instance.DirectEve.GetLocationName((long)Cache.Instance.DirectEve.Session.StationId) + " and " + Settings.Instance.UndockPrefix + " did not both exist in a bookmark");
                    //}
                    //else Logging.Log("TravelerDestination.StationDestination: UndockPrefix is not configured");
                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdExitStation);
                    _undockAttempts++;
                    Cache.Instance.NextUndockAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerExitStationAmIInSpaceYet_seconds);
                    return false;
                }

                // We are not there yet
                return false;
            }

            if ((DateTime.UtcNow > Cache.Instance.LastInStation.AddSeconds(10)) && !Cache.Instance.InSpace)
            {
                // We are not in station and not in space?  Wait for a bit
                return false;
            }

            if (nextAction > DateTime.UtcNow)
                return false;

            _undockAttempts = 0;

            if (localundockBookmark != null)
            {
                double distance = Cache.Instance.DistanceFromMe(localundockBookmark.X ?? 0, localundockBookmark.Y ?? 0, localundockBookmark.Z ?? 0);
                if (distance < (int)Distance.WarptoDistance)
                {
                    Logging.Log("TravelerDestination.BookmarkDestination", "Arrived at undock bookmark [" + Logging.Yellow + localundockBookmark.Title + Logging.Green + "]", Logging.White);
                    localundockBookmark = null;
                }
                else
                {
                    Logging.Log("TravelerDestination.BookmarkDestination", "Warping to undock bookmark [" + Logging.Yellow + localundockBookmark.Title + Logging.Green + "][" + Math.Round((distance / 1000) / 149598000, 2) + " AU away]", Logging.White);

                    //if (!Combat.ReloadAll(Cache.Instance.EntitiesNotSelf.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < (double)Distance.OnGridWithMe))) return false;
                    localundockBookmark.WarpTo();
                    nextAction = DateTime.UtcNow.AddSeconds(10);
                    return false;
                }
            }

            //else Logging.Log("TravelerDestination.BookmarkDestination","undock bookmark missing: " + Cache.Instance.DirectEve.GetLocationName((long)Cache.Instance.DirectEve.Session.StationId) + " and " + Settings.Instance.UndockPrefix + " did not both exist in a bookmark");

            EntityCache entity = Cache.Instance.EntitiesByName(stationName).FirstOrDefault();
            if (entity == null)
            {
                // We are there but no station? Wait a bit
                return false;
            }

            if (entity.Distance < (int)Distance.DockingRange)
            {
                if (DateTime.UtcNow > Cache.Instance.NextDockAction)
                {
                    Logging.Log("TravelerDestination.StationDestination", "Dock at [" + Logging.Yellow + entity.Name + Logging.Green + "] which is [" + Math.Round(entity.Distance / 1000, 0) + "k away]", Logging.Green);
                    entity.Dock();

                    return false;
                }
            }
            else if (entity.Distance < (int)Distance.WarptoDistance)
            {
                if (DateTime.UtcNow > Cache.Instance.NextApproachAction)
                {
                    Logging.Log("TravelerDestintion.StationDestination", "Approaching[" + Logging.Yellow + entity.Name + Logging.Green + "] which is [" + Math.Round(entity.Distance / 1000, 0) + "k away]", Logging.Green);
                    entity.Approach();
                    return false;
                }
            }
            else
            {
                if (DateTime.UtcNow > Cache.Instance.NextDockAction)
                {
                    Logging.Log("TravelerDestination.StationDestination", "Warp to and dock at [" + Logging.Yellow + entity.Name + Logging.Green + "][" + Math.Round((entity.Distance / 1000) / 149598000, 2) + " AU away]", Logging.Green);
                    entity.WarpToAndDock();
                    return false;
                }
            }

            nextAction = DateTime.UtcNow.AddSeconds(20);
            return false;
        }
    }

    public class BookmarkDestination : TravelerDestination
    {
        private DateTime _nextBookmarkAction;

        public BookmarkDestination(DirectBookmark bookmark)
        {
            if (bookmark == null)
            {
                Logging.Log("TravelerDestination.BookmarkDestination", "Invalid bookmark destination!", Logging.Red);

                SolarSystemId = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;
                BookmarkId = -1;
                return;
            }

            Logging.Log("TravelerDestination.BookmarkDestination", "Destination set to bookmark [" + Logging.Yellow + bookmark.Title + Logging.Green + "]", Logging.Green);
            BookmarkId = bookmark.BookmarkId ?? -1;
            SolarSystemId = bookmark.LocationId ?? -1;
        }

        public BookmarkDestination(long bookmarkId)
            : this(Cache.Instance.BookmarkById(bookmarkId))
        {
        }

        public long BookmarkId { get; set; }

        public override bool PerformFinalDestinationTask()
        {
            DirectBookmark bookmark = Cache.Instance.BookmarkById(BookmarkId);
            DirectBookmark undockBookmark = UndockBookmark;
            bool arrived = PerformFinalDestinationTask(bookmark, 150000, ref _nextBookmarkAction, ref undockBookmark);
            UndockBookmark = undockBookmark;
            return arrived;
        }

        internal static bool PerformFinalDestinationTask(DirectBookmark bookmark, int warpDistance, ref DateTime nextAction, ref DirectBookmark undockBookmark)
        {
            // The bookmark no longer exists, assume we aren't there
            if (bookmark == null)
                return false;

            if (Cache.Instance.DirectEve.Session.IsInStation)
            {
                // We have arrived
                if (bookmark.ItemId.HasValue && bookmark.ItemId == Cache.Instance.DirectEve.Session.StationId)
                    return true;

                // We are apparently in a station that is incorrect
                Logging.Log("TravelerDestination.BookmarkDestination", "This is not the destination station, undocking", Logging.Green);

                //if (!string.IsNullOrEmpty(Settings.Instance.UndockPrefix))
                //{
                //    var bookmarks = Cache.Instance.BookmarksByLabel(Settings.Instance.UndockPrefix).OrderByDescending(b => b.CreatedOn).Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId);
                //    //var bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.LocationId == Cache.Instance.DirectEve.Session.SolarSystemId).OrderByDescending(b => b.CreatedOn).Where(b => b.Title.Contains(Settings.Instance.UndockPrefix)); //this does not handle more than one station undock bookmark per system and WILL likely warp to the wrong bm in that case
                //    //var bookmarks = Cache.Instance.DirectEve.Bookmarks.Where(b => b.Title.Contains(Cache.Instance.DirectEve.GetLocationName(Cache.Instance.DirectEve.Session.StationId ?? 0)) && b.Title.Contains(Settings.Instance.UndockPrefix));
                //    if (bookmarks != null && bookmarks.Count() > 0)
                //    {
                //        undockBookmark = bookmarks.FirstOrDefault();
                //        if (undockBookmark.X == null || undockBookmark.Y == null || undockBookmark.Z == null)
                //        {
                //            Logging.Log("TravelerDestination.BookmarkDestination","undock bookmark [" + undockBookmark.Title + "] is unusable: it has no coords");
                //            undockBookmark = null;
                //        }
                //        else Logging.Log("TravelerDestination.BookmarkDestination","undock bookmark [" + undockBookmark.Title + "] is usable: it has coords");
                //    }
                //    else Logging.Log("TravelerDestination.BookmarkDestination","you do not have an undock bookmark that contains [" + Settings.Instance.UndockPrefix + "] in local");
                //}
                //else Logging.Log("TravelerDestination.BookmarkDestination","UndockPrefix is not configured");
                Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdExitStation);
                nextAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerExitStationAmIInSpaceYet_seconds);
                return false;
            }

            // Is this a station bookmark?
            if (bookmark.Entity != null && bookmark.Entity.GroupId == (int)Group.Station)
            {
                bool arrived = StationDestination.PerformFinalDestinationTask(bookmark.Entity.Id, bookmark.Entity.Name, ref nextAction, ref undockBookmark);
                if (arrived)
                    Logging.Log("TravelerDestination.BookmarkDestination", "Arrived at bookmark [" + Logging.Yellow + bookmark.Title + Logging.Green + "]", Logging.Green);
                return arrived;
            }

            // Its not a station bookmark, make sure we are in space
            if (Cache.Instance.DirectEve.Session.IsInStation)
            {
                // We are in a station, but not the correct station!
                if (nextAction < DateTime.UtcNow)
                {
                    Logging.Log("TravelerDestination.BookmarkDestination", "We're docked but our destination is in space, undocking", Logging.Green);

                    Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdExitStation);
                    nextAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerExitStationAmIInSpaceYet_seconds);
                }

                // We are not there yet
                return false;
            }

            if (!Cache.Instance.InSpace)
            {
                // We are not in space and not in a station, wait a bit
                return false;
            }

            if (undockBookmark != null)
            {
                double distancetoundockbookmark = Cache.Instance.DistanceFromMe(bookmark.X ?? 0, bookmark.Y ?? 0, bookmark.Z ?? 0);
                if (distancetoundockbookmark < (int)Distance.WarptoDistance)
                {
                    Logging.Log("TravelerDestination.BookmarkDestination", "Arrived at undock bookmark [" + Logging.Yellow + undockBookmark.Title + Logging.Green + "]", Logging.Green);
                    undockBookmark = null;
                }
                else
                {
                    Logging.Log("TravelerDestination.BookmarkDestination", "Warping to undock bookmark [" + Logging.Yellow + undockBookmark.Title + Logging.Green + "][" + Logging.Yellow + Math.Round((distancetoundockbookmark / 1000) / 149598000, 2) + Logging.Green + " AU away]", Logging.Green);

                    //if (!Combat.ReloadAll(Cache.Instance.EntitiesNotSelf.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < (double)Distance.OnGridWithMe))) return false;
                    undockBookmark.WarpTo();
                    nextAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerInWarpedNextCommandDelay_seconds);
                    return false;
                }
            }

            // This bookmark has no x / y / z, assume we are there.
            if (bookmark.X == -1 || bookmark.Y == -1 || bookmark.Z == -1)
            {
                Logging.Log("TravelerDestination.BookmarkDestination", "Arrived at the bookmark [" + Logging.Yellow + bookmark.Title + Logging.Green + "][No XYZ]", Logging.Green);
                return true;
            }

            double distance = Cache.Instance.DistanceFromMe(bookmark.X ?? 0, bookmark.Y ?? 0, bookmark.Z ?? 0);
            if (distance < warpDistance)
            {
                Logging.Log("TravelerDestination.BookmarkDestination", "Arrived at the bookmark [" + Logging.Yellow + bookmark.Title + Logging.Green + "]", Logging.Green);
                return true;
            }

            if (nextAction > DateTime.UtcNow)
                return false;

            if (Math.Round((distance / 1000)) < (int)Distance.MaxPocketsDistanceKm && Cache.Instance.AccelerationGates.Count() != 0)
            {
                Logging.Log("QuestorManager.BookmarkDestination",
                "Warp to bookmark in same pocket requested but acceleration gate found delaying."
                , Logging.White);
                return true;
            }

            Logging.Log("TravelerDestination.BookmarkDestination", "Warping to bookmark [" + Logging.Yellow + bookmark.Title + Logging.Green + "][" + Logging.Yellow + Math.Round((distance / 1000) / 149598000, 2) + Logging.Green + " AU away]", Logging.Green);
            Cache.Instance.DoNotBreakInvul = false;

            //if (!Combat.ReloadAll(Cache.Instance.EntitiesNotSelf.OrderBy(t => t.Distance).FirstOrDefault(t => t.Distance < (double)Distance.OnGridWithMe))) return false;
            if (Cache.Instance.MissionWarpAtDistanceRange != 0 && bookmark.Title.Contains("Encounter"))
            {
                Logging.Log("TravelerDestination.BookmarkDestination", "Warping to bookmark [" + Logging.Yellow + bookmark.Title + Logging.Green + "][" + Logging.Yellow + " At " + Cache.Instance.MissionWarpAtDistanceRange + Logging.Green + " km]", Logging.Green);
                bookmark.WarpTo(Cache.Instance.MissionWarpAtDistanceRange * 1000);
            }
            else
            {
                bookmark.WarpTo();
            }
            nextAction = DateTime.UtcNow.AddSeconds(Time.Instance.TravelerInWarpedNextCommandDelay_seconds);
            return false;
        }
    }

    public class MissionBookmarkDestination : TravelerDestination
    {
        private DateTime _nextMissionBookmarkAction;

        public MissionBookmarkDestination(DirectAgentMissionBookmark bookmark)
        {
            if (bookmark == null)
            {
                if (!Cache.Instance.MissionBookmarkTimerSet)
                {
                    Cache.Instance.MissionBookmarkTimeout = DateTime.UtcNow.AddSeconds(10);
                }

                if (Cache.Instance.MissionBookmarkTimeout > DateTime.UtcNow)
                {
                    AgentId = -1;
                    Title = null;
                    SolarSystemId = Cache.Instance.DirectEve.Session.SolarSystemId ?? -1;

                    //Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdLogOff);
                    //Cache.Instance.DirectEve.ExecuteCommand(DirectCmd.CmdQuitGame);

                    Cache.Instance.CloseQuestorCMDLogoff = false;
                    Cache.Instance.CloseQuestorCMDExitGame = true;
                    Cache.Instance.ReasonToStopQuestor = "TravelerDestination.MissionBookmarkDestination: Invalid mission bookmark! - Lag?! Closing EVE";
                    Logging.Log("TravelerDestination", Cache.Instance.ReasonToStopQuestor, Logging.Red);
                    Cache.Instance.SessionState = "Quitting";
                }
                else
                {
                    Logging.Log("TravelDestination.MissionBookmarkDestination", "Invalid Mission Bookmark! retrying for another [ " + Math.Round(Cache.Instance.MissionBookmarkTimeout.Subtract(DateTime.UtcNow).TotalSeconds, 0) + " ]sec", Logging.Green);
                }
            }

            if (bookmark != null)
            {
                Logging.Log("TravelerDestination.MissionBookmarkDestination", "Destination set to mission bookmark [" + Logging.Yellow + bookmark.Title + Logging.Green + "]", Logging.Green);
                AgentId = bookmark.AgentId ?? -1;
                Title = bookmark.Title;
                SolarSystemId = bookmark.SolarSystemId ?? -1;
            }
        }

        public MissionBookmarkDestination(int agentId, string title)
            : this(GetMissionBookmark(agentId, title))
        {
        }

        public long AgentId { get; set; }

        public string Title { get; set; }

        private static DirectAgentMissionBookmark GetMissionBookmark(long agentId, string title)
        {
            DirectAgentMission mission = Cache.Instance.GetAgentMission(agentId, true);
            if (mission == null)
                return null;

            return mission.Bookmarks.FirstOrDefault(b => b.Title.ToLower() == title.ToLower());
        }

        public override bool PerformFinalDestinationTask()
        {
            DirectBookmark undockBookmark = UndockBookmark;
            bool arrived = BookmarkDestination.PerformFinalDestinationTask(GetMissionBookmark(AgentId, Title), (int)Distance.MissionWarpLimit, ref _nextMissionBookmarkAction, ref undockBookmark);
            UndockBookmark = undockBookmark;
            return arrived;// Mission bookmarks have a 1.000.000 distance warp-to limit (changed it to 150.000.000 as there are some bugged missions around)
        }
    }
}